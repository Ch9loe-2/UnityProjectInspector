using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Setup script for M14 Runtime Action Fixture.
/// Run once via Unity Editor or -executeMethod to initialize scenes and build settings.
///
/// Creates:
///   - Assets/Scenes/MainMenu.unity with Canvas + Button + SceneSwitcher
///   - Assets/Scenes/TargetScene.unity (empty target)
///   - EditorBuildSettings with both scenes
/// </summary>
public static class M14_Setup
{
    /// <summary>
    /// Main entry point. Creates all scenes and configures build settings.
    /// </summary>
    [MenuItem("M14 Setup/Initialize Scenes")]
    public static void InitializeScenes()
    {
        CreateTargetScene();
        CreateMainMenuScene();
        SetupEditorBuildSettings();
        Debug.Log("[M14_Setup] Complete. Scenes and build settings ready.");
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

        // A simple text label to identify target scene
        var canvasGo = new GameObject("TargetCanvas");
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
        Debug.Log($"[M14_Setup] Created {scenePath}");
    }

    private static void CreateMainMenuScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MainMenu";

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

        // SceneSwitcher (script on a dedicated GameObject)
        var switcherGo = new GameObject("SceneSwitcher");
        switcherGo.AddComponent<SceneSwitcher>();

        // M14Bridge (script on a dedicated GameObject)
        var bridgeGo = new GameObject("M14Bridge");
        bridgeGo.AddComponent<M14RuntimeBridge>();

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

        // MainMenuButton
        var buttonGo = new GameObject("MainMenuButton");
        buttonGo.transform.SetParent(canvasGo.transform, false);
        buttonGo.AddComponent<Image>();

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = buttonGo.GetComponent<Image>();
        button.transition = Selectable.Transition.ColorTint;

        // Button label
        var buttonTextGo = new GameObject("Text");
        buttonTextGo.transform.SetParent(buttonGo.transform, false);
        var buttonText = buttonTextGo.AddComponent<Text>();
        buttonText.text = "Load TargetScene";
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

        // Wire button onClick → SceneSwitcher.SwitchToTargetScene
        var switcher = switcherGo.GetComponent<SceneSwitcher>();
        button.onClick.AddListener(switcher.SwitchToTargetScene);

        var scenePath = "Assets/Scenes/MainMenu.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"[M14_Setup] Created {scenePath}");
    }

    private static void SetupEditorBuildSettings()
    {
        var guids = AssetDatabase.FindAssets("t:Scene");
        var scenes = new EditorBuildSettingsScene[0];

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("MainMenu.unity") || path.EndsWith("TargetScene.unity"))
            {
                System.Array.Resize(ref scenes, scenes.Length + 1);
                scenes[scenes.Length - 1] = new EditorBuildSettingsScene(path, true);
            }
        }

        EditorBuildSettings.scenes = scenes;
        Debug.Log($"[M14_Setup] EditorBuildSettings updated with {scenes.Length} scenes.");
    }
}