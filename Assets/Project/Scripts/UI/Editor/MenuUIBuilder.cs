#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeTheFlood.UI.Editor
{
    /// <summary>
    /// Editor utility to automatically construct the complete Start Menu UI hierarchy in GameMenu.unity.
    /// Access via: Tools -> Escape The Flood -> Build Game Menu UI
    /// </summary>
    public static class MenuUIBuilder
    {
        [MenuItem("Tools/Escape The Flood/Build Game Menu UI")]
        public static void BuildMenuUI()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
            }

            // Configure CanvasScaler for responsive 1080p display
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }

            // Ensure EventSystem exists
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }

            // Clean up any existing MenuRoot under canvas to prevent duplication
            Transform existingMenu = canvas.transform.Find("MenuRoot");
            if (existingMenu != null)
            {
                Undo.DestroyObjectImmediate(existingMenu.gameObject);
            }

            // Root Container
            GameObject menuRoot = new GameObject("MenuRoot", typeof(RectTransform));
            menuRoot.transform.SetParent(canvas.transform, false);
            StretchFull(menuRoot.GetComponent<RectTransform>());
            Undo.RegisterCreatedObjectUndo(menuRoot, "Create MenuRoot");

            // Attach MainMenuController
            MainMenuController controller = menuRoot.AddComponent<MainMenuController>();

            // 1. Fullscreen Dark Ocean Background
            GameObject bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGO.transform.SetParent(menuRoot.transform, false);
            StretchFull(bgGO.GetComponent<RectTransform>());
            Image bgImage = bgGO.GetComponent<Image>();
            bgImage.color = new Color(0.04f, 0.08f, 0.15f, 0.94f); // Deep flood blue/slate

            // 2. Title Section
            GameObject titleGO = new GameObject("TitleSection", typeof(RectTransform));
            titleGO.transform.SetParent(menuRoot.transform, false);
            RectTransform titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.8f);
            titleRect.anchorMax = new Vector2(0.5f, 0.8f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(1000f, 160f);

            Text mainTitle = CreateText(titleGO, "MainTitle", "ESCAPE THE FLOODED BUILDING", 52, FontStyle.Bold, new Color(0.35f, 0.85f, 1f));
            RectTransform mainTitleRect = mainTitle.GetComponent<RectTransform>();
            mainTitleRect.anchorMin = new Vector2(0, 0.4f);
            mainTitleRect.anchorMax = new Vector2(1, 1f);
            mainTitleRect.offsetMin = Vector2.zero;
            mainTitleRect.offsetMax = Vector2.zero;

            Text subtitle = CreateText(titleGO, "Subtitle", "Academic Midterm Project — Group 3", 22, FontStyle.Italic, new Color(0.7f, 0.82f, 0.95f));
            RectTransform subRect = subtitle.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0, 0f);
            subRect.anchorMax = new Vector2(1, 0.4f);
            subRect.offsetMin = Vector2.zero;
            subRect.offsetMax = Vector2.zero;

            // 3. Main Navigation Buttons Panel
            GameObject mainButtonsGO = new GameObject("MainButtonsPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
            mainButtonsGO.transform.SetParent(menuRoot.transform, false);
            RectTransform buttonsRect = mainButtonsGO.GetComponent<RectTransform>();
            buttonsRect.anchorMin = new Vector2(0.5f, 0.38f);
            buttonsRect.anchorMax = new Vector2(0.5f, 0.38f);
            buttonsRect.pivot = new Vector2(0.5f, 0.5f);
            buttonsRect.sizeDelta = new Vector2(380f, 320f);

            VerticalLayoutGroup vlg = mainButtonsGO.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 18f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            Button btnPlay = CreateMenuButton(mainButtonsGO, "Btn_Play", "START GAME", new Color(0.12f, 0.55f, 0.45f), new Color(0.18f, 0.75f, 0.62f));
            Button btnHowToPlay = CreateMenuButton(mainButtonsGO, "Btn_HowToPlay", "HOW TO PLAY", new Color(0.15f, 0.28f, 0.45f), new Color(0.22f, 0.40f, 0.65f));
            Button btnCredits = CreateMenuButton(mainButtonsGO, "Btn_Credits", "CREDITS", new Color(0.15f, 0.28f, 0.45f), new Color(0.22f, 0.40f, 0.65f));
            Button btnQuit = CreateMenuButton(mainButtonsGO, "Btn_Quit", "QUIT GAME", new Color(0.55f, 0.22f, 0.22f), new Color(0.75f, 0.30f, 0.30f));

            // 4. Modal: How to Play Panel
            GameObject howToPlayGO = CreateModalPanel(menuRoot.transform, "HowToPlayPanel", "HOW TO PLAY",
                "SURVIVAL OBJECTIVE:\n" +
                "The building's basement doors have burst and floodwaters are rising rapidly!\n" +
                "Ascend vertically from room to room, jumping across floating and crumbling furniture\n" +
                "to reach the rescue helicopter waiting on the rooftop.\n\n" +
                "CONTROLS:\n" +
                "• W / A / S / D  or  Left Stick : Move & Swim\n" +
                "• Spacebar  or  Gamepad A : Jump & Ascend in Water\n\n" +
                "HAZARDS:\n" +
                "• Breath Meter: Keep your oxygen above 0% when submerged underwater.\n" +
                "• Shark Threat: Watch out for patrolling sharks in flooded hallways!",
                out Button htpBackBtn);

            // 5. Modal: Credits Panel
            GameObject creditsGO = CreateModalPanel(menuRoot.transform, "CreditsPanel", "DEVELOPMENT TEAM — GROUP 3",
                "• Member 1: Mr. Kheng Sokthearith\n" +
                "  Game Core, Architecture & Scene Integration (Lead)\n\n" +
                "• Member 2: Mr. Yeat David\n" +
                "  Environment, Level Design & Platform Mechanics\n\n" +
                "• Member 3: Mr. Thang Saoly\n" +
                "  Water System, Flood Rising Simulation & Shaders\n\n" +
                "• Member 4: Mr. Tiek Chhunhour\n" +
                "  Shark AI, Hazard System & Helicopter Rescue",
                out Button credBackBtn);

            // Connect Persistent Button Click Events
            UnityEventTools.AddPersistentListener(btnPlay.onClick, controller.PlayGame);
            UnityEventTools.AddPersistentListener(btnHowToPlay.onClick, controller.OpenHowToPlay);
            UnityEventTools.AddPersistentListener(btnCredits.onClick, controller.OpenCredits);
            UnityEventTools.AddPersistentListener(btnQuit.onClick, controller.QuitGame);
            UnityEventTools.AddPersistentListener(htpBackBtn.onClick, controller.ShowMainMenu);
            UnityEventTools.AddPersistentListener(credBackBtn.onClick, controller.ShowMainMenu);

            // Wire Serialized References on Controller
            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("mainButtonsPanel").objectReferenceValue = mainButtonsGO;
            so.FindProperty("howToPlayPanel").objectReferenceValue = howToPlayGO;
            so.FindProperty("creditsPanel").objectReferenceValue = creditsGO;
            so.ApplyModifiedProperties();

            // Set default visibility
            howToPlayGO.SetActive(false);
            creditsGO.SetActive(false);
            mainButtonsGO.SetActive(true);

            Selection.activeGameObject = menuRoot;
            EditorUtility.SetDirty(canvas.gameObject);
            Debug.Log("[MenuUIBuilder] Game Menu UI successfully created and wired up!");
            EditorUtility.DisplayDialog("Menu UI Generated", "The Start Game Menu UI has been successfully created with buttons, modals, and event wiring!", "OK");
        }

        #region UI Construction Helpers

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Button CreateMenuButton(GameObject parent, string name, string label, Color normalColor, Color highlightColor)
        {
            GameObject btnGO = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGO.transform.SetParent(parent.transform, false);

            RectTransform rt = btnGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(380f, 56f);

            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 56f;

            Image img = btnGO.GetComponent<Image>();
            img.color = normalColor;

            Button btn = btnGO.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = normalColor;
            cb.highlightedColor = highlightColor;
            cb.pressedColor = normalColor * 0.8f;
            cb.selectedColor = highlightColor;
            btn.colors = cb;

            Text text = CreateText(btnGO, "Text", label, 22, FontStyle.Bold, Color.white);
            StretchFull(text.GetComponent<RectTransform>());

            return btn;
        }

        private static GameObject CreateModalPanel(Transform parent, string panelName, string title, string content, out Button backButton)
        {
            GameObject panelGO = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(parent, false);

            RectTransform panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(780f, 520f);

            Image panelImg = panelGO.GetComponent<Image>();
            panelImg.color = new Color(0.06f, 0.12f, 0.20f, 0.97f);

            // Title
            Text titleText = CreateText(panelGO, "ModalTitle", title, 32, FontStyle.Bold, new Color(0.35f, 0.85f, 1f));
            RectTransform titleRt = titleText.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.85f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(30f, 0f);
            titleRt.offsetMax = new Vector2(-30f, -20f);

            // Body text
            Text bodyText = CreateText(panelGO, "ModalBody", content, 18, FontStyle.Normal, new Color(0.9f, 0.94f, 1f));
            bodyText.alignment = TextAnchor.UpperLeft;
            RectTransform bodyRt = bodyText.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0f, 0.2f);
            bodyRt.anchorMax = new Vector2(1f, 0.82f);
            bodyRt.offsetMin = new Vector2(40f, 0f);
            bodyRt.offsetMax = new Vector2(-40f, 0f);

            // Back button
            backButton = CreateMenuButton(panelGO, "Btn_Back", "BACK", new Color(0.18f, 0.32f, 0.48f), new Color(0.28f, 0.48f, 0.70f));
            RectTransform backRt = backButton.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.5f, 0.08f);
            backRt.anchorMax = new Vector2(0.5f, 0.08f);
            backRt.pivot = new Vector2(0.5f, 0.5f);
            backRt.sizeDelta = new Vector2(200f, 48f);

            return panelGO;
        }

        private static Text CreateText(GameObject parent, string name, string textContent, int fontSize, FontStyle style, Color color)
        {
            GameObject textGO = new GameObject(name, typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(parent.transform, false);

            Text text = textGO.GetComponent<Text>();
            text.text = textContent;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            return text;
        }

        #endregion
    }
}
#endif
