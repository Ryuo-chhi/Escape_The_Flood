using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeTheFlood.Player
{
    /// <summary>
    /// PlayerMovement: Third-person movement controller using CharacterController.
    /// Supports Unity's New Input System (WASD / Left Stick / Space jump).
    /// Moves relative to the Main Camera view direction.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Horizontal movement speed in units per second.")]
        [SerializeField] private float moveSpeed = 5f;

        [Tooltip("How quickly the character turns to face the movement direction (degrees per second).")]
        [SerializeField] private float rotationSpeed = 720f;

        [Header("Jump & Gravity")]
        [Tooltip("Jump apex height in units.")]
        [SerializeField] private float jumpHeight = 1.5f;

        [Tooltip("Downward gravitational acceleration.")]
        [SerializeField] private float gravity = -18f;

        [Header("Camera Reference")]
        [Tooltip("Camera used to calculate forward/right movement. Defaults to Camera.main if unassigned.")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController characterController;
        private Animator animator;
        private Vector3 verticalVelocity;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            animator = GetComponentInChildren<Animator>();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            Vector2 input = ReadInputVector();
            bool jumpPressed = ReadJumpInput();

            MoveCharacter(input);
            ApplyGravityAndJump(jumpPressed);
            UpdateAnimator(input);
        }

        /// <summary>
        /// Reads 2D movement input from Keyboard or Gamepad via Unity Input System.
        /// </summary>
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
                if (stick.sqrMagnitude > input.sqrMagnitude)
                {
                    input = stick;
                }
            }
#else
            input.x = Input.GetAxisRaw("Horizontal");
            input.y = Input.GetAxisRaw("Vertical");
#endif

            return input.magnitude > 1f ? input.normalized : input;
        }

        /// <summary>
        /// Reads jump button press.
        /// </summary>
        private bool ReadJumpInput()
        {
#if ENABLE_INPUT_SYSTEM
            bool keyboardJump = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            bool gamepadJump = Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
            return keyboardJump || gamepadJump;
#else
            return Input.GetButtonDown("Jump");
#endif
        }

        /// <summary>
        /// Moves character horizontally relative to camera direction and smoothly rotates toward heading.
        /// </summary>
        private void MoveCharacter(Vector2 input)
        {
            if (input.sqrMagnitude < 0.001f)
            {
                // No horizontal movement
                return;
            }

            Vector3 moveDirection;

            if (cameraTransform != null)
            {
                // Calculate camera-relative forward and right vectors projected onto XZ plane
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

            moveDirection.Normalize();

            // Smooth rotation towards movement direction
            if (moveDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            // Apply horizontal movement
            characterController.Move(moveDirection * (moveSpeed * Time.deltaTime));
        }

        /// <summary>
        /// Manages grounded state, downward gravity, and jumping.
        /// </summary>
        private void ApplyGravityAndJump(bool jumpPressed)
        {
            if (characterController.isGrounded)
            {
                // Reset vertical velocity when grounded; small negative value keeps controller grounded
                if (verticalVelocity.y < 0f)
                {
                    verticalVelocity.y = -2f;
                }

                if (jumpPressed)
                {
                    // v = sqrt(2 * h * -g)
                    verticalVelocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
                }
            }
            else
            {
                // Apply continuous downward gravity
                verticalVelocity.y += gravity * Time.deltaTime;
            }

            // Apply vertical motion
            characterController.Move(verticalVelocity * Time.deltaTime);
        }

        /// <summary>
        /// Sends motion data to Animator if present.
        /// </summary>
        private void UpdateAnimator(Vector2 input)
        {
            if (animator == null) return;

            animator.SetFloat(SpeedHash, input.magnitude * moveSpeed);
            animator.SetBool(IsGroundedHash, characterController.isGrounded);
        }
    }
}
