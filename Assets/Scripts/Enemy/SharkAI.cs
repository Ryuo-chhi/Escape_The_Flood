using UnityEngine;
using UnityEngine.Serialization;
using EscapeTheFlood.Water;

/// <summary>
/// Shark AI featuring 3-layer anti-clipping & obstacle avoidance:
/// 1. Safe target volume: Targets picked within water bounds shrunk by X/Z and Y margins.
/// 2. Hard position clamp: Every frame, clamps position to safe bounds (X, Y, Z) following rising water.
/// 3. Look-ahead avoidance: Forward SphereCast (~5m) + Up/Down Raycasts (~2m) against "SharkObstacle" layer,
///    steering away via reflected normal on XZ, biasing depth away from floor/ceiling, and drawing debug rays.
/// Plus XZ steering, independent depth lerping, player detection, chase mode, bite attack, banking,
/// and procedural tail wave coupling.
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

    [Header("Layer 1: Safe Volume Margins")]
    [FormerlySerializedAs("boundaryMargin")]
    [Tooltip("Horizontal safety margin inside the water bounds (X/Z), approximately the shark's body length (~3 units).")]
    [SerializeField] private float marginXZ = 3.0f;

    [FormerlySerializedAs("bottomMargin")]
    [Tooltip("Vertical safety margin above the floor top slab to prevent floor clipping.")]
    [SerializeField] private float floorMargin = 1.8f;

    [FormerlySerializedAs("surfaceOffset")]
    [Tooltip("Vertical safety margin below the rising water surface / ceiling platform.")]
    [SerializeField] private float surfaceMargin = 1.8f;

    [Header("Layer 3: Look-Ahead Obstacle Avoidance")]
    [Tooltip("LayerMask containing obstacles (walls, floor, platform). Ignores water and the shark itself.")]
    [SerializeField] private LayerMask obstacleLayerMask;

    [Tooltip("Forward look-ahead cast distance (~5 units).")]
    [SerializeField] private float forwardAvoidanceDistance = 5.0f;

    [Tooltip("Radius of the forward sphere cast (~ shark half-width). Set to 0 for a thin ray.")]
    [SerializeField] private float forwardCastRadius = 0.8f;

    [Tooltip("Vertical up raycast distance (~2 units) to detect ceiling/platform.")]
    [SerializeField] private float upRayDistance = 2.0f;

    [Tooltip("Vertical down raycast distance (~2 units) to detect floor.")]
    [SerializeField] private float downRayDistance = 2.0f;

    [Tooltip("Speed multiplier when steering away from an obstacle ahead.")]
    [Range(0.1f, 1.0f)]
    [SerializeField] private float obstacleSlowdown = 0.5f;

    [Tooltip("Extra turning rate multiplier when steering away from an obstacle.")]
    [SerializeField] private float obstacleTurnMultiplier = 1.8f;

    [Header("Vertical Depth & Pitch Swimming")]
    [Tooltip("Minimum normalized depth (0 = floor, 1 = surface) for random patrol targets.")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float minPatrolDepth = 0.15f;

    [Tooltip("Maximum normalized depth (0 = floor, 1 = surface) for random patrol targets.")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float maxPatrolDepth = 0.85f;

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

    [Tooltip("Tolerance above water surface where player is still considered 'in water'.")]
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

    // Movement hierarchy state
    private Transform moveTransform;
    private Quaternion initialModelLocalRotation;
    private bool hasModelOffset = false;

    // Navigation & patrol state
    private Vector3 currentPatrolTarget;
    private float targetDepthNormalized = 0.5f;
    private float retargetTimer = 0.0f;
    private float avoidanceRetargetCooldown = 0.0f;
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

        // 6. Ensure a kinematic Rigidbody exists on moveTransform for proper collision/trigger queries without physics gravity
        Rigidbody rootRb = moveTransform.GetComponent<Rigidbody>();
        if (rootRb == null)
        {
            rootRb = moveTransform.gameObject.AddComponent<Rigidbody>();
        }
        rootRb.isKinematic = true;
        rootRb.useGravity = false;

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

        // 8. Initialize obstacle LayerMask (defaults to "SharkObstacle" layer)
        InitObstacleLayerMask();
    }

    private void Start()
    {
        // Double-check water parenting
        if (moveTransform.parent != null && waterBody != null && moveTransform.IsChildOf(waterBody.transform))
        {
            Debug.LogWarning("[SharkAI] Shark was parented under WaterBody! Unparenting to prevent inheriting water scale and position.", this);
            moveTransform.SetParent(null);
        }

        // Compute initial random wander target inside water volume safe bounds
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

        if (avoidanceRetargetCooldown > 0f)
        {
            avoidanceRetargetCooldown -= Time.deltaTime;
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

        // Layer 2: Hard position clamp guarantee every frame (X, Y, Z) following rising water
        HardClampPositionToSafeBounds();

        // Update tail undulation
        UpdateTailAnimation();
    }

    #region Layer 3: Look-Ahead Avoidance & LayerMask

    private void InitObstacleLayerMask()
    {
        if (obstacleLayerMask.value == 0)
        {
            int sharkObstacleLayer = LayerMask.NameToLayer("SharkObstacle");
            if (sharkObstacleLayer != -1)
            {
                obstacleLayerMask = 1 << sharkObstacleLayer;
            }
            else
            {
                // Fallback before layer is created: Default, excluding Water & IgnoreRaycast
                int waterLayer = LayerMask.NameToLayer("Water");
                int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
                int sharkLayer = (moveTransform != null) ? moveTransform.gameObject.layer : 0;

                int excludeMask = 0;
                if (waterLayer != -1) excludeMask |= (1 << waterLayer);
                if (ignoreRaycast != -1) excludeMask |= (1 << ignoreRaycast);
                excludeMask |= (1 << sharkLayer);

                obstacleLayerMask = ~excludeMask;
            }
        }
    }

    /// <summary>
    /// Checks for obstacles directly ahead using SphereCast (radius ~ shark half-width, distance ~5 units).
    /// Steers away toward the hit normal's reflected direction on the XZ plane, slows down slightly,
    /// and draws a debug ray in the Scene view (green = clear, red = hit).
    /// </summary>
    private bool CheckForwardObstacleAvoidance(out Vector3 avoidDirection)
    {
        avoidDirection = Vector3.zero;
        if (moveTransform == null) return false;

        Vector3 origin = moveTransform.position;
        Vector3 forwardDir = moveTransform.forward;
        forwardDir.y = 0.0f;
        if (forwardDir.sqrMagnitude > 0.0001f) forwardDir.Normalize();
        else forwardDir = Vector3.forward;

        bool hit = false;
        RaycastHit hitInfo;

        if (forwardCastRadius > 0.01f)
        {
            hit = Physics.SphereCast(
                origin,
                forwardCastRadius,
                forwardDir,
                out hitInfo,
                forwardAvoidanceDistance,
                obstacleLayerMask,
                QueryTriggerInteraction.Ignore
            );
        }
        else
        {
            hit = Physics.Raycast(
                origin,
                forwardDir,
                out hitInfo,
                forwardAvoidanceDistance,
                obstacleLayerMask,
                QueryTriggerInteraction.Ignore
            );
        }

        // Draw forward ray in Scene view (Green = clear, Red = obstacle hit)
        float rayDist = hit ? hitInfo.distance : forwardAvoidanceDistance;
        Debug.DrawRay(origin, forwardDir * rayDist, hit ? Color.red : Color.green);

        if (hit)
        {
            Vector3 normal = hitInfo.normal;
            normal.y = 0.0f;
            if (normal.sqrMagnitude > 0.0001f)
            {
                normal.Normalize();
            }
            else
            {
                normal = -forwardDir;
            }

            // Steer away by rotating toward hit normal's reflected direction on XZ
            Vector3 reflected = Vector3.Reflect(forwardDir, normal);
            reflected.y = 0.0f;

            if (reflected.sqrMagnitude < 0.001f)
            {
                reflected = Vector3.ProjectOnPlane(normal, Vector3.up);
            }

            // Prevent dead-lock if hitting obstacle perfectly head-on (deflect to either side)
            if (Vector3.Dot(reflected.normalized, forwardDir) < -0.99f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, normal);
                reflected = (side.sqrMagnitude > 0.001f) ? side : Vector3.right;
            }

            avoidDirection = (reflected.sqrMagnitude > 0.001f) ? reflected.normalized : -forwardDir;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks vertical obstacles: Up ray (~2m) detects ceiling/platform and biases target Y downward.
    /// Down ray (~2m) detects floor slab and biases target Y upward.
    /// Draws Cyan/Yellow rays (turning Red when blocked).
    /// </summary>
    private float ApplyVerticalObstacleAvoidance(float currentTargetY)
    {
        if (moveTransform == null) return currentTargetY;

        Vector3 origin = moveTransform.position;
        float targetY = currentTargetY;

        // 1. Up ray: detects ceiling or overhead platform
        bool upHit = Physics.Raycast(
            origin,
            Vector3.up,
            out RaycastHit upHitInfo,
            upRayDistance,
            obstacleLayerMask,
            QueryTriggerInteraction.Ignore
        );
        float upDist = upHit ? upHitInfo.distance : upRayDistance;
        Debug.DrawRay(origin, Vector3.up * upDist, upHit ? Color.red : Color.cyan);

        if (upHit)
        {
            // Bias target Y down away from ceiling/platform
            float maxAllowedFromCeiling = upHitInfo.point.y - surfaceMargin;
            targetY = Mathf.Min(targetY, maxAllowedFromCeiling);
            targetDepthNormalized = Mathf.Max(minPatrolDepth, targetDepthNormalized - 0.4f * Time.deltaTime);
        }

        // 2. Down ray: detects floor slab below
        bool downHit = Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit downHitInfo,
            downRayDistance,
            obstacleLayerMask,
            QueryTriggerInteraction.Ignore
        );
        float downDist = downHit ? downHitInfo.distance : downRayDistance;
        Debug.DrawRay(origin, Vector3.down * downDist, downHit ? Color.red : Color.yellow);

        if (downHit)
        {
            // Bias target Y up away from floor
            float minAllowedFromFloor = downHitInfo.point.y + floorMargin;
            targetY = Mathf.Max(targetY, minAllowedFromFloor);
            targetDepthNormalized = Mathf.Min(maxPatrolDepth, targetDepthNormalized + 0.4f * Time.deltaTime);
        }

        return targetY;
    }

    #endregion

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

        // 5. Look-ahead obstacle avoidance check
        Vector3 steerDirection = toTargetXZ;
        float currentMoveSpeed = speed;
        float currentTurnRate = turnSpeed;

        if (CheckForwardObstacleAvoidance(out Vector3 avoidDir))
        {
            steerDirection = avoidDir;
            currentMoveSpeed = speed * obstacleSlowdown;
            currentTurnRate = turnSpeed * obstacleTurnMultiplier;

            // Pick a new target if hitting obstacle
            if (avoidanceRetargetCooldown <= 0f)
            {
                PickNewPatrolTarget();
                avoidanceRetargetCooldown = 1.0f;
            }
        }

        // 6. Steer on XZ plane with Quaternion.RotateTowards and smooth pitch tilt
        RotateTowardsTargetXZ(steerDirection, currentTurnRate);

        // 7. Move forward along transform.forward
        moveTransform.position += moveTransform.forward * (currentMoveSpeed * Time.deltaTime);

        // 8. Handle depth separately with vertical obstacle avoidance
        float targetY = GetCurrentPatrolTargetY();
        targetY = ApplyVerticalObstacleAvoidance(targetY);
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

        Vector3 steerDirection = toPlayerXZ;
        float currentMoveSpeed = chaseSpeed;
        float currentTurnRate = chaseTurnSpeed;

        // Obstacle avoidance during chase
        if (CheckForwardObstacleAvoidance(out Vector3 avoidDir))
        {
            steerDirection = avoidDir;
            currentMoveSpeed = chaseSpeed * obstacleSlowdown;
            currentTurnRate = chaseTurnSpeed * obstacleTurnMultiplier;
        }

        // Steer toward player on XZ plane
        RotateTowardsTargetXZ(steerDirection, currentTurnRate);

        // Move forward along transform.forward at chase speed
        moveTransform.position += moveTransform.forward * (currentMoveSpeed * Time.deltaTime);

        // Handle depth separately: smoothly Lerp shark Y toward player Y (bounded inside water)
        float targetY = Mathf.Min(playerTransform.position.y, GetMaxAllowedY());
        if (waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();
            targetY = Mathf.Max(targetY, b.min.y + floorMargin);
        }
        targetY = ApplyVerticalObstacleAvoidance(targetY);
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

    #region Layer 1 & Layer 2: Safe Volume Target Generation & Hard Position Clamping

    /// <summary>
    /// Layer 1: Computes random targets strictly inside the water's world-space bounds shrunk by margins.
    /// Recomputes target Y every time a target is picked so it respects the rising water surface.
    /// </summary>
    public void PickNewPatrolTarget()
    {
        retargetTimer = retargetInterval;

        // 1. Random wander within WaterBody volume (no waypoints needed)
        if (useRandomWander && waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();
            float minX = b.min.x + marginXZ;
            float maxX = b.max.x - marginXZ;
            float minZ = b.min.z + marginXZ;
            float maxZ = b.max.z - marginXZ;

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

            // Recompute target Y between (floor top + floorMargin) and (water surface - surfaceMargin)
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
                    float bot = waterBody.FloorY + floorMargin;
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
    /// Evaluates current world Y target depth according to current rising water surface elevation.
    /// Kept between (floor top + floorMargin) and (current water surface - surfaceMargin).
    /// </summary>
    private float GetCurrentPatrolTargetY()
    {
        if (waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();
            float floorTop = b.min.y;
            float minY = floorTop + floorMargin;
            float maxY = waterBody.CurrentWaterY - surfaceMargin;

            if (minY < maxY)
            {
                return Mathf.Lerp(minY, maxY, targetDepthNormalized);
            }
            return (minY + maxY) * 0.5f;
        }

        return GetMaxAllowedY();
    }

    /// <summary>
    /// Layer 2: Hard-clamps the shark's position every frame to the safe bounds (X, Y, Z).
    /// Guarantees the shark cannot leave the pool, penetrate the floor, or poke through the ceiling/platform.
    /// Y strictly follows the rising water: lower limit = floor top + floorMargin, upper limit = current surface - surfaceMargin.
    /// </summary>
    private void HardClampPositionToSafeBounds()
    {
        if (moveTransform == null) return;

        Vector3 pos = moveTransform.position;

        if (waterBody != null)
        {
            Bounds b = waterBody.GetWaterBounds();

            float minX = b.min.x + marginXZ;
            float maxX = b.max.x - marginXZ;
            float minZ = b.min.z + marginXZ;
            float maxZ = b.max.z - marginXZ;

            float floorTop = b.min.y;
            float currentSurface = waterBody.CurrentWaterY;

            float minY = floorTop + floorMargin;
            float maxY = currentSurface - surfaceMargin;

            if (minX <= maxX)
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
            else
                pos.x = (b.min.x + b.max.x) * 0.5f;

            if (minZ <= maxZ)
                pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
            else
                pos.z = (b.min.z + b.max.z) * 0.5f;

            if (minY <= maxY)
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
            else
                pos.y = (minY + maxY) * 0.5f;
        }
        else
        {
            float waterY = (waterLevelTransform != null) ? waterLevelTransform.position.y : waterSurfaceY;
            float maxY = waterY - surfaceMargin;
            if (pos.y > maxY) pos.y = maxY;
        }

        moveTransform.position = pos;
    }

    /// <summary>
    /// Smoothly Lerps the shark's Y to target depth and tracks vertical velocity for natural pitch tilting.
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

    #region Water Boundaries & Anti-Flight Queries

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
        return GetWaterLevel() - surfaceMargin;
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
        Debug.Log($"[SharkAI] Margins -> XZ: {marginXZ:F1}, Floor: {floorMargin:F1}, Surface: {surfaceMargin:F1}", this);
        Debug.Log($"[SharkAI] Obstacle LayerMask Value: {obstacleLayerMask.value}", this);

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

        // Safe Volume Boundary (Green wireframe)
        if (waterBody != null)
        {
            Bounds wb = waterBody.GetWaterBounds();
            Gizmos.color = new Color(0f, 0.7f, 1f, 0.25f);
            Gizmos.DrawWireCube(wb.center, wb.size);

            float minX = wb.min.x + marginXZ;
            float maxX = wb.max.x - marginXZ;
            float minZ = wb.min.z + marginXZ;
            float maxZ = wb.max.z - marginXZ;
            float minY = wb.min.y + floorMargin;
            float maxY = waterBody.CurrentWaterY - surfaceMargin;

            Vector3 safeCenter = new Vector3(
                (minX + maxX) * 0.5f,
                (minY + maxY) * 0.5f,
                (minZ + maxZ) * 0.5f
            );
            Vector3 safeSize = new Vector3(
                Mathf.Max(0.5f, maxX - minX),
                Mathf.Max(0.5f, maxY - minY),
                Mathf.Max(0.5f, maxZ - minZ)
            );

            Gizmos.color = new Color(0.1f, 1f, 0.3f, 0.6f);
            Gizmos.DrawWireCube(safeCenter, safeSize);
        }
    }
}
