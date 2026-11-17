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

        [Tooltip("Offset relative to the target's position.")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.5f, -4f);

        [Tooltip("Follow smoothing factor (higher = snappier).")]
        [SerializeField] private float smoothSpeed = 10f;

        [Tooltip("Look ahead height offset on the player.")]
        [SerializeField] private float lookAtHeight = 1.2f;

        public void SetTarget(Transform newTarget) => target = newTarget;

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPosition = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

            Vector3 lookTarget = target.position + Vector3.up * lookAtHeight;
            transform.LookAt(lookTarget);
        }
    }
}
