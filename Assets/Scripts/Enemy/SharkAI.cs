using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Shark AI featuring waypoint patrol, player detection (only when player is in water),
/// chase mode, bite attack lunge with cooldown, strict anti-flight water surface clamping,
/// clean transform turning without needle distortion, banking, and procedural tail wave coupling.
/// </summary>
public class SharkAI : MonoBehaviour
{
    public enum SharkState
    {
        Patrol,
        Chase,
        Attack
    }

    [Header("Current State (View Only)")]
    [SerializeField] private SharkState currentState = SharkState.Patrol;

    [Header("Patrol Movement")]
    [Tooltip("Waypoints that the shark will patrol in sequence.")]
    [SerializeField] private Transform[] waypoints;

    [FormerlySerializedAs("swimSpeed")]
    [Tooltip("Forward swimming speed along transform.forward in units/sec during patrol.")]
    [SerializeField] private float speed = 3.5f;

    [FormerlySerializedAs("turnSpeed")]
    [Tooltip("Turning rate in degrees per second during patrol.")]
    [SerializeField] private float turnSpeed = 60.0f;

    [FormerlySerializedAs("waypointTolerance")]
    [Tooltip("Distance threshold to advance to the next waypoint.")]
    [SerializeField] private float arrivalDistance = 2.0f;

    [Header("Player Detection & Chase")]
    [Tooltip("Tag used to find the player GameObject.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Direct reference to the player. Auto-located via playerTag if unassigned.")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("Radius within which the shark detects a submerged player.")]
    [SerializeField] private float detectionRadius = 12.0f;

    [Tooltip("Distance at which the shark loses the player and returns to patrol.")]
    [SerializeField] private float loseSightRadius = 18.0f;

    [Tooltip("Swimming speed when chasing the player.")]
    [SerializeField] private float chaseSpeed = 6.5f;

    [Tooltip("Turning rate in degrees per second when chasing the player.")]
    [SerializeField] private float chaseTurnSpeed = 90.0f;

    [Header("Water & Anti-Flight Boundary")]
    [Tooltip("Optional transform marking the water surface. If unassigned, waterSurfaceY is used.")]
    [SerializeField] private Transform waterLevelTransform;

    [Tooltip("World Y height of the water surface (e.g. 0.0).")]
    [SerializeField] private float waterSurfaceY = 0.0f;

    [Tooltip("Margin below the water surface that the shark's center is kept under (prevents shark/dorsal fin from flying out).")]
    [SerializeField] private float surfaceOffset = 0.5f;

    [Tooltip("Tolerance above water surface where player is still considered 'in water' (e.g. wading or swimming at surface).")]
    [SerializeField] private float playerWaterTolerance = 0.5f;

    [Header("Bite Attack & Lunge")]
    [Tooltip("Distance threshold to trigger bite attack.")]
    [SerializeField] private float attackRange = 2.5f;

    [Tooltip("Damage dealt to the player on bite (sent via TakeDamage message).")]
    [SerializeField] private int attackDamage = 25;

    [Tooltip("Cooldown in seconds between bite lunges.")]
    [SerializeField] private float attackCooldown = 2.0f;

    [Tooltip("Duration in seconds of the forward lunge burst.")]
    [SerializeField] private float lungeDuration = 0.35f;

    [Tooltip("Speed during the forward lunge burst.")]
    [SerializeField] private float lungeSpeed = 9.0f;

    [Header("Model & Forward Orientation")]
    [Tooltip("Child transform containing the shark mesh (e.g. SharkV1). Auto-assigned if empty.")]
    [SerializeField] private Transform sharkModel;

    [Tooltip("Initial local rotation of sharkModel to preserve (e.g. Y = 90 for meshes modeled facing +X).")]
    [SerializeField] private Vector3 modelRotationOffset = Vector3.zero;

    [Header("Tail Swimming Wave")]
    [Tooltip("If true, drives CPU procedural tail swinging (SharkTailWave). If false, uses Shader Graph.")]
    [SerializeField] private bool useCpuTailWave = true;

    [Tooltip("CPU procedural tail wave component. Auto-located or auto-added at runtime if missing.")]
    [SerializeField] private SharkTailWave tailWave;

    [Tooltip("Tail wave oscillation speed at normal patrol speed.")]
    [SerializeField] private float idleWaveSpeed = 6.0f;

    [Tooltip("Tail wave oscillation speed at chase speed.")]
    [SerializeField] private float maxWaveSpeed = 10.0f;

    [Header("Banking")]
    [FormerlySerializedAs("maxBankAngle")]
    [Tooltip("Maximum roll angle in degrees on local Z proportional to turning rate.")]
    [SerializeField] private float bankAmount = 15.0f;

    [Tooltip("Smooth damp time for banking roll.")]
    [SerializeField] private float bankSmoothTime = 0.25f;

    [Header("Shader Graph Coupling (Optional)")]
    [Tooltip("Renderer using SharkSwimShader. If unassigned, automatically found in children.")]
    [SerializeField] private Renderer sharkRenderer;

    [Tooltip("Tail sway amplitude at patrol speed.")]
    [SerializeField] private float idleAmplitude = 0.12f;

    [Tooltip("Tail sway amplitude at max speed.")]
    [SerializeField] private float maxAmplitude = 0.26f;

    [Tooltip("Smooth damp time for the _Turn shader property.")]
    [SerializeField] private float turnSmoothTime = 0.15f;

    [Header("Movement Constraints")]
    [Tooltip("If true, shark pitches up/down toward waypoints and player. If false, shark stays on horizontal plane.")]
    [SerializeField] private bool allow3DMovement = true;

    // Movement hierarchy state
    private Transform moveTransform;
    private Quaternion initialModelLocalRotation;
    private bool hasModelOffset = false;

    // Navigation & turning state
    private int currentWaypointIndex = 0;
    private float previousHeadingYaw = 0.0f;
    private float currentTurn = 0.0f;
    private float turnVelocity = 0.0f;
    private float currentBank = 0.0f;
    private float bankVelocity = 0.0f;
    private Quaternion headingRotation;

    // Attack & Cooldown timers
    private float attackCooldownTimer = 0.0f;
    private float lungeTimer = 0.0f;

    // Accumulated wave phase (for Shader Graph fallback)
    private float accumulatedPhase = 0.0f;

    // MaterialPropertyBlock cache & Shader property IDs
    private MaterialPropertyBlock propertyBlock;
    private static readonly int PhasePropId = Shader.PropertyToID("_Phase");
    private static readonly int SwimSpeedPropId = Shader.PropertyToID("_SwimSpeed");
    private static readonly int AmplitudePropId = Shader.PropertyToID("_Amplitude");
    private static readonly int TurnPropId = Shader.PropertyToID("_Turn");

    public SharkState CurrentState => currentState;

    private void Awake()
    {
        // 1. Determine moving transform (Root vs Child Model)
        if (transform.parent != null && (transform.parent.name.Contains("Shark") || transform.parent.name.Contains("Root")))
        {
            moveTransform = transform.parent;
            sharkModel = transform;
        }
        else
        {
            moveTransform = transform;
            if (sharkModel == null && transform.childCount > 0)
            {
                sharkModel = transform.GetChild(0);
            }
        }

        headingRotation = moveTransform.rotation;
        previousHeadingYaw = moveTransform.eulerAngles.y;
        propertyBlock = new MaterialPropertyBlock();

        if (sharkModel != null && sharkModel != moveTransform)
        {
            initialModelLocalRotation = sharkModel.localRotation;
            hasModelOffset = true;
        }

        // 2. Locate or auto-attach SharkTailWave
        tailWave = GetComponent<SharkTailWave>();
        if (tailWave == null)
        {
            tailWave = GetComponentInChildren<SharkTailWave>();
        }
        if (tailWave == null && moveTransform != transform)
        {
            tailWave = moveTransform.GetComponentInChildren<SharkTailWave>();
        }
        if (tailWave == null)
        {
            MeshFilter mf = GetComponent<MeshFilter>() ?? GetComponentInChildren<MeshFilter>();
            if (mf == null && moveTransform != null)
            {
                mf = moveTransform.GetComponentInChildren<MeshFilter>();
            }

            if (mf != null)
            {
                tailWave = mf.gameObject.AddComponent<SharkTailWave>();
                tailWave.headAtPositiveEnd = false;
                Debug.Log($"[SharkAI] Auto-added SharkTailWave to '{mf.gameObject.name}'.", this);
            }
        }

        // 3. Locate Renderer
        if (sharkRenderer == null)
        {
            sharkRenderer = GetComponentInChildren<Renderer>();
        }

        // 4. Set rigidbodies to kinematic to prevent physics gravity drop
        foreach (var rb in moveTransform.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void Start()
    {
        // Find player if not assigned
        FindPlayer();

        // Run diagnostics
        RunDebugChecklist();

        if (waypoints != null && waypoints.Length > 0 && waypoints[0] != null)
        {
            Debug.Log($"[SharkAI] Starting patrol. Swimming to: {waypoints[currentWaypointIndex].name}", this);
        }
    }

    private void Update()
    {
        // Cooldown timer
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
        }

        // Ensure player reference is kept updated
        if (playerTransform == null)
        {
            FindPlayer();
        }

        // State Machine
        switch (currentState)
        {
            case SharkState.Patrol:
                HandlePatrolState();
                break;

            case SharkState.Chase:
                HandleChaseState();
                break;

            case SharkState.Attack:
                HandleAttackState();
                break;
        }

        // Hard clamp: shark MUST stay under the water surface ceiling
        ClampToWaterBoundary();

        // Update tail undulation
        UpdateTailAnimation();
    }

    #region State Logic

    private void HandlePatrolState()
    {
        // Check if player entered detection radius while submerged in water
        if (playerTransform != null && IsPlayerInWater())
        {
            float distToPlayer = Vector3.Distance(moveTransform.position, playerTransform.position);
            if (distToPlayer <= detectionRadius)
            {
                SwitchState(SharkState.Chase);
                return;
            }
        }

        // Waypoint Navigation
        if (waypoints == null || waypoints.Length == 0)
        {
            moveTransform.position += moveTransform.forward * (speed * Time.deltaTime);
            return;
        }

        Transform targetWaypoint = waypoints[currentWaypointIndex];
        if (targetWaypoint == null)
        {
            AdvanceToNextWaypoint();
            return;
        }

        float distanceToWaypoint = Vector3.Distance(moveTransform.position, targetWaypoint.position);
        if (distanceToWaypoint <= arrivalDistance)
        {
            AdvanceToNextWaypoint();
            targetWaypoint = waypoints[currentWaypointIndex];
        }

        // Steer toward waypoint (with height clamped below water surface)
        Vector3 targetPos = targetWaypoint.position;
        targetPos.y = Mathf.Min(targetPos.y, GetMaxAllowedY());
        RotateTowardsTarget(targetPos, turnSpeed);

        // Move forward along transform.forward
        moveTransform.position += moveTransform.forward * (speed * Time.deltaTime);
    }

    private void HandleChaseState()
    {
        // Disengage if player is lost or leaves water (climbs onto boat/dock/land)
        if (playerTransform == null || !IsPlayerInWater())
        {
            Debug.Log("[SharkAI] Player left the water or was lost. Returning to patrol.", this);
            SwitchState(SharkState.Patrol);
            return;
        }

        float distToPlayer = Vector3.Distance(moveTransform.position, playerTransform.position);
        if (distToPlayer > loseSightRadius)
        {
            Debug.Log("[SharkAI] Lost sight of player. Returning to patrol.", this);
            SwitchState(SharkState.Patrol);
            return;
        }

        // Trigger Bite Attack when close enough and off cooldown
        if (distToPlayer <= attackRange && attackCooldownTimer <= 0f)
        {
            SwitchState(SharkState.Attack);
            return;
        }

        // Steer toward player (aiming NO higher than the water ceiling)
        Vector3 targetPos = playerTransform.position;
        targetPos.y = Mathf.Min(targetPos.y, GetMaxAllowedY());
        RotateTowardsTarget(targetPos, chaseTurnSpeed);

        // Move forward at chase speed
        moveTransform.position += moveTransform.forward * (chaseSpeed * Time.deltaTime);
    }

    private void HandleAttackState()
    {
        lungeTimer -= Time.deltaTime;

        // Forward burst lunge
        moveTransform.position += moveTransform.forward * (lungeSpeed * Time.deltaTime);

        // When lunge completes, return to chase while on cooldown
        if (lungeTimer <= 0f)
        {
            SwitchState(SharkState.Chase);
        }
    }

    private void SwitchState(SharkState newState)
    {
        if (currentState == newState) return;

        currentState = newState;

        switch (currentState)
        {
            case SharkState.Patrol:
                break;

            case SharkState.Chase:
                Debug.Log("<color=yellow><b>[SharkAI] Player spotted in water! Chasing player.</b></color>", this);
                break;

            case SharkState.Attack:
                Debug.Log("<color=red><b>[SharkAI] Bite Attack Lunge!</b></color>", this);
                lungeTimer = lungeDuration;
                attackCooldownTimer = attackCooldown;

                // Deal damage to player
                if (playerTransform != null)
                {
                    playerTransform.SendMessage("TakeDamage", attackDamage, SendMessageOptions.DontRequireReceiver);
                }
                break;
        }
    }

    #endregion

    #region Water Boundaries & Anti-Flight

    /// <summary>
    /// Gets the world Y coordinate of the water surface.
    /// </summary>
    public float GetWaterLevel()
    {
        return (waterLevelTransform != null) ? waterLevelTransform.position.y : waterSurfaceY;
    }

    /// <summary>
    /// Gets the maximum allowed Y coordinate for the shark's center to ensure it stays submerged.
    /// </summary>
    public float GetMaxAllowedY()
    {
        return GetWaterLevel() - surfaceOffset;
    }

    /// <summary>
    /// Checks whether the player is currently submerged or swimming in the water.
    /// </summary>
    public bool IsPlayerInWater()
    {
        if (playerTransform == null) return false;
        float waterCeiling = GetWaterLevel() + playerWaterTolerance;
        return playerTransform.position.y <= waterCeiling;
    }

    /// <summary>
    /// Hard-clamps the shark's position so it can NEVER leave the water or fly.
    /// </summary>
    private void ClampToWaterBoundary()
    {
        Vector3 pos = moveTransform.position;
        float maxY = GetMaxAllowedY();

        if (pos.y > maxY)
        {
            pos.y = maxY;
            moveTransform.position = pos;
        }
    }

    #endregion

    #region Turning & Orientation

    /// <summary>
    /// Steers the shark smoothly toward targetPos with bounded pitch and banking.
    /// </summary>
    private void RotateTowardsTarget(Vector3 targetPos, float currentTurnRate)
    {
        Vector3 directionToTarget = targetPos - moveTransform.position;
        if (!allow3DMovement)
        {
            directionToTarget.y = 0.0f;
        }

        if (directionToTarget.sqrMagnitude < 0.0001f) return;

        // Base target heading
        Quaternion rawTargetRotation = Quaternion.LookRotation(directionToTarget.normalized, Vector3.up);

        // Clamp pitch angle so shark never aims vertically up into the air or nose-dives
        Vector3 targetEuler = rawTargetRotation.eulerAngles;
        float pitch = Mathf.DeltaAngle(0, targetEuler.x);
        pitch = Mathf.Clamp(pitch, -22.0f, 22.0f); // max 22 degrees up or down
        Quaternion targetRotation = Quaternion.Euler(pitch, targetEuler.y, 0.0f);

        // Smooth Quaternion turn towards target (no needle distortion)
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        headingRotation = Quaternion.RotateTowards(headingRotation, targetRotation, currentTurnRate * dt);

        // Compute yaw delta for banking
        float currentYaw = headingRotation.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(previousHeadingYaw, currentYaw);
        previousHeadingYaw = currentYaw;

        float angularVelocity = yawDelta / dt;
        float maxTurnRate = Mathf.Max(currentTurnRate, 1.0f);
        float rawTurn = Mathf.Clamp(angularVelocity / maxTurnRate, -1.0f, 1.0f);

        currentTurn = Mathf.SmoothDamp(currentTurn, rawTurn, ref turnVelocity, turnSmoothTime);

        // Roll on local Z proportional to turn rate
        float targetBankAngle = Mathf.Clamp(-currentTurn * bankAmount, -35.0f, 35.0f);
        currentBank = Mathf.SmoothDamp(currentBank, targetBankAngle, ref bankVelocity, bankSmoothTime);

        // Apply rotation to moving root
        moveTransform.rotation = headingRotation * Quaternion.Euler(0.0f, 0.0f, currentBank);

        // Preserve model visual offset (e.g. Y = 90)
        if (hasModelOffset && sharkModel != null && sharkModel != moveTransform)
        {
            sharkModel.localRotation = initialModelLocalRotation;
        }
    }

    #endregion

    #region Tail Swimming Coupling

    private void UpdateTailAnimation()
    {
        // 1. CPU Procedural Tail Wave
        if (tailWave != null)
        {
            tailWave.enabled = useCpuTailWave;
            if (useCpuTailWave)
            {
                switch (currentState)
                {
                    case SharkState.Patrol:
                        tailWave.swim = 1.0f;
                        tailWave.speed = idleWaveSpeed;
                        break;

                    case SharkState.Chase:
                        tailWave.swim = 1.2f;
                        tailWave.speed = maxWaveSpeed;
                        break;

                    case SharkState.Attack:
                        tailWave.swim = 1.4f;
                        tailWave.speed = maxWaveSpeed * 1.25f;
                        break;
                }
            }
        }

        // 2. Shader Graph Fallback
        if (sharkRenderer != null)
        {
            float currentSpeed = (currentState == SharkState.Chase) ? chaseSpeed : ((currentState == SharkState.Attack) ? lungeSpeed : speed);
            float currentWaveFreq = (currentState == SharkState.Patrol) ? idleWaveSpeed : maxWaveSpeed;

            accumulatedPhase += currentWaveFreq * Time.deltaTime;
            if (accumulatedPhase > 628.3185f)
            {
                accumulatedPhase -= 628.3185f;
            }

            sharkRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(PhasePropId, accumulatedPhase);
            propertyBlock.SetFloat(SwimSpeedPropId, (currentState == SharkState.Patrol) ? 0.5f : 1.0f);
            propertyBlock.SetFloat(AmplitudePropId, useCpuTailWave ? 0f : (currentState == SharkState.Patrol ? idleAmplitude : maxAmplitude));
            propertyBlock.SetFloat(TurnPropId, currentTurn);
            sharkRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    #endregion

    private void AdvanceToNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
    }

    private void FindPlayer()
    {
        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject playerObj = GameObject.FindWithTag(playerTag);
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }
    }

    #region Diagnostics

    [ContextMenu("Run Swim Debug Checklist")]
    public void RunDebugChecklist()
    {
        Debug.Log("<color=cyan><b>[SharkAI] === SHARK SWIM DIAGNOSTIC CHECKLIST ===</b></color>", this);
        Debug.Log($"[SharkAI] Current State: {currentState} | Water Surface Y: {GetWaterLevel():F2} | Max Allowed Y: {GetMaxAllowedY():F2}", this);

        if (playerTransform != null)
        {
            Debug.Log($"[SharkAI] Player found: '{playerTransform.name}' (In Water: {IsPlayerInWater()})", playerTransform);
        }
        else
        {
            Debug.LogWarning($"[SharkAI] No GameObject with tag '{playerTag}' currently found in scene.", this);
        }

        if (tailWave != null)
        {
            Debug.Log($"[SharkAI] SharkTailWave is ACTIVE on '{tailWave.gameObject.name}'.", tailWave);
        }

        Debug.Log("<color=cyan><b>[SharkAI] === DIAGNOSTIC CHECKLIST COMPLETE ===</b></color>", this);
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Transform t = (moveTransform != null) ? moveTransform : transform;

        // Forward heading ray
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(t.position, t.forward * 3.5f);

        // Arrival distance
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(t.position, arrivalDistance);

        // Player Detection Radius (Yellow)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(t.position, detectionRadius);

        // Lose Sight Radius (Orange)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(t.position, loseSightRadius);

        // Attack Range (Red)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(t.position, attackRange);

        // Water Ceiling Boundary (Cyan Plane)
        float waterY = GetWaterLevel();
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.4f);
        Vector3 waterCenter = new Vector3(t.position.x, waterY, t.position.z);
        Gizmos.DrawWireCube(waterCenter, new Vector3(detectionRadius * 2f, 0.05f, detectionRadius * 2f));

        // Waypoints path
        if (waypoints != null && waypoints.Length > 1)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] != null)
                {
                    Transform next = waypoints[(i + 1) % waypoints.Length];
                    if (next != null)
                    {
                        Gizmos.DrawLine(waypoints[i].position, next.position);
                    }
                    Gizmos.DrawSphere(waypoints[i].position, 0.3f);
                }
            }
        }
    }
}
