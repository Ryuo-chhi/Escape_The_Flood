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
/// The shark stays hidden until the flood is deep enough, keeps between the floor below and the
/// ceiling/slab above (so it cannot swim through building floors), only bites with a clear line of
/// sight, and makes passes at the player (bite, swim past, turn back) instead of orbiting.
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

    [Header("Fish-Like Turning & Movement (A-D)")]
    [FormerlySerializedAs("speed")]
    [FormerlySerializedAs("swimSpeed")]
    [Tooltip("Normal forward cruising speed in units/sec during straight swimming.")]
    [SerializeField] private float cruiseSpeed = 3.0f;

    [Tooltip("Minimum swimming speed in units/sec when executing a sharp turn.")]
    [SerializeField] private float minSpeed = 1.0f;

    [Tooltip("Multiplier on shark body length to define minimum turning radius (typically 1.0 to 1.5). Lower = tighter turns.")]
    [SerializeField] private float minTurnRadiusMultiplier = 1.2f;

    [Tooltip("Smooth time in seconds for steering direction changes (0.2 to 0.4s). Higher = smoother / less twitchy.")]
    [Range(0.05f, 1.0f)]
    [SerializeField] private float directionSmoothTime = 0.25f;

    [Tooltip("Acceleration and deceleration rate in units/s^2 when adapting speed into turns.")]
    [SerializeField] private float speedAcceleration = 3.5f;

    [Tooltip("Draws debug rays in Scene view showing true forward heading (cyan) vs actual movement velocity (yellow).")]
    [SerializeField] private bool drawDebugRays = true;

    [FormerlySerializedAs("turnSpeed")]
    [Tooltip("Minimum patrol turn rate in degrees per second (patrol uses half of this; lets the shark pivot when slow).")]
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
    [SerializeField] private float detectionRadius = 5.0f;

    [Tooltip("Distance at which the shark loses the player and returns to patrol.")]
    [SerializeField] private float loseSightRadius = 8.0f;

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

    [Tooltip("During the lunge, the bite lands if the mouth gets this close to the player's body center.")]
    [SerializeField] private float biteRadius = 1.4f;

    [Tooltip("Shark must be facing the player within this angle (degrees) to start a lunge.")]
    [Range(10f, 90f)]
    [SerializeField] private float attackFacingAngle = 40.0f;

    [Tooltip("Height of the player's body center above their feet (used for distance and line-of-sight checks).")]
    [SerializeField] private float playerCenterHeight = 0.9f;

    [Tooltip("Seconds the shark keeps chasing after losing line of sight before giving up.")]
    [SerializeField] private float lostSightGiveUpTime = 2.0f;

    [Header("Activation (Rising Water)")]
    [Tooltip("Shark stays hidden and inactive until the water is at least this deep.")]
    [SerializeField] private float minActiveWaterDepth = 2.0f;

    [Tooltip("Hide the shark renderers while it is waiting for deep enough water.")]
    [SerializeField] private bool hideWhileDormant = true;

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
    [Tooltip("Maximum roll angle in degrees on local Z proportional to turning rate (typically 10-15 degrees).")]
    [SerializeField] private float bankAmount = 12.0f;

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

    // Fish-like turning & movement state
    private float bodyLength = 1.8f;
    private float minTurnRadius = 2.16f;
    private float currentMoveSpeed = 3.0f;
    private Vector3 smoothedDirection = Vector3.forward;
    private Vector3 previousPosition;
    private Vector3 measuredVelocity;
    private Rigidbody rootRb;

    // Attack & Cooldown timers
    private float attackCooldownTimer = 0.0f;
    private float lungeTimer = 0.0f;
    private bool hasBittenThisLunge = false;
    private float lostSightTimer = 0.0f;
    private float repositionTimer = 0.0f;

    // Activation & body size
    private bool isActive = false;
    private Renderer[] cachedRenderers;
    private float bodyHalfHeight = 0.5f;

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

        // 2b. Re-center: the AI moves/measures from moveTransform, so the visible model must sit on it.
        //     If the model child was placed with an offset in the scene/prefab, move the root onto the
        //     model and zero the child's local position (keeps the shark exactly where it appears).
        if (sharkModel != null && sharkModel != moveTransform && sharkModel.localPosition.sqrMagnitude > 0.0001f)
        {
            Vector3 modelWorldPos = sharkModel.position;
            moveTransform.position = modelWorldPos;
            sharkModel.localPosition = Vector3.zero;
            Debug.Log($"[SharkAI] Model was offset from its AI root; re-centered root on the model at {modelWorldPos}.", this);
        }

        // 2c. Fit the model's trigger collider to the actual mesh size
        if (sharkModel != null)
        {
            BoxCollider modelCollider = sharkModel.GetComponent<BoxCollider>();
            MeshFilter modelMesh = sharkModel.GetComponent<MeshFilter>();
            if (modelCollider != null && modelMesh != null && modelMesh.sharedMesh != null)
            {
                modelCollider.center = modelMesh.sharedMesh.bounds.center;
                modelCollider.size = modelMesh.sharedMesh.bounds.size;
            }
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
        rootRb = moveTransform.GetComponent<Rigidbody>();
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

        // Initialize fish body dimensions and movement state
        CalculateBodyDimensions();
        currentMoveSpeed = cruiseSpeed;
        previousPosition = moveTransform.position;
        Vector3 initForward = moveTransform.forward;
        initForward.y = 0.0f;
        smoothedDirection = (initForward.sqrMagnitude > 0.001f) ? initForward.normalized : Vector3.forward;

        // Compute initial random wander target inside water volume safe bounds
        PickNewPatrolTarget();

        // Find player if not assigned
        FindPlayer();

        // Start dormant; Update activates once the water is deep enough
        cachedRenderers = moveTransform.GetComponentsInChildren<Renderer>(true);
        isActive = false;
        SetSharkVisible(!hideWhileDormant);

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

        // Wait (hidden) until the flood is deep enough to swim in
        if (!isActive)
        {
            if (GetWaterDepth() < minActiveWaterDepth) return;
            ActivateShark();
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

    private void CalculateBodyDimensions()
    {
        float length = 0f;
        if (sharkRenderer != null)
        {
            Vector3 size = sharkRenderer.bounds.size;
            length = Mathf.Max(size.x, size.z);
            bodyHalfHeight = Mathf.Max(0.2f, size.y * 0.5f);
        }

        if (length <= 0.1f)
        {
            BoxCollider box = GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
                length = Mathf.Max(size.x, size.z);
            }
        }

        if (length > 0.1f)
        {
            bodyLength = length;
        }
        else
        {
            bodyLength = 1.8f;
        }

        minTurnRadius = bodyLength * Mathf.Max(minTurnRadiusMultiplier, 0.2f);
        Debug.Log($"[SharkAI] Body Length: {bodyLength:F2}m | Initial Min Turn Radius: {minTurnRadius:F2}m", this);
    }

    /// <summary>
    /// Checks for obstacles directly ahead using SphereCast (radius ~ shark half-width, distance ~5 units).
    /// Steers away toward the hit normal's reflected direction on the XZ plane,
    /// and draws a debug ray in the Scene view (green = clear, red = hit).
    /// </summary>
    private bool CheckForwardObstacleAvoidance(out Vector3 avoidDirection, out float hitDistance)
    {
        avoidDirection = Vector3.zero;
        hitDistance = forwardAvoidanceDistance;
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
        hitDistance = rayDist;
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

    private bool CheckForwardObstacleAvoidance(out Vector3 avoidDirection)
    {
        return CheckForwardObstacleAvoidance(out avoidDirection, out _);
    }

    /// <summary>
    /// Combines the goal target direction with weighted obstacle avoidance.
    /// Closeness scales avoidance weight (gentle nudge far away, strong turn only when close).
    /// </summary>
    private Vector3 ComputeDesiredDirection(Vector3 targetDirectionXZ, out float obstacleCloseness)
    {
        targetDirectionXZ.y = 0.0f;
        Vector3 baseDir = (targetDirectionXZ.sqrMagnitude > 0.0001f) ? targetDirectionXZ.normalized : moveTransform.forward;
        baseDir.y = 0.0f;
        baseDir.Normalize();

        obstacleCloseness = 0.0f;

        if (CheckForwardObstacleAvoidance(out Vector3 avoidDir, out float hitDistance))
        {
            // 0 at max distance (~5m), 1 when touching
            float normalizedClose = 1.0f - Mathf.Clamp01(hitDistance / Mathf.Max(forwardAvoidanceDistance, 0.1f));
            obstacleCloseness = normalizedClose;

            // Quadratic weighting: small nudge far away, strong turn only when close
            float avoidWeight = normalizedClose * normalizedClose;

            Vector3 blended = Vector3.Lerp(baseDir, avoidDir, avoidWeight);
            if (blended.sqrMagnitude > 0.001f)
            {
                return blended.normalized;
            }
            return avoidDir;
        }

        return baseDir;
    }

    /// <summary>
    /// Smooths the desired direction via Slerp so the shark doesn't snap or swing wildly.
    /// </summary>
    private void UpdateSmoothedDirection(Vector3 rawDesiredDirection)
    {
        rawDesiredDirection.y = 0.0f;
        if (rawDesiredDirection.sqrMagnitude < 0.0001f) return;
        rawDesiredDirection.Normalize();

        if (smoothedDirection.sqrMagnitude < 0.001f)
        {
            smoothedDirection = rawDesiredDirection;
        }
        else
        {
            float slerpFactor = 1.0f - Mathf.Exp(-Time.deltaTime / Mathf.Max(directionSmoothTime, 0.05f));
            smoothedDirection = Vector3.Slerp(smoothedDirection, rawDesiredDirection, slerpFactor).normalized;
        }
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
        // 1. Detect a submerged player within range (3D distance) that the shark can actually see
        if (playerTransform != null && IsPlayerInWater())
        {
            Vector3 playerCenter = GetPlayerCenter();
            if (Vector3.Distance(moveTransform.position, playerCenter) <= detectionRadius && HasLineOfSightToPlayer())
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

        // 4. Arrive check (distance < arrivalDistance in XZ) OR retarget timer (~6s)
        if (distanceXZ <= arrivalDistance || retargetTimer <= 0f)
        {
            PickNewPatrolTarget();
            toTargetXZ = currentPatrolTarget - moveTransform.position;
            toTargetXZ.y = 0.0f;
        }

        // 5. Look-ahead obstacle avoidance with gentle distance-based weighting
        Vector3 rawDesired = ComputeDesiredDirection(toTargetXZ, out float obstacleCloseness);

        // Pick a new target only if obstacle is right in front (prevents thrashing)
        if (obstacleCloseness > 0.75f && avoidanceRetargetCooldown <= 0f)
        {
            PickNewPatrolTarget();
            avoidanceRetargetCooldown = 1.0f;
        }

        // 6. Smooth the desired direction (no snapping)
        UpdateSmoothedDirection(rawDesired);

        // 7. Fish-like turning & movement along facing direction
        ExecuteFishMovement(cruiseSpeed, minSpeed, turnSpeed * 0.5f);

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

        Vector3 playerCenter = GetPlayerCenter();
        Vector3 toPlayerXZ = playerCenter - moveTransform.position;
        toPlayerXZ.y = 0.0f;
        float distToPlayer = Vector3.Distance(moveTransform.position, playerCenter);

        if (distToPlayer > loseSightRadius)
        {
            Debug.Log("[SharkAI] Lost sight of player. Returning to patrol.", this);
            SwitchState(SharkState.Patrol);
            return;
        }

        // Give up if a wall/floor blocks the view for too long
        bool canSee = HasLineOfSightToPlayer();
        lostSightTimer = canSee ? 0f : lostSightTimer + Time.deltaTime;
        if (lostSightTimer > lostSightGiveUpTime)
        {
            Debug.Log("[SharkAI] Player hidden behind obstacles. Returning to patrol.", this);
            SwitchState(SharkState.Patrol);
            return;
        }

        // Lunge when the mouth is close, the shark faces the player, nothing blocks, and cooldown is over
        if (canSee && attackCooldownTimer <= 0f && GetMouthDistanceToPlayer() <= attackRange && IsFacingPlayer())
        {
            SwitchState(SharkState.Attack);
            return;
        }

        // Too close and side-on (player is alongside the body): swim on past, then turn and charge again.
        // This makes "passes" like a real shark instead of orbiting the player forever.
        if (repositionTimer <= 0f && !IsFacingPlayer() && toPlayerXZ.magnitude < bodyLength * 0.75f)
        {
            repositionTimer = 1.0f;
        }

        Vector3 rawDesired;
        if (repositionTimer > 0f)
        {
            repositionTimer -= Time.deltaTime;
            Vector3 forwardXZ = moveTransform.forward;
            forwardXZ.y = 0f;
            rawDesired = ComputeDesiredDirection(forwardXZ, out _);
            UpdateSmoothedDirection(rawDesired);
            ExecuteFishMovement(chaseSpeed, chaseSpeed * 0.6f, chaseTurnSpeed);
        }
        else
        {
            // Compute desired direction towards player with obstacle avoidance
            rawDesired = ComputeDesiredDirection(toPlayerXZ, out _);
            UpdateSmoothedDirection(rawDesired);

            // Low minimum speed lets it pivot to face the player when turning hard
            ExecuteFishMovement(chaseSpeed, minSpeed, chaseTurnSpeed);
        }

        // Depth: follow the player's body center, kept inside the local water band (floor/ceiling/surface)
        GetSafeVerticalBand(out float bandMinY, out float bandMaxY);
        float targetY = Mathf.Clamp(playerCenter.y, bandMinY, Mathf.Max(bandMinY, bandMaxY));
        UpdateDepth(targetY);
    }

    private void HandleAttackState()
    {
        lungeTimer -= Time.deltaTime;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // Small homing during the lunge so a slightly moving player can still be caught
        if (playerTransform != null)
        {
            Vector3 toPlayerXZ = GetPlayerCenter() - moveTransform.position;
            toPlayerXZ.y = 0f;
            if (toPlayerXZ.sqrMagnitude > 0.01f)
            {
                headingRotation = Quaternion.RotateTowards(headingRotation, Quaternion.LookRotation(toPlayerXZ.normalized, Vector3.up), 120f * dt);
                moveTransform.rotation = headingRotation;
            }

            GetSafeVerticalBand(out float bandMinY, out float bandMaxY);
            UpdateDepth(Mathf.Clamp(GetPlayerCenter().y, bandMinY, Mathf.Max(bandMinY, bandMaxY)));
        }

        // Forward burst lunge strictly along transform.forward
        moveTransform.position += moveTransform.forward * (lungeSpeed * dt);

        // Bite lands once per lunge, only on real contact with nothing in between
        if (!hasBittenThisLunge && playerTransform != null && IsPlayerInWater()
            && GetMouthDistanceToPlayer() <= biteRadius && HasLineOfSightToPlayer())
        {
            hasBittenThisLunge = true;
            Debug.Log($"<color=red><b>[SharkAI] Bite hit! -{attackDamage} HP</b></color>", this);
            playerTransform.SendMessage("TakeDamage", (float)attackDamage, SendMessageOptions.DontRequireReceiver);
        }

        if (rootRb != null && !rootRb.isKinematic)
        {
#if UNITY_6000_0_OR_NEWER
            rootRb.linearVelocity = moveTransform.forward * lungeSpeed;
#else
            rootRb.velocity = moveTransform.forward * lungeSpeed;
#endif
        }

        Vector3 currentPos = moveTransform.position;
        measuredVelocity = (currentPos - previousPosition) / dt;
        previousPosition = currentPos;

        if (drawDebugRays)
        {
            Debug.DrawRay(moveTransform.position, moveTransform.forward * 3.0f, Color.cyan);
            Debug.DrawRay(moveTransform.position + Vector3.up * 0.05f, measuredVelocity, Color.yellow);
        }

        // When lunge completes, swim on past the player briefly, then come back around for another pass
        if (lungeTimer <= 0f)
        {
            SwitchState(SharkState.Chase);
            repositionTimer = 0.8f;
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
                lostSightTimer = 0f;
                Debug.Log("<color=yellow><b>[SharkAI] Player spotted in water! Chasing player.</b></color>", this);
                break;

            case SharkState.Attack:
                Debug.Log("<color=red><b>[SharkAI] Bite Attack Lunge!</b></color>", this);
                lungeTimer = lungeDuration;
                attackCooldownTimer = attackCooldown;
                hasBittenThisLunge = false; // damage is applied in HandleAttackState on contact
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
            // Uses the local band so targets stay between this room's floor and ceiling
            GetSafeVerticalBand(out float minY, out float maxY);

            if (minY < maxY)
            {
                return Mathf.Lerp(minY, maxY, targetDepthNormalized);
            }
            return (minY + maxY) * 0.5f;
        }

        return GetMaxAllowedY();
    }

    /// <summary>
    /// Vertical range the shark's center may occupy at its current XZ position:
    /// above the nearest floor below (furniture/floor slab on the obstacle layer, or the water floor)
    /// and below both the water surface and the nearest ceiling/slab above.
    /// This keeps the shark from swimming through building floors as the water rises.
    /// </summary>
    private void GetSafeVerticalBand(out float minY, out float maxY)
    {
        Vector3 pos = moveTransform.position;
        float surface = GetWaterLevel();

        minY = (waterBody != null ? waterBody.FloorY : surface - 50f) + floorMargin;
        maxY = surface - surfaceMargin;

        const float probeDistance = 60f;

        if (Physics.Raycast(pos, Vector3.down, out RaycastHit floorHit, probeDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore))
        {
            minY = Mathf.Max(minY, floorHit.point.y + Mathf.Max(floorMargin, bodyHalfHeight));
        }

        if (Physics.Raycast(pos, Vector3.up, out RaycastHit ceilingHit, probeDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore))
        {
            maxY = Mathf.Min(maxY, ceilingHit.point.y - (bodyHalfHeight + 0.2f));
        }
    }

    /// <summary>
    /// Current water depth (surface height above the water floor).
    /// </summary>
    private float GetWaterDepth()
    {
        if (waterBody == null) waterBody = WaterBody.Instance;
        return waterBody != null ? waterBody.CurrentHeight : 0f;
    }

    private void ActivateShark()
    {
        isActive = true;
        SetSharkVisible(true);

        // Start at a safe depth right where it was placed, then begin patrolling
        GetSafeVerticalBand(out float minY, out float maxY);
        Vector3 pos = moveTransform.position;
        pos.y = (minY <= maxY) ? Mathf.Lerp(minY, maxY, 0.4f) : (minY + maxY) * 0.5f;
        moveTransform.position = pos;
        previousPosition = pos;

        SwitchState(SharkState.Patrol);
        PickNewPatrolTarget();
        Debug.Log($"[SharkAI] Water is {GetWaterDepth():F1}m deep. Shark is now active.", this);
    }

    private void SetSharkVisible(bool visible)
    {
        if (cachedRenderers == null) return;
        foreach (Renderer r in cachedRenderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    private Vector3 GetPlayerCenter()
    {
        // Scales with the player (the player prefab is scaled 2x)
        return playerTransform.position + Vector3.up * (playerCenterHeight * Mathf.Max(0.1f, playerTransform.lossyScale.y));
    }

    private Vector3 GetMouthPosition()
    {
        return moveTransform.position + moveTransform.forward * (bodyLength * 0.45f);
    }

    private float GetMouthDistanceToPlayer()
    {
        return Vector3.Distance(GetMouthPosition(), GetPlayerCenter());
    }

    private bool IsFacingPlayer()
    {
        Vector3 toPlayer = GetPlayerCenter() - moveTransform.position;
        toPlayer.y = 0f;
        Vector3 forward = moveTransform.forward;
        forward.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) return true;
        return Vector3.Angle(forward, toPlayer) <= attackFacingAngle;
    }

    /// <summary>
    /// True when no wall/floor/furniture on the obstacle layer is between the shark and the player.
    /// </summary>
    private bool HasLineOfSightToPlayer()
    {
        if (playerTransform == null) return false;
        return !Physics.Linecast(moveTransform.position, GetPlayerCenter(), obstacleLayerMask, QueryTriggerInteraction.Ignore);
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

            // Y: between local floor and min(local ceiling, water surface)
            GetSafeVerticalBand(out float minY, out float maxY);

            if (minX <= maxX)
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
            else
                pos.x = (b.min.x + b.max.x) * 0.5f;

            if (minZ <= maxZ)
                pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
            else
                pos.z = (b.min.z + b.max.z) * 0.5f;

            // Rise smoothly over furniture/floors instead of snapping, but never above the ceiling/surface
            if (pos.y < minY)
                pos.y = Mathf.MoveTowards(pos.y, minY, 3.0f * Time.deltaTime);
            if (pos.y > maxY)
                pos.y = maxY;
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
    /// Executes fish-like steering and movement:
    /// A. Always moves strictly along the forward facing direction (zero sideways slip).
    /// B. Limits turning radius using body length: maxTurnRate (deg/s) = (speed / minTurnRadius) * Rad2Deg.
    /// C. Slows down into sharp turns (interpolating between minSpeed and cruiseSpeed based on turn angle).
    /// D. Steers toward smoothedDirection (no snapping).
    /// E. Banks visual mesh child into the turn (up to bankAmount degrees).
    /// </summary>
    private void ExecuteFishMovement(float targetCruiseSpeed, float targetMinSpeed, float minTurnRateDeg = 0f)
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // Dynamically update minTurnRadius to allow live Inspector tweaking
        minTurnRadius = bodyLength * Mathf.Max(minTurnRadiusMultiplier, 0.2f);

        // 1. Angle between forward and desired direction on XZ plane
        Vector3 forwardXZ = moveTransform.forward;
        forwardXZ.y = 0.0f;
        if (forwardXZ.sqrMagnitude > 0.0001f) forwardXZ.Normalize();
        else forwardXZ = Vector3.forward;

        float turnAngle = Vector3.Angle(forwardXZ, smoothedDirection);

        // 2. Slow down when turn is sharp: (1 - angle / 90 clamped to 0..1)
        float straightFactor = Mathf.Clamp01(1.0f - (turnAngle / 90.0f));
        float desiredSpeed = Mathf.Lerp(targetMinSpeed, targetCruiseSpeed, straightFactor);

        // Smooth speed changes gradually
        currentMoveSpeed = Mathf.MoveTowards(currentMoveSpeed, desiredSpeed, speedAcceleration * dt);

        // 3. Dynamic max turn rate: maxTurnRate (deg/s) = (currentSpeed / minTurnRadius) * Rad2Deg
        float effectiveRadius = Mathf.Max(minTurnRadius, 0.3f);
        float maxTurnRateDeg = (currentMoveSpeed / effectiveRadius) * Mathf.Rad2Deg;
        // Sharks can pivot tightly when slow: never turn slower than the minimum rate (avoids endless circling)
        maxTurnRateDeg = Mathf.Max(maxTurnRateDeg, minTurnRateDeg);

        // 4. Rotate first towards smoothed direction using max turn rate
        Quaternion targetRotation = Quaternion.LookRotation(smoothedDirection, Vector3.up);
        headingRotation = Quaternion.RotateTowards(headingRotation, targetRotation, maxTurnRateDeg * dt);

        // Compute yaw delta for visual banking
        float currentYaw = headingRotation.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(previousHeadingYaw, currentYaw);
        previousHeadingYaw = currentYaw;

        float angularVelocity = yawDelta / dt;
        float normalizedTurn = Mathf.Clamp(angularVelocity / Mathf.Max(maxTurnRateDeg, 1.0f), -1.0f, 1.0f);
        currentTurn = Mathf.SmoothDamp(currentTurn, normalizedTurn, ref turnVelocity, 0.15f);

        // Roll into the turn on the visual mesh
        float targetBankAngle = Mathf.Clamp(-currentTurn * bankAmount, -35.0f, 35.0f);
        currentBank = Mathf.SmoothDamp(currentBank, targetBankAngle, ref bankVelocity, bankSmoothTime);

        // Pitch tilt when ascending or descending
        float targetPitchAngle = 0.0f;
        if (allowPitchTilt)
        {
            targetPitchAngle = Mathf.Clamp(-verticalVelocity * pitchStrength, -maxPitchAngle, maxPitchAngle);
        }
        currentPitch = Mathf.SmoothDamp(currentPitch, targetPitchAngle, ref pitchVelocity, pitchSmoothTime);

        // 5. Apply rotations: Root stays on XZ steering plane (pure yaw)
        moveTransform.rotation = headingRotation;

        // Visual child mesh gets pitch, bank, and original model rotation offset
        if (sharkModel != null && sharkModel != moveTransform)
        {
            Quaternion visualTilt = Quaternion.Euler(currentPitch, 0.0f, currentBank);
            sharkModel.localRotation = visualTilt * initialModelLocalRotation;
        }
        else
        {
            moveTransform.rotation = headingRotation * Quaternion.Euler(currentPitch, 0.0f, currentBank);
        }

        // 6. Move forward strictly along facing direction (zero sideways slip)
        Vector3 moveStep = moveTransform.forward * (currentMoveSpeed * dt);
        moveTransform.position += moveStep;

        if (rootRb != null && !rootRb.isKinematic)
        {
#if UNITY_6000_0_OR_NEWER
            rootRb.linearVelocity = moveTransform.forward * currentMoveSpeed;
#else
            rootRb.velocity = moveTransform.forward * currentMoveSpeed;
#endif
        }

        // 7. Measure actual velocity and draw debug rays
        Vector3 currentPos = moveTransform.position;
        measuredVelocity = (currentPos - previousPosition) / dt;
        previousPosition = currentPos;

        if (drawDebugRays)
        {
            // Cyan ray = forward heading, Yellow ray = actual velocity (overlap = zero slide)
            Debug.DrawRay(moveTransform.position, moveTransform.forward * 3.0f, Color.cyan);
            Debug.DrawRay(moveTransform.position + Vector3.up * 0.05f, measuredVelocity, Color.yellow);
        }
    }

    #endregion

    #region Tail Swimming Coupling

    private void UpdateTailAnimation()
    {
        // Increase tail wave speed & amplitude slightly when turning for a lively fish kick
        float turnTailBoost = 1.0f + Mathf.Abs(currentTurn) * 0.35f;

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
                        tailWave.speed = idleWaveSpeed * turnTailBoost;
                        tailWave.amplitude = idleAmplitude * turnTailBoost;
                        break;

                    case SharkState.Chase:
                        tailWave.swim = 1.2f;
                        tailWave.speed = maxWaveSpeed * turnTailBoost;
                        tailWave.amplitude = maxAmplitude * turnTailBoost;
                        break;

                    case SharkState.Attack:
                        tailWave.swim = 1.4f;
                        tailWave.speed = maxWaveSpeed * 1.25f;
                        tailWave.amplitude = maxAmplitude * 1.2f;
                        break;
                }
            }
        }

        // 2. Shader Graph Fallback
        if (sharkRenderer != null)
        {
            float activeWaveFreq = (currentState == SharkState.Patrol) ? (idleWaveSpeed * turnTailBoost) : (maxWaveSpeed * turnTailBoost);

            accumulatedPhase += activeWaveFreq * Time.deltaTime;
            if (accumulatedPhase > 628.3185f)
            {
                accumulatedPhase -= 628.3185f;
            }

            sharkRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(PhasePropId, accumulatedPhase);
            propertyBlock.SetFloat(SwimSpeedPropId, (currentState == SharkState.Patrol) ? 0.5f : 1.0f);
            propertyBlock.SetFloat(AmplitudePropId, useCpuTailWave ? 0f : (currentState == SharkState.Patrol ? idleAmplitude : maxAmplitude) * turnTailBoost);
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
        // Prefer the actual player controller (tags can be set on other objects by mistake)
        EscapeTheFlood.Player.PlayerMovement playerMovement = EscapeTheFlood.Player.PlayerMovement.Instance;
        if (playerMovement == null) playerMovement = FindFirstObjectByType<EscapeTheFlood.Player.PlayerMovement>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
            return;
        }

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

        // Smoothed Desired Direction & Min Turn Radius (Cyan)
        if (drawDebugRays)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(t.position, smoothedDirection * 3.5f);

            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(t.position, minTurnRadius);
        }

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
        Gizmos.DrawWireSphere(t.position + t.forward * (bodyLength * 0.45f), attackRange);

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
