using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeTheFlood.UI
{
    /// <summary>
    /// MainMenuController: Handles UI navigation, scene loading, and modal panels in GameMenu.
    /// Belongs to UI & Game State Management.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Scene Loading")]
        [Tooltip("Name of the main gameplay scene to load when clicking Start Game.")]
        [SerializeField] private string gameplaySceneName = "EscapeBuilding";

        [Header("Menu Panels")]
        [Tooltip("The main container holding the Play, How to Play, Credits, and Quit buttons.")]
        [SerializeField] private GameObject mainButtonsPanel;

        [Tooltip("The modal panel explaining game controls and objectives.")]
        [SerializeField] private GameObject howToPlayPanel;

        [Tooltip("The modal panel displaying Group 3 student credits and roles.")]
        [SerializeField] private GameObject creditsPanel;

        [Header("Audio (Optional)")]
        [Tooltip("AudioSource to play button click sound effects.")]
        [SerializeField] private AudioSource uiAudioSource;

        [Tooltip("AudioClip played whenever any menu button is clicked.")]
        [SerializeField] private AudioClip buttonClickSound;

        private void Awake()
        {
            // Coming back from gameplay: make sure the menu is usable (cursor free and visible, time running)
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            // Ensure default clean state: main buttons visible, modals closed
            ShowMainMenu();
        }

        #region Public Button Actions

        /// <summary>
        /// Loads the main game scene.
        /// </summary>
        public void PlayGame()
        {
            PlaySound();

            if (string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.LogWarning("[MainMenuController] Gameplay scene name is not set!", this);
                return;
            }

            Debug.Log($"[MainMenuController] Loading gameplay scene: '{gameplaySceneName}'...");
            SceneManager.LoadScene(gameplaySceneName);
        }

        /// <summary>
        /// Opens the How to Play instructions modal.
        /// </summary>
        public void OpenHowToPlay()
        {
            PlaySound();
            if (howToPlayPanel != null) howToPlayPanel.SetActive(true);
            if (creditsPanel != null) creditsPanel.SetActive(false);
            if (mainButtonsPanel != null) mainButtonsPanel.SetActive(false);
        }

        /// <summary>
        /// Opens the Group 3 team credits modal.
        /// </summary>
        public void OpenCredits()
        {
            PlaySound();
            if (creditsPanel != null) creditsPanel.SetActive(true);
            if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
            if (mainButtonsPanel != null) mainButtonsPanel.SetActive(false);
        }

        /// <summary>
        /// Closes any open modal panel and returns to the primary menu buttons.
        /// </summary>
        public void ShowMainMenu()
        {
            PlaySound();
            if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
            if (creditsPanel != null) creditsPanel.SetActive(false);
            if (mainButtonsPanel != null) mainButtonsPanel.SetActive(true);
        }

        /// <summary>
        /// Exits the game application (or stops Play Mode if running inside Unity Editor).
        /// </summary>
        public void QuitGame()
        {
            PlaySound();
            Debug.Log("[MainMenuController] Quitting application...");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        #endregion

        #region Helpers

        private void PlaySound()
        {
            if (uiAudioSource != null && buttonClickSound != null)
            {
                uiAudioSource.PlayOneShot(buttonClickSound);
            }
        }

        #endregion
    }
}
