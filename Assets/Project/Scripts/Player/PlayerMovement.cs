using System;
using UnityEngine;
using EscapeTheFlood.Water;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeTheFlood.Player
{
    /// <summary>
    /// PlayerMovement: Third-person movement, swimming physics, procedural swimming animation, health & breath.
    /// Supports Unity's New Input System (WASD / Left Stick / Space jump & swim).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerMovement : MonoBehaviour, IWaterSubmergible
    {
        public static PlayerMovement Instance { get; private set; }

        [Header("Ground Movement Settings")]
        [Tooltip("Horizontal walking/running speed on dry ground.")]
        [SerializeField] private float moveSpeed = 6.5f;

        [Tooltip("How quickly the character turns to face movement direction (degrees per second).")]
        [SerializeField] private float rotationSpeed = 720f;

        [Tooltip("Jump apex height in units.")]
        [SerializeField] private float jumpHeight = 2.8f;

        [Tooltip("Downward gravitational acceleration on land.")]
        [SerializeField] private float gravity = -18f;

        [Header("Swimming Settings")]
        [Tooltip("Horizontal movement speed while swimming.")]
        [SerializeField] private float swimSpeed = 5.0f;

        [Tooltip("Upward swim speed when pressing Space.")]
        [SerializeField] private float swimAscendSpeed = 4.5f;

        [Tooltip("Downward dive speed when pressing Left Ctrl or C.")]
        [SerializeField] private float swimDescendSpeed = 3.5f;

        [Tooltip("Water depth above player feet to start swimming (base unit: 0.3m). Auto-scales with player scale.")]
        [SerializeField] private float swimTriggerDepth = 0.3f;

        [Tooltip("Height of player's head above feet for oxygen depletion (base unit: 1.4m). Auto-scales with player scale.")]
        [SerializeField] private float headHeightOffset = 1.4f;

        [Tooltip("Forward tilt angle in degrees when swimming forward (e.g. 65° = horizontal swimming pose).")]
        [SerializeField] private float swimForwardTilt = 65f;

        [Header("Health & Breath (Vitals)")]
        [Tooltip("Maximum player health points.")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        [Tooltip("Maximum breath percentage.")]
        [SerializeField] private float maxBreath = 100f;
        [SerializeField] private float currentBreath = 100f;

        [Tooltip("Breath depletion rate per second while head is submerged (100 -> 0 in 10s).")]
        [SerializeField] private float breathDepletionRate = 10f;

        [Tooltip("Breath recovery rate per second when head is above water surface.")]
        [SerializeField] private float breathRecoveryRate = 30f;

        [Tooltip("Damage taken per second when breath reaches 0.")]
        [SerializeField] private float drowningDamagePerSec = 15f;

        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;

        // Events for HUD
        public event Action<float, float> OnHealthChanged;
        public event Action<float, float> OnBreathChanged;
        public event Action OnPlayerDrowned;
        public event Action OnPlayerDied;

        // Getters
        public float Health => currentHealth;
        public float MaxHealth => maxHealth;
        public float Breath => currentBreath;
        public float MaxBreath => maxBreath;
        public bool IsUnderwater => isUnderwater;
        public bool IsHeadSubmerged => isHeadSubmerged;
        public bool IsDead => isDead;

        private CharacterController characterController;
        private Animator animator;
        private Vector3 verticalVelocity;
        private bool isUnderwater = false;
        private bool isHeadSubmerged = false;
        private bool isDead = false;
        private float currentWaterSurfaceY = 0f;

        // Procedural Swim Animation States
        private float currentYaw = 0f;
        private float currentPitch = 0f;
        private float currentRoll = 0f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int IsSwimmingHash = Animator.StringToHash("IsSwimming");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            characterController = GetComponent<CharacterController>();
            animator = GetComponentInChildren<Animator>();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            currentYaw = transform.eulerAngles.y;
            currentHealth = maxHealth;
            currentBreath = maxBreath;
        }

        private void Start()
        {
            // Auto-attach PlayerHUD if none exists in scene so HUD shows at top-left automatically!
            if (FindFirstObjectByType<EscapeTheFlood.UI.PlayerHUD>() == null)
            {
                gameObject.AddComponent<EscapeTheFlood.UI.PlayerHUD>();
            }

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnBreathChanged?.Invoke(currentBreath, maxBreath);
        }

        private void Update()
        {
            if (isDead) return;

            // Ignore input while paused / on end screens (otherwise a jump pressed during pause fires on resume)
            if (Time.timeScale <= 0f) return;

            // 1. Water Line Check (Checks against water level)
            CheckWaterLevel();

            // 2. Read Inputs
            Vector2 moveInput = ReadInputVector();
            bool jumpOrAscend = ReadJumpInput();
            bool diveDown = ReadDiveInput();

            // 3. Update Physics & Procedural Swim Animation
            if (isUnderwater)
            {
                UpdateUnderwaterMovement(moveInput, jumpOrAscend, diveDown);
            }
            else
            {
                UpdateGroundMovement(moveInput, jumpOrAscend);
            }

            // 4. Update Breath & Oxygen
            UpdateOxygen();

            // 5. Update Animator parameters if an animator exists
            UpdateAnimator(moveInput);
        }

        #region Water Level & Oxygen

        private void CheckWaterLevel()
        {
            if (WaterBody.Instance != null)
            {
                currentWaterSurfaceY = WaterBody.Instance.CurrentWaterY;
                Vector3 playerPos = transform.position;
                Bounds waterBounds = WaterBody.Instance.GetWaterBounds();

                // 1. Check if player is horizontally inside the water boundary (X and Z)
                bool insideHorizontalBounds = playerPos.x >= waterBounds.min.x && playerPos.x <= waterBounds.max.x &&
                                              playerPos.z >= waterBounds.min.z && playerPos.z <= waterBounds.max.z;

                if (!insideHorizontalBounds)
                {
                    // Outside the water area (e.g. outside the building / in dry air): cannot swim!
                    SetUnderwater(false);
                    isHeadSubmerged = false;
                    return;
                }

                // 2. Player is within water perimeter: check vertical submersion
                float scaleY = Mathf.Max(0.1f, transform.lossyScale.y);
                float feetY = playerPos.y;
                float headY = feetY + (headHeightOffset * scaleY);

                // Swimming mode only active when inside the water column
                bool inWater = (feetY + (swimTriggerDepth * scaleY)) < currentWaterSurfaceY && feetY >= (waterBounds.min.y - 1f);
                SetUnderwater(inWater);

                // Breath depletes only when head is actually below the surface
                isHeadSubmerged = inWater && (headY < currentWaterSurfaceY);
            }
            else
            {
                SetUnderwater(false);
                isHeadSubmerged = false;
            }
        }

        public void SetUnderwater(bool submerged)
        {
            if (isUnderwater == submerged) return;

            isUnderwater = submerged;

            if (isUnderwater)
            {
                // Reset vertical fall momentum when diving into water
                verticalVelocity.y = Mathf.Clamp(verticalVelocity.y, -2f, 1f);
            }

            if (animator != null)
            {
                animator.SetBool(IsSwimmingHash, isUnderwater);
            }
        }

        private void UpdateOxygen()
        {
            if (isHeadSubmerged)
            {
                // Head is underwater: lose breath
                if (currentBreath > 0f)
                {
                    currentBreath -= breathDepletionRate * Time.deltaTime;
                    if (currentBreath <= 0f)
                    {
                        currentBreath = 0f;
                        OnPlayerDrowned?.Invoke();
                    }
                    OnBreathChanged?.Invoke(currentBreath, maxBreath);
                }
                else
                {
                    // Asphyxiation / drowning damage
                    TakeDamage(drowningDamagePerSec * Time.deltaTime);
                }
            }
            else
            {
                // Head is above water: recover breath back to 100%
                if (currentBreath < maxBreath)
                {
                    currentBreath += breathRecoveryRate * Time.deltaTime;
                    if (currentBreath > maxBreath) currentBreath = maxBreath;
                    OnBreathChanged?.Invoke(currentBreath, maxBreath);
                }
            }
        }

        public void TakeDamage(float amount)
        {
            if (isDead || amount <= 0f) return;

            currentHealth -= amount;
            if (currentHealth <= 0f)
            {
                currentHealth = 0f;
                Die();
            }

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void Die()
        {
            if (isDead) return;
            isDead = true;
            Debug.Log("[PlayerMovement] Player died!");
            OnPlayerDied?.Invoke();
        }

        #endregion

        #region Movement & Procedural Swim Animation

        private void UpdateGroundMovement(Vector2 input, bool jumpPressed)
        {
            Vector3 targetDirection = CalculateCameraRelativeDirection(input);

            // Smooth rotation towards heading only when input is active
            if (targetDirection.sqrMagnitude > 0.005f)
            {
                float targetYaw = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;
                currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, rotationSpeed * Time.deltaTime);
            }

            // Upright orientation on ground
            currentPitch = Mathf.MoveTowards(currentPitch, 0f, 360f * Time.deltaTime);
            currentRoll = Mathf.MoveTowards(currentRoll, 0f, 360f * Time.deltaTime);
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, currentRoll);

            // Gravity & Jump handling
            if (characterController.isGrounded)
            {
                // Firm downward grounded stick force to prevent micro-bouncing on floor seams
                if (verticalVelocity.y < 0f)
                {
                    verticalVelocity.y = -5f;
                }

                if (jumpPressed)
                {
                    verticalVelocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
                }
            }
            else
            {
                // Continuous in-air gravity
                verticalVelocity.y += gravity * Time.deltaTime;
            }

            // Combined single Move call to eliminate collision sweep jitter
            Vector3 finalMove = (targetDirection * moveSpeed) + verticalVelocity;
            characterController.Move(finalMove * Time.deltaTime);
        }

        private void UpdateUnderwaterMovement(Vector2 input, bool ascend, bool descend)
        {
            Vector3 targetDirection = CalculateCameraRelativeDirection(input);
            bool isMoving = targetDirection.sqrMagnitude > 0.005f;

            // 1. Horizontal Heading
            if (isMoving)
            {
                float targetYaw = Mathf.Atan2(targetDirection.x, targetDirection.z) * Mathf.Rad2Deg;
                currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, rotationSpeed * Time.deltaTime);
            }

            // 2. PROCEDURAL SWIM ANIMATION (Smooth body tilt forward)
            float targetPitch;
            float targetRoll;

            if (isMoving)
            {
                // Active swimming stroke
                float strokeWave = Mathf.Sin(Time.time * 6f) * 6f;
                float rollWave = Mathf.Cos(Time.time * 6f) * 4f;

                targetPitch = swimForwardTilt + strokeWave;
                targetRoll = rollWave;
            }
            else
            {
                // Treading water
                float treadWave = Mathf.Sin(Time.time * 3f) * 4f;
                targetPitch = 15f + treadWave;
                targetRoll = Mathf.Cos(Time.time * 2f) * 3f;
            }

            currentPitch = Mathf.MoveTowards(currentPitch, targetPitch, 180f * Time.deltaTime);
            currentRoll = Mathf.MoveTowards(currentRoll, targetRoll, 90f * Time.deltaTime);
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, currentRoll);

            // 3. Vertical 3D swimming speed target
            float targetVertSpeed = -0.3f; // subtle natural sinking/buoyancy
            if (ascend)
            {
                targetVertSpeed = swimAscendSpeed;
            }
            else if (descend)
            {
                targetVertSpeed = -swimDescendSpeed;
            }

            verticalVelocity.y = Mathf.MoveTowards(verticalVelocity.y, targetVertSpeed, 12f * Time.deltaTime);

            // Surface clamp: jump out if ascending near surface
            float currentFeetY = transform.position.y;
            float scaledHeadOffset = headHeightOffset * Mathf.Max(0.1f, transform.lossyScale.y);
            if (currentFeetY + scaledHeadOffset > currentWaterSurfaceY && ascend)
            {
                verticalVelocity.y = Mathf.Sqrt(2f * jumpHeight * 0.8f * -gravity);
            }

            // Combined single Move call in water
            Vector3 finalMove = (targetDirection * swimSpeed) + verticalVelocity;
            characterController.Move(finalMove * Time.deltaTime);
        }

        private Vector3 CalculateCameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.001f) return Vector3.zero;

            Vector3 moveDirection;
            if (cameraTransform != null)
            {
                Vector3 forward = cameraTransform.forward;
                Vector3 right = cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();

                moveDirection = (forward * input.y) + (right * input.x);
            }
            else
            {
                moveDirection = new Vector3(input.x, 0f, input.y);
            }

            return moveDirection.normalized;
        }

        #endregion

        #region Inputs & Animator

        private Vector2 ReadInputVector()
        {
            Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
            }

            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                if (stick.sqrMagnitude > input.sqrMagnitude) input = stick;
            }
#else
            input.x = Input.GetAxisRaw("Horizontal");
            input.y = Input.GetAxisRaw("Vertical");
#endif

            return input.magnitude > 1f ? input.normalized : input;
        }

        private bool ReadJumpInput()
        {
#if ENABLE_INPUT_SYSTEM
            bool keyboardJump = Keyboard.current != null && (Keyboard.current.spaceKey.isPressed || Keyboard.current.spaceKey.wasPressedThisFrame);
            bool gamepadJump = Gamepad.current != null && (Gamepad.current.buttonSouth.isPressed || Gamepad.current.buttonSouth.wasPressedThisFrame);
            return keyboardJump || gamepadJump;
#else
            return Input.GetButton("Jump");
#endif
        }

        private bool ReadDiveInput()
        {
#if ENABLE_INPUT_SYSTEM
            bool keyboardDive = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed);
            bool gamepadDive = Gamepad.current != null && (Gamepad.current.leftTrigger.isPressed || Gamepad.current.buttonEast.isPressed);
            return keyboardDive || gamepadDive;
#else
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
#endif
        }

        private void UpdateAnimator(Vector2 input)
        {
            if (animator == null) return;
            animator.SetFloat(SpeedHash, input.magnitude * (isUnderwater ? swimSpeed : moveSpeed));
            animator.SetBool(IsGroundedHash, isUnderwater || characterController.isGrounded);
        }

        #endregion

        #region IWaterSubmergible Implementation

        public void OnEnterWater(WaterBody waterBody)
        {
            SetUnderwater(true);
        }

        public void OnStayWater(WaterBody waterBody, float waterSurfaceY)
        {
            currentWaterSurfaceY = waterSurfaceY;
            bool inWater = (transform.position.y + swimTriggerDepth) < waterSurfaceY;
            SetUnderwater(inWater);
        }

        public void OnExitWater(WaterBody waterBody)
        {
            SetUnderwater(false);
            isHeadSubmerged = false;
        }

        #endregion
    }
}
