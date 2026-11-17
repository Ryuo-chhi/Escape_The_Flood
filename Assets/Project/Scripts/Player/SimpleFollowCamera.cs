using UnityEngine;

namespace EscapeTheFlood.Player
{
    /// <summary>
    /// Simple third-person follow camera for testing and prototyping.
    /// Attach this component to the Main Camera.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SimpleFollowCamera : MonoBehaviour
    {
        [Tooltip("The target Transform to follow (Player).")]
        [SerializeField] private Transform target;

        [Tooltip("Offset relative to the target's position (framed for 2x player size).")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 3.8f, -6.5f);

        [Tooltip("Follow smoothing time in seconds (lower = snappier, 0.05-0.1 is buttery smooth).")]
        [SerializeField] private float smoothTime = 0.08f;

        [Tooltip("Look ahead height offset on the player (framed for 2x player height).")]
        [SerializeField] private float lookAtHeight = 2.4f;

        private Vector3 currentVelocity = Vector3.zero;

        public void SetTarget(Transform newTarget) => target = newTarget;

        private void LateUpdate()
        {
            if (target == null) return;

            // 1. Smoothly follow target position using SmoothDamp (completely eliminates frame-rate jitter)
            Vector3 desiredPosition = target.position + offset;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothTime);

            // 2. Smoothly rotate towards target without snapping
            Vector3 lookTarget = target.position + Vector3.up * lookAtHeight;
            Vector3 directionToTarget = lookTarget - transform.position;
            if (directionToTarget.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 15f * Time.deltaTime);
            }
        }
    }
}
