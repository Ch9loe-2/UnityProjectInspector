using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fixture helper script for GenericUnityRuntimeHarness test project.
///
/// This is NOT part of the Generic Harness core (GenericRuntimeBridge).
/// It is fixture infrastructure: a serializable proxy for SceneManager.LoadScene
/// that persists correctly when wired to a Button's onClick UnityEvent in the
/// scene editor.
///
/// The Generic Harness (GenericRuntimeBridge) does NOT contain or reference
/// any scene-switching logic. Scene switching is the fixture's responsibility.
/// </summary>
public class ButtonSceneLoader : MonoBehaviour
{
    [SerializeField]
    private string _targetSceneName = "TargetScene";

    /// <summary>
    /// Public method callable from Button.onClick persistent UnityEvent.
    /// Loads the target scene (single mode).
    /// </summary>
    public void LoadTargetScene()
    {
        Debug.Log($"[ButtonSceneLoader] Loading scene: '{_targetSceneName}'");
        SceneManager.LoadScene(_targetSceneName);
    }
}