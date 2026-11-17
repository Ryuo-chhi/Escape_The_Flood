using System;
using UnityEngine;

namespace EscapeTheFlood.Water
{
    /// <summary>
    /// WaterBody: Core Flood Simulation Component (Member 3 - Water System).
    /// 
    /// Responsibilities:
    /// 1. Continuously raises the flood level along the Y-axis over time.
    /// 2. Manages the water trigger volume and submerge detection.
    /// 3. Informs submerged entities (Player) via IWaterSubmergible for breath depletion and swim physics.
    /// 4. Provides water bounds and surface height queries for Member 4's Shark AI.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class WaterBody : MonoBehaviour
    {
        public static WaterBody Instance { get; private set; }

        [Header("Rising Parameters")]
        [Tooltip("Speed at which the water level rises in units per second.")]
        [SerializeField] private float riseSpeed = 0.5f;

        [Tooltip("Whether the water is actively rising.")]
        [SerializeField] private bool isRising = true;

        [Tooltip("Minimum starting Y position of the water surface (e.g. basement floor).")]
        [SerializeField] private float minWaterY = 0f;

        [Tooltip("Maximum Y position the water can reach (e.g. rooftop).")]
        [SerializeField] private float maxWaterY = 30f;

        [Header("Wave / Visual Properties")]
        [Tooltip("Optional wave vertical oscillation offset.")]
        [SerializeField] private float waveOffset = 0f;

        [Tooltip("Optional wave bobbing amplitude for procedural surface movement.")]
        [SerializeField] private float waveAmplitude = 0.05f;

        [Tooltip("Frequency of procedural wave bobbing.")]
        [SerializeField] private float waveFrequency = 1.5f;

        [Header("Trigger & Volume Components")]
        [Tooltip("Optional reference to the child water trigger collider. If empty, will search automatically.")]
        [SerializeField] private BoxCollider waterCollider;

        // Events for UI and external systems
        public event Action<float> OnWaterLevelChanged;
        public event Action OnMaxWaterLevelReached;

        #region Public Properties (UML Specification)

        /// <summary>
        /// Current Y level of the water surface.
        /// </summary>
        public float CurrentWaterY => transform.position.y + waveOffset;

        /// <summary>
        /// Rising speed in units per second.
        /// </summary>
        public float RiseSpeed
        {
            get => riseSpeed;
            set => riseSpeed = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Controls whether the flood is actively rising.
        /// </summary>
        public bool IsRising
        {
            get => isRising;
            set => isRising = value;
        }

        /// <summary>
        /// Wave vertical offset added to current surface Y.
        /// </summary>
        public float WaveOffset => waveOffset;

        /// <summary>
        /// Maximum height limit for the flood.
        /// </summary>
        public float MaxWaterY => maxWaterY;

        /// <summary>
        /// Minimum starting height of the flood.
        /// </summary>
        public float MinWaterY => minWaterY;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (waterCollider == null)
            {
                waterCollider = GetComponentInChildren<BoxCollider>();
            }

            if (waterCollider != null && !waterCollider.isTrigger)
            {
                Debug.LogWarning("[WaterBody] BoxCollider was not set to Trigger. Automatically enabling IsTrigger.", this);
                waterCollider.isTrigger = true;
            }
        }

        private void Start()
        {
            // Ensure starting height is clamped to minWaterY
            if (transform.position.y < minWaterY)
            {
                Vector3 pos = transform.position;
                pos.y = minWaterY;
                transform.position = pos;
            }
        }

        private void Update()
        {
            if (isRising)
            {
                Rise(Time.deltaTime);
            }

            UpdateWaveEffect();
        }

        #endregion

        #region Core Logic

        /// <summary>
        /// Advances the water level upward by delta time (UML Method: +rise(delta: float)).
        /// </summary>
        /// <param name="deltaTime">Time slice in seconds (typically Time.deltaTime).</param>
        public void Rise(float deltaTime)
        {
            if (transform.position.y >= maxWaterY)
            {
                isRising = false;
                OnMaxWaterLevelReached?.Invoke();
                return;
            }

            float newY = transform.position.y + (riseSpeed * deltaTime);
            newY = Mathf.Min(newY, maxWaterY);

            Vector3 currentPosition = transform.position;
            currentPosition.y = newY;
            transform.position = currentPosition;

            OnWaterLevelChanged?.Invoke(CurrentWaterY);
        }

        /// <summary>
        /// Updates the subtle procedural wave offset for visual dynamics.
        /// </summary>
        private void UpdateWaveEffect()
        {
            if (waveAmplitude > 0f)
            {
                waveOffset = Mathf.Sin(Time.time * waveFrequency) * waveAmplitude;
            }
            else
            {
                waveOffset = 0f;
            }
        }

        /// <summary>
        /// Instantly teleports or resets the water level to a target Y position.
        /// </summary>
        public void SetWaterLevel(float targetY)
        {
            Vector3 pos = transform.position;
            pos.y = Mathf.Clamp(targetY, minWaterY, maxWaterY);
            transform.position = pos;
            OnWaterLevelChanged?.Invoke(CurrentWaterY);
        }

        #endregion

        #region Spatial & Containment Queries (For Shark AI & Player)

        /// <summary>
        /// Checks whether a given world-space coordinate is below the water surface.
        /// Used by Member 4's Shark AI to determine if the player or target is submerged.
        /// </summary>
        /// <param name="worldPosition">World coordinate to test.</param>
        /// <returns>True if position is submerged below CurrentWaterY.</returns>
        public bool IsPositionUnderwater(Vector3 worldPosition)
        {
            return worldPosition.y < CurrentWaterY;
        }

        /// <summary>
        /// Returns how deep a submerged position is below the surface (in world units).
        /// </summary>
        /// <param name="worldPosition">World position to check.</param>
        /// <returns>Submersion depth, or 0 if above water.</returns>
        public float GetSubmersionDepth(Vector3 worldPosition)
        {
            return Mathf.Max(0f, CurrentWaterY - worldPosition.y);
        }

        /// <summary>
        /// Returns the vertical clamp bounds for aquatic entities (e.g. Shark).
        /// </summary>
        /// <param name="bottomY">Out parameter for bottom boundary.</param>
        /// <param name="surfaceY">Out parameter for top water surface boundary.</param>
        public void GetVerticalSwimBounds(out float bottomY, out float surfaceY)
        {
            bottomY = minWaterY;
            surfaceY = CurrentWaterY;
        }

        #endregion

        #region Trigger Callbacks (Submersion & Breath Interaction)

        private void OnTriggerEnter(Collider other)
        {
            // 1. Clean Interface Hook for Member 1's Player or submergible objects
            if (other.TryGetComponent<IWaterSubmergible>(out var submergible))
            {
                submergible.OnEnterWater(this);
            }
            else if (other.CompareTag("Player"))
            {
                // Fallback message in case interface is not yet attached
                other.SendMessage("OnEnterWater", this, SendMessageOptions.DontRequireReceiver);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other.TryGetComponent<IWaterSubmergible>(out var submergible))
            {
                submergible.OnStayWater(this, CurrentWaterY);
            }
            else if (other.CompareTag("Player"))
            {
                other.SendMessage("OnStayWater", CurrentWaterY, SendMessageOptions.DontRequireReceiver);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent<IWaterSubmergible>(out var submergible))
            {
                submergible.OnExitWater(this);
            }
            else if (other.CompareTag("Player"))
            {
                other.SendMessage("OnExitWater", this, SendMessageOptions.DontRequireReceiver);
            }
        }

        #endregion

        #region Editor Visualization

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw current water surface plane outline
            Gizmos.color = new Color(0.1f, 0.6f, 1f, 0.4f);
            Vector3 center = transform.position + Vector3.up * waveOffset;
            Vector3 size = waterCollider != null ? waterCollider.size : new Vector3(30f, 0.1f, 30f);
            size.y = 0.1f;
            Gizmos.DrawCube(center, size);

            // Draw surface boundary wireframe
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(center, size);

            // Draw max water height wireframe
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Vector3 maxPos = center;
            maxPos.y = maxWaterY;
            Gizmos.DrawWireCube(maxPos, size);
        }
#endif

        #endregion
    }
}
