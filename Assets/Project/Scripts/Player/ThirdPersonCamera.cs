using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeTheFlood.Player
{
    /// <summary>
    /// ThirdPersonCamera: orbit camera for the player.
    /// - Mouse / right stick rotates around the player (PlayerMovement already moves relative to this camera).
    /// - Pulls in when a wall, floor or furniture is between the player and the camera.
    ///   Invisible colliders (renderer disabled, e.g. invisible barriers) and triggers are ignored.
    /// - Locks and hides the cursor while the game is running; leaves it alone while paused or on end screens.
    /// Attach to the Main Camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Player to follow. Auto-found from PlayerMovement if empty.")]
        [SerializeField] private Transform target;

        [Tooltip("Height above the player's feet the camera orbits around and looks at.")]
        [SerializeField] private float pivotHeight = 2.6f;

        [Header("Distance")]
        [SerializeField] private float distance = 7f;
        [SerializeField] private float minDistance = 1.0f;

        [Tooltip("How fast the camera moves back out after an obstacle is gone (units/second).")]
        [SerializeField] private float distanceRecoverSpeed = 6f;

        [Header("Look")]
        [Tooltip("Degrees per mouse pixel.")]
        [SerializeField] private float mouseSensitivity = 0.12f;

        [Tooltip("Degrees per second at full right-stick deflection.")]
        [SerializeField] private float stickSensitivity = 140f;

        [SerializeField] private bool invertY = false;
        [SerializeField] private float minPitch = -25f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float startPitch = 20f;

        [Header("Collision")]
        [SerializeField] private float collisionRadius = 0.3f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorWhilePlaying = true;

        private float yaw;
        private float pitch;
        private float currentDistance;
        private readonly RaycastHit[] hits = new RaycastHit[16];

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            SnapBehindTarget();
        }

        private void Start()
        {
            if (target == null)
            {
                PlayerMovement player = PlayerMovement.Instance != null ? PlayerMovement.Instance : FindFirstObjectByType<PlayerMovement>();
                if (player != null) target = player.transform;
            }
            SnapBehindTarget();
        }

        private void SnapBehindTarget()
        {
            if (target == null) return;
            yaw = target.eulerAngles.y;
            pitch = startPitch;
            currentDistance = distance;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // Only look around / lock the cursor while the game is running (not paused or on end screens)
            if (Time.timeScale > 0f)
            {
                ReadLookInput();
                if (lockCursorWhilePlaying && Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            Vector3 pivot = target.position + Vector3.up * pivotHeight;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rotation * Vector3.back;

            float allowed = GetAllowedDistance(pivot, back);
            if (allowed < currentDistance)
            {
                currentDistance = allowed; // snap in so we never see through walls
            }
            else
            {
                currentDistance = Mathf.MoveTowards(currentDistance, allowed, distanceRecoverSpeed * Time.unscaledDeltaTime);
            }

            transform.SetPositionAndRotation(pivot + back * currentDistance, rotation);
        }

        private void ReadLookInput()
        {
            Vector2 look = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                look += Mouse.current.delta.ReadValue() * mouseSensitivity;
            }
            if (Gamepad.current != null)
            {
                look += Gamepad.current.rightStick.ReadValue() * (stickSensitivity * Time.deltaTime);
            }
#endif
            yaw += look.x;
            pitch += invertY ? look.y : -look.y;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        private float GetAllowedDistance(Vector3 pivot, Vector3 direction)
        {
            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, hits, distance, collisionMask, QueryTriggerInteraction.Ignore);
            float closest = distance;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.distance <= 0f) continue;                              // overlapping at start
                if (hit.collider.transform.IsChildOf(target.root)) continue;  // the player itself

                Renderer hitRenderer = hit.collider.GetComponent<Renderer>();
                if (hitRenderer != null && !hitRenderer.enabled) continue;     // invisible barriers

                closest = Mathf.Min(closest, hit.distance);
            }

            return Mathf.Clamp(closest - 0.1f, minDistance, distance);
        }
    }
}
