using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// One-time bootstrap for the GenericUnityRuntimeHarness Unity project.
///
/// Creates:
///   - Assets/Scenes/MainScene.unity (Canvas + pre-wired StartButton)
///   - Assets/Scenes/TargetScene.unity (empty target scene)
///   - EditorBuildSettings with both scenes
///
/// Button.onClick is pre-wired in the scene editor (not by GenericRuntimeBridge).
/// The GenericRuntimeBridge only handles IPC, commands, and evidence — NOT scene logic.
///
/// Run once via Unity CLI:
///   Unity -projectPath ... -executeMethod SetupHarnessProject.Initialize -quit -batchmode -noGraphics
///
/// After this runs, commit the .unity + .meta files. This script can be deleted
/// or kept as documentation — it is NOT a core part of the Generic Harness.
/// </summary>
public static class SetupHarnessProject
{
    [MenuItem("Harness Setup/Initialize Scenes")]
    public static void Initialize()
    {
        CreateTargetScene();
        CreateMainScene();
        SetupEditorBuildSettings();
        Debug.Log("[SetupHarnessProject] Complete. Scenes and build settings ready.");
    }

    private static void CreateTargetScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "TargetScene";

        // Camera
        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<Camera>();
        cameraGo.AddComponent<AudioListener>();

        // Light
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;

        // Target label canvas
        var canvasGo = new GameObject("Canvas");
        canvasGo.AddComponent<Canvas>();
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var textGo = new GameObject("TargetLabel");
        textGo.transform.SetParent(canvasGo.transform, false);
        var text = textGo.AddComponent<Text>();
        text.text = "Target Scene";
        text.fontSize = 48;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        var rectTransform = textGo.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        var scenePath = "Assets/Scenes/TargetScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"[SetupHarnessProject] Created {scenePath}");
    }

    private static void CreateMainScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MainScene";

        // Camera
        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<Camera>();
        cameraGo.AddComponent<AudioListener>();

        // Light
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;

        // EventSystem (required for UI Button interaction)
        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<StandaloneInputModule>();

        // Canvas
        var canvasGo = new GameObject("Canvas");
        canvasGo.AddComponent<Canvas>();
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        // StartButton
        var buttonGo = new GameObject("StartButton");
        buttonGo.transform.SetParent(canvasGo.transform, false);
        buttonGo.AddComponent<Image>();

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = buttonGo.GetComponent<Image>();
        button.transition = Selectable.Transition.ColorTint;

        // Button label
        var buttonTextGo = new GameObject("Text");
        buttonTextGo.transform.SetParent(buttonGo.transform, false);
        var buttonText = buttonTextGo.AddComponent<Text>();
        buttonText.text = "Start";
        buttonText.fontSize = 24;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.black;

        var buttonTextRect = buttonTextGo.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        // Button rect
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(300, 80);
        buttonRect.anchoredPosition = Vector2.zero;

        // Create ButtonSceneLoader GameObject for persistent onClick binding
        var loaderGo = new GameObject("ButtonSceneLoader");
        var loader = loaderGo.AddComponent<ButtonSceneLoader>();

        // Wire button onClick via UnityEventTools (persistent across serialization)
        // Lambda-style listeners are NOT serialized — UnityEventTools ensures
        // the listener survives Player build.
        UnityEventTools.AddPersistentListener(button.onClick, loader.LoadTargetScene);

        var scenePath = "Assets/Scenes/MainScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"[SetupHarnessProject] Created {scenePath}");
    }

    private static void SetupEditorBuildSettings()
    {
        var guids = AssetDatabase.FindAssets("t:Scene");
        var scenes = new EditorBuildSettingsScene[0];

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("MainScene.unity") || path.EndsWith("TargetScene.unity"))
            {
                System.Array.Resize(ref scenes, scenes.Length + 1);
                scenes[scenes.Length - 1] = new EditorBuildSettingsScene(path, true);
            }
        }

        EditorBuildSettings.scenes = scenes;
        Debug.Log($"[SetupHarnessProject] EditorBuildSettings updated with {scenes.Length} scenes.");
    }
}