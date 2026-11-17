using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using EscapeTheFlood.Player;
using EscapeTheFlood.Water;

namespace EscapeTheFlood.GameCore
{
    public enum GameState { Playing, Paused, GameOver, Victory }

    /// <summary>
    /// GameManager: owns the game state (Playing / Paused / GameOver / Victory),
    /// the elapsed timer, and the Pause / Game Over / Victory panels.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "GameMenu";

        [Header("References (auto-found if left empty)")]
        [SerializeField] private PlayerMovement player;
        [SerializeField] private WinTrigger winTrigger;

        [Header("UI Panels")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private GameObject victoryPanel;

        [Header("UI Text (optional)")]
        [SerializeField] private Text timerText;
        [SerializeField] private Text gameOverReasonText;
        [SerializeField] private Text victoryTimeText;

        public GameState State { get; private set; } = GameState.Playing;
        public float ElapsedTime { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Time.timeScale = 1f;
        }

        private void Start()
        {
            if (player == null) player = FindFirstObjectByType<PlayerMovement>();
            if (winTrigger == null) winTrigger = FindFirstObjectByType<WinTrigger>();

            if (player != null) player.OnPlayerDied += HandlePlayerDied;
            else Debug.LogWarning("[GameManager] No PlayerMovement found in the scene.", this);

            SetPanel(pausePanel, false);
            SetPanel(gameOverPanel, false);
            SetPanel(victoryPanel, false);

            State = GameState.Playing;
            ElapsedTime = 0f;
        }

        private void OnDestroy()
        {
            if (player != null) player.OnPlayerDied -= HandlePlayerDied;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (State == GameState.Playing)
            {
                ElapsedTime += Time.deltaTime;
                if (timerText != null) timerText.text = FormatTime(ElapsedTime);

                // Chhunhour's WinTrigger pauses the game itself; we just notice it and switch state.
                if (winTrigger != null && winTrigger.HasWon) TriggerVictory();
            }

            if (PausePressed())
            {
                if (State == GameState.Playing) PauseGame();
                else if (State == GameState.Paused) ResumeGame();
            }
        }

        // ---------- State changes (also used by UI buttons) ----------

        public void PauseGame()
        {
            if (State != GameState.Playing) return;
            State = GameState.Paused;
            Time.timeScale = 0f;
            SetPanel(pausePanel, true);
            ShowCursor(true);
        }

        public void ResumeGame()
        {
            if (State != GameState.Paused) return;
            State = GameState.Playing;
            Time.timeScale = 1f;
            SetPanel(pausePanel, false);
        }

        public void TriggerGameOver(string reason)
        {
            if (State == GameState.GameOver || State == GameState.Victory) return;
            State = GameState.GameOver;
            EndGame();
            SetPanel(gameOverPanel, true);
            if (gameOverReasonText != null) gameOverReasonText.text = reason;
        }

        public void TriggerVictory()
        {
            if (State == GameState.GameOver || State == GameState.Victory) return;
            State = GameState.Victory;
            EndGame();
            SetPanel(victoryPanel, true);
            if (victoryTimeText != null) victoryTimeText.text = "Escaped in " + FormatTime(ElapsedTime);
        }

        public void RestartLevel()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // Works in the Editor even if the scene is not in the Build Profile scene list.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);
#endif
        }

        public void LoadMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        public void QuitGame()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Helpers ----------

        private void HandlePlayerDied()
        {
            string reason = (player != null && player.Breath <= 0f)
                ? "You drowned!"
                : "The shark got you!";
            TriggerGameOver(reason);
        }

        private void EndGame()
        {
            Time.timeScale = 0f;
            SetPanel(pausePanel, false);
            if (WaterBody.Instance != null) WaterBody.Instance.IsRising = false;
            ShowCursor(true);
        }

        private static void SetPanel(GameObject panel, bool visible)
        {
            if (panel != null) panel.SetActive(visible);
        }

        private static void ShowCursor(bool visible)
        {
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }

        private static bool PausePressed()
        {
#if ENABLE_INPUT_SYSTEM
            bool key = Keyboard.current != null &&
                       (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame);
            bool pad = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            return key || pad;
#else
            return false;
#endif
        }

        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m:00}:{s:00}";
        }
    }
}