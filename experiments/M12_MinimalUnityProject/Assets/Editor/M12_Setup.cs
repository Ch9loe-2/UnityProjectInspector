using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// M12 setup: create MainScene, add RuntimeProbe GameObject, configure Build Settings.
///
/// Call via:
///   Unity -batchmode -projectPath <path> -executeMethod M12_Setup.CreateProject -quit
/// </summary>
public static class M12_Setup
{
    private const string SceneName = "MainScene";
    private const string ScenePath = "Assets/Scenes/MainScene.unity";
    private const string GameObjectName = "RuntimeProbe";
    private const string ScriptTypeName = "RuntimeProbe";

    public static void CreateProject()
    {
        Debug.Log("[M12_Setup] Starting M12 project setup...");

        // Validate Script exists
        var script = FindScript(ScriptTypeName);
        if (script == null)
        {
            Debug.LogError($"[M12_Setup] CRITICAL: Script '{ScriptTypeName}' not found! Aborting.");
            EditorApplication.Exit(1);
            return;
        }

        // Create the scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = SceneName;
        Debug.Log($"[M12_Setup] Created new scene: '{SceneName}'");

        // Create RuntimeProbe GameObject with the MonoBehaviour attached
        var go = new GameObject(GameObjectName);
        var scriptType = script.GetClass();
        if (scriptType == null)
        {
            Debug.LogError($"[M12_Setup] Could not resolve class from MonoScript '{script.name}'");
            EditorApplication.Exit(1);
            return;
        }
        go.AddComponent(scriptType);
        Debug.Log($"[M12_Setup] Created GameObject '{GameObjectName}' with {ScriptTypeName} component");

        // Save scene
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[M12_Setup] Scene saved to {ScenePath}");

        // Configure Build Settings - add MainScene as the only scene
        var scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorBuildSettings.scenes = scenes;
        Debug.Log($"[M12_Setup] Build Settings configured with 1 scene: {ScenePath}");

        Debug.Log("[M12_Setup] M12 project setup complete.");
    }

    /// <summary>
    /// Static validation check: verify critical project structure.
    /// Can be called before build to confirm everything is ready.
    /// </summary>
    public static void ValidateAndReport()
    {
        Debug.Log("[M12_Validate] Running M12 static validation...");

        bool allPass = true;

        // 1. Scene exists
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        bool sceneExists = scene != null;
        Debug.Log($"[M12_Validate] Scene exists: {sceneExists} ({ScenePath})");
        allPass &= sceneExists;

        // 2. Build Settings
        var buildScenes = EditorBuildSettings.scenes;
        bool buildSettingsOk = buildScenes != null && buildScenes.Length > 0 &&
                               buildScenes[0].enabled && buildScenes[0].path == ScenePath;
        Debug.Log($"[M12_Validate] Build Settings OK: {buildSettingsOk}");
        allPass &= buildSettingsOk;

        // 3. GameObject exists
        var go = GameObject.Find(GameObjectName);
        bool goExists = go != null;
        Debug.Log($"[M12_Validate] GameObject '{GameObjectName}' exists: {goExists}");
        allPass &= goExists;

        // 4. Script attached
        bool scriptAttached = goExists && go.GetComponent(ScriptTypeName) != null;
        Debug.Log($"[M12_Validate] Script '{ScriptTypeName}' attached: {scriptAttached}");
        allPass &= scriptAttached;

        // 5. Project structure
        var assetsOk = AssetDatabase.IsValidFolder("Assets/Scenes") &&
                       AssetDatabase.IsValidFolder("Assets/Scripts");
        Debug.Log($"[M12_Validate] Assets structure OK: {assetsOk}");
        allPass &= assetsOk;

        Debug.Log($"[M12_Validate] Static validation overall: {(allPass ? "PASS" : "FAIL")}");
    }

    private static MonoScript FindScript(string className)
    {
        var guids = AssetDatabase.FindAssets($"t:MonoScript {className}");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.name == className)
                return script;
        }
        return null;
    }
}