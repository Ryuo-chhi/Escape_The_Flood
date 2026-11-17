using UnityEngine;
using UnityEngine.Serialization;
using EscapeTheFlood.Water;

/// <summary>
/// Shark AI featuring 3D random wandering without waypoints, smooth vertical ascending/diving,
/// XZ-plane steering with realistic pitch tilting and banking, player detection (when player is in water),
/// chase mode, bite attack lunge with cooldown, and procedural tail wave coupling.
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

    [Header("Patrol & Random Wander Movement")]
    [Tooltip("If true, shark roams randomly within the water volume without requiring waypoints.")]
    [SerializeField] private bool useRandomWander = true;

    [Tooltip("Waypoints used only as optional fallback if useRandomWander is false.")]
    [SerializeField] private Transform[] waypoints;

    [FormerlySerializedAs("swimSpeed")]
    [Tooltip("Forward swimming speed along transform.forward in units/sec during patrol.")]
    [SerializeField] private float speed = 3.5f;

    [FormerlySerializedAs("turnSpeed")]
    [Tooltip("Turning rate in degrees per second during patrol.")]
    [SerializeField] private float turnSpeed = 60.0f;

    [FormerlySerializedAs("waypointTolerance")]
    [Tooltip("Distance threshold to advance to the next target in the XZ plane.")]
    [SerializeField] private float arrivalDistance = 1.5f;

    [Tooltip("Timeout in seconds before picking a new patrol target if arrival was not reached (prevents infinite orbits).")]
    [SerializeField] private float retargetInterval = 6.0f;

    [Tooltip("Speed at which the shark's Y position smoothly Lerps to stay inside the water volume.")]
    [SerializeField] private float depthLerpSpeed = 2.0f;

    [Header("Vertical Depth & Pitch Swimming")]
    [Tooltip("Minimum normalized depth (0 = floor, 1 = surface) for random patrol targets.")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float minPatrolDepth = 0.15f;

    [Tooltip("Maximum normalized depth (0 = floor, 1 = surface) for random patrol targets.")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float maxPatrolDepth = 0.85f;

    [Tooltip("Safety margin above the floor slab in world units to prevent floor clipping.")]
    [SerializeField] private float bottomMargin = 0.8f;

    [Tooltip("If true, shark smoothly pitches its nose up when ascending and down when diving.")]
    [SerializeField] private bool allowPitchTilt = true;

    [Tooltip("Maximum pitch angle in degrees when swimming up or down.")]
    [Range(5f, 35f)]
    [SerializeField] private float maxPitchAngle = 18.0f;

    [Tooltip("Strength of pitch tilt relative to vertical swimming velocity.")]
    [SerializeField] private float pitchStrength = 12.0f;

    [Tooltip("Smooth damp time for pitch transitions.")]
    [SerializeField] private float pitchSmoothTime = 0.2f;

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

    [Header("Water & Boundary Constraints")]
    [Tooltip("Optional reference to WaterBody simulation. If unassigned, automatically finds WaterBody.Instance in scene.")]
    [SerializeField] private WaterBody waterBody;

    [Tooltip("Optional fallback transform marking the water surface. If unassigned, waterSurfaceY is used.")]
    [SerializeField] private Transform waterLevelTransform;

    [Tooltip("World Y height of the water surface fallback.")]
    [SerializeField] private float waterSurfaceY = 0.0f;

    [Tooltip("Margin below the water surface that the shark's center is kept under (prevents dorsal fin from flying out).")]
    [SerializeField] private float surfaceOffset = 0.5f;

    [Tooltip("Tolerance above water surface where player is still considered 'in water'.")]
    [SerializeField] private float playerWaterTolerance = 0.5f;

    [Tooltip("Horizontal safety margin inside the water perimeter to ensure shark model never pokes outside.")]
    [SerializeField] private float boundaryMargin = 3.0f;

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

    // Movement hierarchy state
    private Transform moveTransform;
    private Quaternion initialModelLocalRotation;
    private bool hasModelOffset = false;

    // Navigation & patrol state
    private Vector3 currentPatrolTarget;
    private float targetDepthNormalized = 0.5f;
    private float retargetTimer = 0.0f;
    private int currentWaypointIndex = 0;
    private float previousHeadingYaw = 0.0f;
    private float currentTurn = 0.0f;
    private float turnVelocity = 0.0f;
    private float currentBank = 0.0f;
    private float bankVelocity = 0.0f;
    private float currentPitch = 0.0f;
    private float pitchVelocity = 0.0f;
    private float verticalVelocity = 0.0f;
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
    public Vector3 CurrentPatrolTarget => currentPatrolTarget;
    public bool UseRandomWander
    {
        get => useRandomWander;
        set => useRandomWander = value;
    }

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

        // 2. Unparent from water if accidentally parented under water (prevents inheriting water scale/pos)
        if (moveTransform.parent != null && moveTransform.GetComponentInParent<WaterBody>() != null)
        {
            Debug.LogWarning("[SharkAI] Shark was parented under WaterBody! Unparenting to prevent inheriting water scale and position.", this);
            moveTransform.SetParent(null);
        }

        headingRotation = moveTransform.rotation;
        previousHeadingYaw = moveTransform.eulerAngles.y;
        propertyBlock = new MaterialPropertyBlock();

        // 3. Preserve child model local rotation offset (e.g. Y = 90 when model faces +X)
        if (sharkModel != null && sharkModel != moveTransform)
        {
            initialModelLocalRotation = sharkModel.localRotation;
            hasModelOffset = true;
        }

        // 4. Locate or auto-attach SharkTailWave
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

        // 5. Locate Renderer
        if (sharkRenderer == null)
        {
            sharkRenderer = GetComponentInChildren<Renderer>();
        }

        // 6. Set rigidbodies to kinematic to prevent physics gravity drop
        foreach (var rb in moveTransform.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 7. Connect to WaterBody if unassigned
        if (waterBody == null)
        {
            waterBody = WaterBody.Instance ?? FindFirstObjectByType<WaterBody>();
        }
    }

    private void Start()
    {
        // Double-check water parenting
        if (moveTransform.parent != null && waterBody != null && moveTransform.IsChildOf(waterBody.transform))
        {
            Debug.LogWarning("[SharkAI] Shark was parented under WaterBody! Unparenting to prevent inheriting water scale and position.", this);
            moveTransform.SetParent(null);
        }

        // Compute initial random wander target inside water volume
        PickNewPatrolTarget();

        // Find player if not assigned
        FindPlayer();

        // Run diagnostics
        RunDebugChecklist();

        Debug.Log($"[SharkAI] Initial patrol target: {currentPatrolTarget} (Depth: {targetDepthNormalized:P0})", this);
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

        // Hard clamp: shark MUST stay within water horizontal boundary, above floor, and below surface
        ClampToWaterBoundary();

        // Update tail undulation
        UpdateTailAnimation();
    }

    #region State Logic

    private void HandlePatrolState()
    {
        // 1. Check if player entered detection radius while submerged in water
        if (playerTransform != null && IsPlayerInWater())
        {
            Vector3 toPlayerXZ = playerTransform.position - moveTransform.position;
            toPlayerXZ.y = 0.0f;
            if (toPlayerXZ.magnitude <= detectionRadius)
            {
                SwitchState(SharkState.Chase);
                return;
            }
        }

        // 2. Retarget timer countdown (prevents orbiting forever)
        retargetTimer -= Time.deltaTime;

        // 3. Compute horizontal direction and distance on XZ plane only
        Vector3 toTargetXZ = currentPatrolTarget - moveTransform.position;
        toTargetXZ.y = 0.0f;
        float distanceXZ = toTargetXZ.magnitude;

        // 4. Arrive check (distance < ~1.5 in XZ) OR retarget timer (~6s)
        if (distanceXZ <= arrivalDistance || retargetTimer <= 0f)
        {
            PickNewPatrolTarget();
            toTargetXZ = currentPatrolTarget - moveTransform.position;
            toTargetXZ.y = 0.0f;
        }

        // 5. Steer on XZ plane with Quaternion.RotateTowards and smooth pitch tilt
        RotateTowardsTargetXZ(toTargetXZ, turnSpeed);

        // 6. Move forward along transform.forward
        moveTransform.position += moveTransform.forward * (speed * Time.deltaTime);

        // 7. Handle depth separately: smoothly Lerp shark Y to stay at target depth inside water volume
        float targetY = GetCurrentPatrolTargetY();
        UpdateDepth(targetY);
    }

    private void HandleChaseState()
    {
        // Disengage if player is lost or leaves water
        if (playerTransform == null || !IsPlayerInWater())
        {
            Debug.Log("[SharkAI] Player left the water or was lost. Returning to patrol.", this);
            SwitchState(SharkState.Patrol);
            return;
        }

        Vector3 toPlayerXZ = playerTransform.position - moveTransform.position;
        toPlayerXZ.y = 0.0f;
        float distToPlayerXZ = toPlayerXZ.magnitude;

        if (distToPlayerXZ > loseSightRadius)
        {
            Debug.Log("[SharkAI] Lost sight of player. Returning to patrol.", this);
            SwitchState(SharkState.Patrol);
            return;
        }

        // Trigger Bite Attack when close enough in XZ and off cooldown
        if (distToPlayerXZ <= attackRange && attackCooldownTimer <= 0f)
        {
            SwitchState(SharkState.Attack);
            return;
        }

        // Steer toward player on XZ plane
        RotateTowardsTargetXZ(toPlayerXZ, chaseTurnSpeed);

        // Move forward along transform.forward at chase speed
        moveTransform.position += moveTransform.forward * (chaseSpeed * Time.deltaTime);

        // Handle depth separately: smoothly Lerp shark Y toward player Y (bounded inside water)
        float targetY = Mathf.Min(playerTransform.position.y, GetMaxAllowedY());
        if (waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();
            targetY = Mathf.Max(targetY, b.min.y + bottomMargin);
        }
        UpdateDepth(targetY);
    }

    private void HandleAttackState()
    {
        lungeTimer -= Time.deltaTime;

        // Forward burst lunge along transform.forward
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
                PickNewPatrolTarget();
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

    #region Target Selection & Depth Management

    /// <summary>
    /// Computes random 3D targets inside the water volume with varying depths, without requiring waypoints.
    /// Resets the retarget timer to retargetInterval (~6s).
    /// </summary>
    public void PickNewPatrolTarget()
    {
        retargetTimer = retargetInterval;

        // 1. Random wander within WaterBody volume (no waypoints needed)
        if (useRandomWander && waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();
            float minX = b.min.x + boundaryMargin;
            float maxX = b.max.x - boundaryMargin;
            float minZ = b.min.z + boundaryMargin;
            float maxZ = b.max.z - boundaryMargin;

            if (minX > maxX)
            {
                float midX = (b.min.x + b.max.x) * 0.5f;
                minX = midX;
                maxX = midX;
            }
            if (minZ > maxZ)
            {
                float midZ = (b.min.z + b.max.z) * 0.5f;
                minZ = midZ;
                maxZ = midZ;
            }

            float randX = Random.Range(minX, maxX);
            float randZ = Random.Range(minZ, maxZ);

            // Random depth ratio between minPatrolDepth and maxPatrolDepth so shark ascends and dives
            targetDepthNormalized = Random.Range(minPatrolDepth, maxPatrolDepth);
            float targetY = GetCurrentPatrolTargetY();

            currentPatrolTarget = new Vector3(randX, targetY, randZ);
            return;
        }

        // 2. Fallback to waypoints if random wander is disabled and waypoints are provided
        if (!useRandomWander && waypoints != null && waypoints.Length > 0)
        {
            AdvanceToNextWaypoint();
            if (waypoints[currentWaypointIndex] != null)
            {
                currentPatrolTarget = waypoints[currentWaypointIndex].position;
                if (waterBody != null)
                {
                    float bot = waterBody.FloorY + bottomMargin;
                    float top = GetMaxAllowedY();
                    targetDepthNormalized = (top > bot) ? Mathf.Clamp01((currentPatrolTarget.y - bot) / (top - bot)) : 0.5f;
                }
                return;
            }
        }

        // 3. Fallback forward target if no WaterBody or waypoints
        targetDepthNormalized = Random.Range(minPatrolDepth, maxPatrolDepth);
        currentPatrolTarget = moveTransform.position + moveTransform.forward * 10f;
    }

    /// <summary>
    /// Evaluates current world Y target depth according to current water surface elevation.
    /// As water rises, the depth targets scale smoothly with the flood.
    /// </summary>
    private float GetCurrentPatrolTargetY()
    {
        if (waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();
            float bottomY = b.min.y + bottomMargin;
            float topY = GetMaxAllowedY();

            if (bottomY < topY)
            {
                return Mathf.Lerp(bottomY, topY, targetDepthNormalized);
            }
            return (bottomY + topY) * 0.5f;
        }

        return GetMaxAllowedY();
    }

    /// <summary>
    /// Handles vertical movement separately: smoothly Lerps the shark's Y to target depth
    /// and tracks vertical velocity for natural ascending/diving pitch tilting.
    /// </summary>
    private void UpdateDepth(float targetY)
    {
        Vector3 pos = moveTransform.position;
        float prevY = pos.y;
        float newY = Mathf.Lerp(pos.y, targetY, depthLerpSpeed * Time.deltaTime);
        pos.y = newY;
        moveTransform.position = pos;

        // Calculate vertical velocity for pitch tilting
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        verticalVelocity = (newY - prevY) / dt;
    }

    #endregion

    #region Water Boundaries & Anti-Flight

    /// <summary>
    /// Gets the world Y coordinate of the water surface.
    /// </summary>
    public float GetWaterLevel()
    {
        if (waterBody != null) return waterBody.CurrentWaterY;
        if (WaterBody.Instance != null) return WaterBody.Instance.CurrentWaterY;
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

        if (waterBody != null)
        {
            return waterBody.IsPositionUnderwater(playerTransform.position - Vector3.up * playerWaterTolerance);
        }
        if (WaterBody.Instance != null)
        {
            return WaterBody.Instance.IsPositionUnderwater(playerTransform.position - Vector3.up * playerWaterTolerance);
        }

        float waterCeiling = GetWaterLevel() + playerWaterTolerance;
        return playerTransform.position.y <= waterCeiling;
    }

    /// <summary>
    /// Hard-clamps the shark's position so it can NEVER leave the water volume or clip through the floor.
    /// </summary>
    private void ClampToWaterBoundary()
    {
        if (moveTransform == null) return;

        if (waterBody != null)
        {
            moveTransform.position = waterBody.ClampPositionToWater(
                moveTransform.position,
                boundaryMargin,
                surfaceOffset,
                bottomMargin
            );
            return;
        }

        // Fallback vertical clamp if WaterBody is not active in scene
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
    /// Steers the shark on the XZ plane with Quaternion.RotateTowards,
    /// applies smooth pitch tilting when ascending/descending, banking roll,
    /// and preserves child model visual offset.
    /// </summary>
    private void RotateTowardsTargetXZ(Vector3 directionXZ, float currentTurnRate)
    {
        directionXZ.y = 0.0f;
        if (directionXZ.sqrMagnitude < 0.0001f) return;

        // Base target heading on horizontal plane only
        Quaternion targetRotation = Quaternion.LookRotation(directionXZ.normalized, Vector3.up);

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

        // Pitch tilt when swimming up or down
        float targetPitchAngle = 0.0f;
        if (allowPitchTilt)
        {
            // Negative pitch points nose UP when ascending (verticalVelocity > 0)
            // Positive pitch points nose DOWN when descending (verticalVelocity < 0)
            targetPitchAngle = Mathf.Clamp(-verticalVelocity * pitchStrength, -maxPitchAngle, maxPitchAngle);
        }
        currentPitch = Mathf.SmoothDamp(currentPitch, targetPitchAngle, ref pitchVelocity, pitchSmoothTime);

        // Apply rotation to moving root (Yaw * Pitch * Bank)
        moveTransform.rotation = headingRotation * Quaternion.Euler(currentPitch, 0.0f, currentBank);

        // Preserve model visual offset (e.g. Y = 90) on child object
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
        Debug.Log($"[SharkAI] Random Wander: {useRandomWander} | Depth Target: {targetDepthNormalized:P0} | Current Target: {currentPatrolTarget}", this);

        if (playerTransform != null)
        {
            Debug.Log($"[SharkAI] Player found: '{playerTransform.name}' (In Water: {IsPlayerInWater()})", playerTransform);
        }
        else
        {
            Debug.LogWarning($"[SharkAI] No GameObject with tag '{playerTag}' currently found in scene.", this);
        }

        if (waterBody != null)
        {
            Debug.Log($"[SharkAI] WaterBody active: '{waterBody.name}' (Rising: {waterBody.IsRising}, RiseSpeed: {waterBody.RiseSpeed:F2}, FloorY: {waterBody.FloorY:F2}, MaxHeight: {waterBody.MaxHeight:F2})", waterBody);
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

        // Current patrol target & line to it (in full 3D)
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(currentPatrolTarget, 0.6f);
        Gizmos.DrawLine(t.position, currentPatrolTarget);

        // Player Detection Radius (Yellow)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(t.position, detectionRadius);

        // Lose Sight Radius (Orange)
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Gizmos.DrawWireSphere(t.position, loseSightRadius);

        // Attack Range (Red)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(t.position, attackRange);

        // Water Swim Boundaries (Cyan = Outer Water Volume, Green = Confined Safe Swim Zone)
        if (waterBody != null)
        {
            Bounds wb = waterBody.GetWaterBounds();
            Gizmos.color = new Color(0f, 0.7f, 1f, 0.35f);
            Gizmos.DrawWireCube(wb.center, wb.size);

            // Confined inner boundary for shark
            Vector3 innerCenter = wb.center;
            float topY = GetMaxAllowedY();
            float botY = wb.min.y + bottomMargin;
            innerCenter.y = (topY + botY) * 0.5f;

            Vector3 innerSize = new Vector3(
                Mathf.Max(0.5f, wb.size.x - boundaryMargin * 2f),
                Mathf.Max(0.5f, topY - botY),
                Mathf.Max(0.5f, wb.size.z - boundaryMargin * 2f)
            );
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.5f);
            Gizmos.DrawWireCube(innerCenter, innerSize);
        }
    }
}
