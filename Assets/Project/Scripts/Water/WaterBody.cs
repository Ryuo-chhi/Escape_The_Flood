using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace EscapeTheFlood.Water
{
    /// <summary>
    /// WaterBody: Core Flood Simulation Component.
    /// 
    /// Responsibilities:
    /// 1. Continuously raises the flood level along the Y-axis over time while keeping
    ///    the bottom pinned to floorY.
    /// 2. Handles center-pivot 3D volume mesh, flat surface plane, and separate surface/volume objects.
    /// 3. Manages the water trigger volume and submerge detection.
    /// 4. Informs submerged entities (Player) via IWaterSubmergible for breath depletion and swim physics.
    /// 5. Provides world-space water bounds and surface height queries for Shark AI.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class WaterBody : MonoBehaviour
    {
        public static WaterBody Instance { get; private set; }

        [Header("Rising Parameters")]
        [Tooltip("Floor Y position where the bottom of the water remains pinned.")]
        [FormerlySerializedAs("minWaterY")]
        [SerializeField] private float floorY = -4.16f;

        [Tooltip("Maximum height the water can rise to above the floor.")]
        [FormerlySerializedAs("maxWaterY")]
        [SerializeField] private float maxHeight = 30f;

        [Tooltip("Speed at which the water level rises in units per second.")]
        [SerializeField] private float riseSpeed = 0.5f;

        [Tooltip("Whether the water is actively rising.")]
        [SerializeField] private bool isRising = true;

        [Tooltip("Current height of the water above floorY.")]
        [SerializeField] private float currentHeight = 0f;

        [Header("Visual & Volume Setup (Optional)")]
        [Tooltip("Optional reference to separate flat surface plane transform. If unassigned and this object has a plane mesh, this object is treated as the surface.")]
        [SerializeField] private Transform surfacePlane;

        [Tooltip("Optional reference to separate volume object (e.g. center-pivot cube). If unassigned and this object has a 3D mesh, this object is treated as the volume.")]
        [SerializeField] private Transform volumeObject;

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

        #region Public Properties

        /// <summary>
        /// Current Y level of the water surface in world space.
        /// </summary>
        public float CurrentWaterY => floorY + currentHeight + waveOffset;

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
        /// Maximum height the water can rise above the floor.
        /// </summary>
        public float MaxHeight => maxHeight;

        /// <summary>
        /// World Y position of the floor where water bottom is pinned.
        /// </summary>
        public float FloorY => floorY;

        /// <summary>
        /// Current height of the water above floor Y.
        /// </summary>
        public float CurrentHeight => currentHeight;

        /// <summary>
        /// Maximum world Y level reachable by the water surface (floorY + maxHeight).
        /// </summary>
        public float MaxWaterY => floorY + maxHeight;

        /// <summary>
        /// Starting / Floor world Y level of the flood.
        /// </summary>
        public float MinWaterY => floorY;

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
            // If currentHeight wasn't set, deduce it from current transform position relative to floorY
            if (currentHeight <= 0.0001f)
            {
                currentHeight = Mathf.Clamp(transform.position.y - floorY, 0f, maxHeight);
            }

            // Immediately pin bottom to floorY and align top
            ApplyWaterHeight(currentHeight);
        }

        private void Update()
        {
            UpdateWaveEffect();

            if (isRising)
            {
                Rise(Time.deltaTime);
            }
            else
            {
                ApplyWaterHeight(currentHeight);
            }
        }

        #endregion

        #region Core Logic

        /// <summary>
        /// Advances the water level upward by delta time, keeping bottom pinned to floorY.
        /// </summary>
        /// <param name="deltaTime">Time slice in seconds (Time.deltaTime).</param>
        public void Rise(float deltaTime)
        {
            if (currentHeight >= maxHeight)
            {
                isRising = false;
                OnMaxWaterLevelReached?.Invoke();
                return;
            }

            currentHeight += riseSpeed * deltaTime;
            currentHeight = Mathf.Min(currentHeight, maxHeight);

            ApplyWaterHeight(currentHeight);

            OnWaterLevelChanged?.Invoke(CurrentWaterY);

            if (currentHeight >= maxHeight)
            {
                isRising = false;
                OnMaxWaterLevelReached?.Invoke();
            }
        }

        /// <summary>
        /// Updates the water surface and volume geometry so the bottom stays pinned to floorY
        /// while the top rises to (floorY + height).
        /// Handles center-pivot cube/mesh, flat surface plane, and separate surface plane + volume.
        /// </summary>
        public void ApplyWaterHeight(float height)
        {
            float surfaceY = floorY + height;
            float surfaceYWithWave = surfaceY + waveOffset;

            // Check if this object is a 3D mesh volume (center-pivot cube) or a flat surface plane
            bool thisIsVolume = false;
            float thisMeshRealHeight = 1f;
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                float meshY = mf.sharedMesh.bounds.size.y;
                if (meshY > 0.001f)
                {
                    thisIsVolume = true;
                    thisMeshRealHeight = meshY;
                }
            }

            // 1. Center-pivot volume mesh (either volumeObject, or this.transform if thisIsVolume)
            Transform vol = volumeObject != null ? volumeObject : (thisIsVolume ? transform : null);
            if (vol != null)
            {
                float meshRealHeight = thisMeshRealHeight;
                if (vol != transform)
                {
                    MeshFilter volMf = vol.GetComponent<MeshFilter>();
                    if (volMf != null && volMf.sharedMesh != null && volMf.sharedMesh.bounds.size.y > 0.001f)
                    {
                        meshRealHeight = volMf.sharedMesh.bounds.size.y;
                    }
                }

                // Account for parent scale and rotation if any
                float parentScaleY = vol.parent != null ? vol.parent.lossyScale.y : 1f;
                if (Mathf.Abs(parentScaleY) < 0.0001f) parentScaleY = 1f;

                // Change scale.y and set position.y = floorY + height * 0.5 in the same frame
                Vector3 volScale = vol.localScale;
                volScale.y = height / (meshRealHeight * parentScaleY);
                vol.localScale = volScale;

                Vector3 volPos = vol.position;
                volPos.y = floorY + (height * 0.5f);
                vol.position = volPos;
            }

            // 2. Flat surface plane (either surfacePlane, or this.transform if not a 3D volume)
            Transform surf = surfacePlane != null ? surfacePlane : (!thisIsVolume ? transform : null);
            if (surf != null && surf != vol)
            {
                Vector3 surfPos = surf.position;
                surfPos.y = surfaceYWithWave;
                surf.position = surfPos;
            }

            // 3. Water Trigger Volume (BoxCollider)
            if (waterCollider != null)
            {
                Transform colTransform = waterCollider.transform;
                float parentScaleY = colTransform.lossyScale.y;
                if (Mathf.Abs(parentScaleY) < 0.0001f) parentScaleY = 1f;

                if (colTransform == surf)
                {
                    // Attached to surface plane: collider extends downwards from surface to floorY
                    float localColHeight = height / parentScaleY;
                    Vector3 size = waterCollider.size;
                    size.y = localColHeight;
                    waterCollider.size = size;

                    Vector3 center = waterCollider.center;
                    center.y = -localColHeight * 0.5f;
                    waterCollider.center = center;
                }
                else if (colTransform == vol)
                {
                    // Attached to center-pivot volume: collider already centered on volume
                    Vector3 size = waterCollider.size;
                    size.y = thisMeshRealHeight;
                    waterCollider.size = size;

                    Vector3 center = waterCollider.center;
                    center.y = 0f;
                    waterCollider.center = center;
                }
                else
                {
                    // Standalone collider transform
                    Vector3 colPos = colTransform.position;
                    colPos.y = floorY + (height * 0.5f);
                    colTransform.position = colPos;

                    Vector3 size = waterCollider.size;
                    size.y = height / parentScaleY;
                    waterCollider.size = size;
                }
            }
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
            currentHeight = Mathf.Clamp(targetY - floorY, 0f, maxHeight);
            ApplyWaterHeight(currentHeight);
            OnWaterLevelChanged?.Invoke(CurrentWaterY);
        }

        #endregion

        #region Spatial & Containment Queries

        /// <summary>
        /// Returns the 3D bounding box of the water volume in world space.
        /// Accurately bounds from floorY up to CurrentWaterY.
        /// </summary>
        public Bounds GetWaterBounds()
        {
            if (waterCollider != null)
            {
                Bounds b = waterCollider.bounds;
                Vector3 max = b.max;
                max.y = CurrentWaterY;
                Vector3 min = b.min;
                min.y = floorY;
                b.SetMinMax(min, max);
                return b;
            }

            Renderer rend = GetComponent<Renderer>();
            if (rend != null)
            {
                Bounds b = rend.bounds;
                Vector3 min = b.min;
                Vector3 max = b.max;
                min.y = floorY;
                max.y = CurrentWaterY;
                b.SetMinMax(min, max);
                return b;
            }

            Vector3 center = transform.position;
            center.y = (floorY + CurrentWaterY) * 0.5f;
            Vector3 size = new Vector3(50f, Mathf.Max(0.5f, CurrentWaterY - floorY), 50f);
            return new Bounds(center, size);
        }

        /// <summary>
        /// Checks whether a given world-space coordinate is below the water surface
        /// and within the horizontal perimeter of the water body.
        /// </summary>
        public bool IsPositionUnderwater(Vector3 worldPosition)
        {
            if (worldPosition.y >= CurrentWaterY) return false;
            Bounds b = GetWaterBounds();
            return worldPosition.x >= b.min.x && worldPosition.x <= b.max.x &&
                   worldPosition.z >= b.min.z && worldPosition.z <= b.max.z &&
                   worldPosition.y >= b.min.y;
        }

        /// <summary>
        /// Returns true if the position is fully within the water bounds with an optional margin.
        /// </summary>
        public bool IsPositionInsideWater(Vector3 worldPosition, float margin = 0f)
        {
            Bounds b = GetWaterBounds();
            if (margin > 0f)
            {
                b.Expand(-margin * 2f);
            }
            return b.Contains(worldPosition);
        }

        /// <summary>
        /// Clamps a world position so it is strictly contained inside the water volume.
        /// </summary>
        public Vector3 ClampPositionToWater(Vector3 worldPosition, float horizontalMargin = 0f, float topMargin = 0f, float bottomMargin = 0f)
        {
            Bounds b = GetWaterBounds();
            float minX = b.min.x + horizontalMargin;
            float maxX = b.max.x - horizontalMargin;
            float minZ = b.min.z + horizontalMargin;
            float maxZ = b.max.z - horizontalMargin;
            float minY = b.min.y + bottomMargin;
            float maxY = CurrentWaterY - topMargin;

            Vector3 clamped = worldPosition;
            if (minX < maxX) clamped.x = Mathf.Clamp(clamped.x, minX, maxX);
            if (minZ < maxZ) clamped.z = Mathf.Clamp(clamped.z, minZ, maxZ);
            if (minY < maxY) clamped.y = Mathf.Clamp(clamped.y, minY, maxY);
            return clamped;
        }

        /// <summary>
        /// Returns how deep a submerged position is below the surface (in world units).
        /// </summary>
        public float GetSubmersionDepth(Vector3 worldPosition)
        {
            return Mathf.Max(0f, CurrentWaterY - worldPosition.y);
        }

        /// <summary>
        /// Returns the vertical clamp bounds for aquatic entities (e.g. Shark).
        /// </summary>
        public void GetVerticalSwimBounds(out float bottomY, out float surfaceY)
        {
            bottomY = floorY;
            surfaceY = CurrentWaterY;
        }

        #endregion

        #region Trigger Callbacks

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<IWaterSubmergible>(out var submergible))
            {
                submergible.OnEnterWater(this);
            }
            else if (other.CompareTag("Player"))
            {
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
            float surfaceY = floorY + currentHeight + waveOffset;
            Vector3 center = transform.position;
            center.y = surfaceY;
            Vector3 size = waterCollider != null ? waterCollider.size : new Vector3(30f, 0.1f, 30f);
            size.y = 0.1f;
            Gizmos.DrawCube(center, size);

            // Draw surface boundary wireframe
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(center, size);

            // Draw water volume wireframe (floorY to surfaceY)
            Gizmos.color = new Color(0f, 0.5f, 0.9f, 0.25f);
            Vector3 volCenter = center;
            volCenter.y = floorY + (currentHeight * 0.5f);
            Vector3 volSize = size;
            volSize.y = Mathf.Max(0.1f, currentHeight);
            Gizmos.DrawWireCube(volCenter, volSize);

            // Draw max water height wireframe
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Vector3 maxPos = center;
            maxPos.y = floorY + maxHeight;
            Gizmos.DrawWireCube(maxPos, size);
        }
#endif

        #endregion
    }
}
