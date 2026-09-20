using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple scene switcher for M14 Runtime Action Fixture.
/// Attached to a GameObject in MainMenu scene.
/// Called by MainMenuButton.onClick to load TargetScene.
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    [SerializeField]
    private string _targetSceneName = "TargetScene";

    /// <summary>
    /// Called by Button.onClick.
    /// Switches to the target scene (single mode).
    /// </summary>
    public void SwitchToTargetScene()
    {
        Debug.Log($"[SceneSwitcher] Loading scene: '{_targetSceneName}'");
        SceneManager.LoadScene(_targetSceneName);
    }

    /// <summary>
    /// Called by Button.onClick for back-navigation.
    /// </summary>
    public void SwitchBackToMainMenu()
    {
        Debug.Log($"[SceneSwitcher] Loading scene: 'MainMenu'");
        SceneManager.LoadScene("MainMenu");
    }
}