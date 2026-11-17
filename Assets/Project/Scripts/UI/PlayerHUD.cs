using UnityEngine;
using UnityEngine.UI;
using EscapeTheFlood.Player;

namespace EscapeTheFlood.UI
{
    /// <summary>
    /// PlayerHUD: Displays Health and Breath meters at the top-left corner of the screen.
    /// Listens to PlayerMovement events or queries real-time vitals.
    /// Can automatically generate the UI elements if unassigned.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHUD : MonoBehaviour
    {
        [Header("Target Reference")]
        [Tooltip("The Player to monitor. Defaults to PlayerMovement.Instance if unassigned.")]
        [SerializeField] private PlayerMovement player;

        [Header("Health Bar UI")]
        [SerializeField] private Image healthBarFill;
        [SerializeField] private Text healthText;

        [Header("Breath Bar UI")]
        [SerializeField] private Image breathBarFill;
        [SerializeField] private Text breathText;

        [Header("Visual Colors")]
        [SerializeField] private Color healthFullColor = new Color(0.2f, 0.85f, 0.35f);
        [SerializeField] private Color healthLowColor = new Color(0.9f, 0.15f, 0.15f);
        [SerializeField] private Color breathColor = new Color(0.15f, 0.75f, 0.98f);
        [SerializeField] private Color breathCriticalColor = new Color(0.95f, 0.35f, 0.15f);

        private Canvas hudCanvas;

        private void Awake()
        {
            if (player == null)
            {
                player = FindFirstObjectByType<PlayerMovement>();
            }

            // Auto-generate top-left HUD elements if not manually wired in inspector
            if (healthBarFill == null || breathBarFill == null)
            {
                GenerateRuntimeHUD();
            }
        }

        private void OnEnable()
        {
            if (player != null)
            {
                player.OnHealthChanged += UpdateHealthBar;
                player.OnBreathChanged += UpdateBreathBar;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.OnHealthChanged -= UpdateHealthBar;
                player.OnBreathChanged -= UpdateBreathBar;
            }
        }

        private void Start()
        {
            if (player == null)
            {
                player = FindFirstObjectByType<PlayerMovement>();
                if (player != null)
                {
                    player.OnHealthChanged += UpdateHealthBar;
                    player.OnBreathChanged += UpdateBreathBar;
                    UpdateHealthBar(player.Health, player.MaxHealth);
                    UpdateBreathBar(player.Breath, player.MaxBreath);
                }
            }
            else
            {
                UpdateHealthBar(player.Health, player.MaxHealth);
                UpdateBreathBar(player.Breath, player.MaxBreath);
            }
        }

        private void Update()
        {
            // Continuous fallback update
            if (player != null)
            {
                UpdateHealthBar(player.Health, player.MaxHealth);
                UpdateBreathBar(player.Breath, player.MaxBreath);
            }
        }

        public void UpdateHealthBar(float current, float max)
        {
            if (healthBarFill == null) return;

            float ratio = Mathf.Clamp01(current / (max > 0f ? max : 100f));
            healthBarFill.fillAmount = ratio;
            healthBarFill.color = Color.Lerp(healthLowColor, healthFullColor, ratio);

            if (healthText != null)
            {
                healthText.text = $"HP  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
            }
        }

        public void UpdateBreathBar(float current, float max)
        {
            if (breathBarFill == null) return;

            float ratio = Mathf.Clamp01(current / (max > 0f ? max : 100f));
            breathBarFill.fillAmount = ratio;

            if (ratio < 0.25f)
            {
                // Pulsing critical warning when breath is low
                float pulse = (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f;
                breathBarFill.color = Color.Lerp(breathCriticalColor, Color.white, pulse);
            }
            else
            {
                breathBarFill.color = breathColor;
            }

            if (breathText != null)
            {
                breathText.text = $"BREATH  {Mathf.CeilToInt(current)}%";
            }
        }

        /// <summary>
        /// Automatically generates a clean, responsive Top-Left HUD at runtime if none was placed in the scene.
        /// </summary>
        public void GenerateRuntimeHUD()
        {
            // 1. Locate or create screen-space Canvas
            hudCanvas = GetComponentInParent<Canvas>();
            if (hudCanvas == null)
            {
                GameObject canvasGO = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                hudCanvas = canvasGO.GetComponent<Canvas>();
                hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }

            // 2. Top-Left Container Panel
            GameObject container = new GameObject("TopLeft_VitalsContainer", typeof(RectTransform), typeof(Image));
            container.transform.SetParent(hudCanvas.transform, false);

            RectTransform containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 1f);
            containerRect.anchorMax = new Vector2(0f, 1f);
            containerRect.pivot = new Vector2(0f, 1f);
            containerRect.anchoredPosition = new Vector2(25f, -25f);
            containerRect.sizeDelta = new Vector2(300f, 115f);

            Image containerBg = container.GetComponent<Image>();
            containerBg.color = new Color(0.04f, 0.08f, 0.14f, 0.85f); // Deep frosted slate

            // 3. Create Health Bar
            CreateBar(container.transform, "HealthBar", "HP  100 / 100", 25f, healthFullColor, out healthBarFill, out healthText);

            // 4. Create Breath Bar
            CreateBar(container.transform, "BreathBar", "BREATH  100%", -25f, breathColor, out breathBarFill, out breathText);
        }

        private void CreateBar(Transform parent, string barName, string defaultText, float yOffset, Color barColor, out Image fillImage, out Text labelText)
        {
            // Bar Background
            GameObject bgGO = new GameObject(barName + "_Bg", typeof(RectTransform), typeof(Image));
            bgGO.transform.SetParent(parent, false);

            RectTransform bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.anchoredPosition = new Vector2(0f, yOffset);
            bgRect.sizeDelta = new Vector2(260f, 32f);

            Image bgImg = bgGO.GetComponent<Image>();
            bgImg.color = new Color(0.12f, 0.16f, 0.22f, 0.95f);

            // Bar Fill
            GameObject fillGO = new GameObject(barName + "_Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(bgGO.transform, false);

            RectTransform fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            fillImage = fillGO.GetComponent<Image>();
            fillImage.color = barColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;

            // Bar Text
            GameObject textGO = new GameObject(barName + "_Text", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(bgGO.transform, false);

            RectTransform textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);

            labelText = textGO.GetComponent<Text>();
            labelText.text = defaultText;
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (labelText.font == null)
            {
                labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            labelText.fontSize = 15;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.raycastTarget = false;
        }
    }
}
