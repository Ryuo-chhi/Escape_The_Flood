#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using EscapeTheFlood.UI;

namespace EscapeTheFlood.UI.Editor
{
    public static class PlayerHUDBuilder
    {
        [MenuItem("Tools/Escape The Flood/Add Player HUD to Scene")]
        public static void AddHUDToScene()
        {
            PlayerHUD existingHUD = Object.FindFirstObjectByType<PlayerHUD>();
            if (existingHUD != null)
            {
                Selection.activeGameObject = existingHUD.gameObject;
                EditorUtility.DisplayDialog("HUD Already Exists", "PlayerHUD already exists in this scene.", "OK");
                return;
            }

            GameObject hudManagerGO = new GameObject("PlayerHUD_Manager");
            PlayerHUD hud = hudManagerGO.AddComponent<PlayerHUD>();
            hud.GenerateRuntimeHUD();

            Undo.RegisterCreatedObjectUndo(hudManagerGO, "Create Player HUD");
            Selection.activeGameObject = hudManagerGO;
            Debug.Log("[PlayerHUDBuilder] Player HUD added to scene successfully!");
            EditorUtility.DisplayDialog("Player HUD Added", "Player HUD with Health and Breath bars has been added to the top-left corner!", "OK");
        }
    }
}
#endif
