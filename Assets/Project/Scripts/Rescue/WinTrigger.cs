using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// WinTrigger: Handles the victory condition when the player reaches the rooftop finish / rescue zone.
/// Activates the victory UI panel, unlocks the mouse cursor for UI navigation, pauses the gameplay
/// simulation, and provides scene reload / restart capabilities.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class WinTrigger : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("The Victory Panel UI GameObject to enable upon reaching the rooftop.")]
    public GameObject victoryPanel;

    [Header("Audio & Polish (Optional)")]
    [Tooltip("Optional sound effect to play when the victory condition is triggered.")]
    [SerializeField] private AudioClip victorySound;

    [Tooltip("Optional AudioSource to play victory sound. If unassigned, AudioSource.PlayClipAtPoint will be used.")]
    [SerializeField] private AudioSource audioSource;

    [Header("State (View Only)")]
    [Tooltip("Tracks whether the win condition has already been triggered.")]
    [SerializeField] private bool hasWon = false;

    /// <summary>
    /// Public getter indicating if the player has triggered the win condition.
    /// </summary>
    public bool HasWon => hasWon;

    private void Awake()
    {
        // Ensure the attached collider is configured as a trigger so OnTriggerEnter works
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[WinTrigger] Collider on '{gameObject.name}' is not marked as 'Is Trigger'. Automatically enabling isTrigger = true.", this);
            col.isTrigger = true;
        }
    }

    private void Start()
    {
        // 3. Automatically hide the victory UI screen at the start of the game
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[WinTrigger] 'victoryPanel' is not assigned in the Inspector! Please assign your victory UI panel.", this);
        }
    }

    /// <summary>
    /// 1. Detects when an object enters this trigger volume.
    /// </summary>
    /// <param name="other">The collider that entered the trigger.</param>
    private void OnTriggerEnter(Collider other)
    {
        // 2. Prevent triggering multiple times
        if (hasWon) return;

        // Verify if the entering object is tagged "Player"
        if (other.CompareTag("Player") || (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player")))
        {
            hasWon = true;
            TriggerWin();
        }
    }

    /// <summary>
    /// Executes the win sequence: shows victory UI, unlocks cursor, and freezes gameplay.
    /// </summary>
    private void TriggerWin()
    {
        Debug.Log("[WinTrigger] Player reached the rooftop finish zone! Victory triggered.");

        // 3. Enable the victory panel UI screen
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }

        // 4. Unlock and show the mouse cursor so the player can click UI buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Play optional victory audio
        PlayVictoryAudio();

        // 5. Pause the game so rising water, enemies, and physics freeze
        Time.timeScale = 0f;
    }

    /// <summary>
    /// Plays the optional victory audio clip.
    /// </summary>
    private void PlayVictoryAudio()
    {
        if (victorySound == null) return;

        if (audioSource != null)
        {
            audioSource.PlayOneShot(victorySound);
        }
        else
        {
            AudioSource.PlayClipAtPoint(victorySound, transform.position);
        }
    }

    /// <summary>
    /// 6. Resets Time.timeScale to 1f and reloads the current active scene using SceneManager.
    /// Connect this method to the 'Restart' button's OnClick event in your Victory Panel.
    /// </summary>
    public void RestartGame()
    {
        // Always reset timeScale before loading so the next scene runs at normal speed
        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(currentScene.buildIndex);
        }
        else
        {
            SceneManager.LoadScene(currentScene.name);
        }
    }

    /// <summary>
    /// Resets Time.timeScale to 1f and loads the main menu scene.
    /// Connect this method to a 'Main Menu' button's OnClick event.
    /// </summary>
    /// <param name="menuSceneName">Name of the menu scene to load (default: "GameMenu").</param>
    public void LoadMainMenu(string menuSceneName = "GameMenu")
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(menuSceneName);
    }

    /// <summary>
    /// Quits the application or exits Play Mode in the Unity Editor.
    /// Connect this method to a 'Quit' button's OnClick event.
    /// </summary>
    public void QuitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Visualizes the finish zone volume with a translucent green highlight in the Scene view.
    /// </summary>
    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.1f, 1f, 0.2f, 0.35f);
        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
    }
#endif
}
