namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// Provides a single entry point for all data that rules can inspect.
///
/// Rule Engine must ONLY access data through this context.
/// Rules must NOT directly instantiate parsers or analyzers.
/// </summary>
public class InspectionContext
{
    /// <summary>
    /// The parsed Unity project info, containing all scenes and their GameObjects.
    /// </summary>
    public required UnityProjectInfo ProjectInfo { get; init; }

    /// <summary>
    /// UnityEvent persistent call bindings parsed from scene files.
    /// Maps source components to target methods.
    /// </summary>
    public List<UnityEventBindingInfo> EventBindings { get; init; } = new();

    /// <summary>
    /// Full static evidence chains linking UnityEvent bindings
    /// through C# source analysis to actual method invocations.
    /// </summary>
    public List<UnityEventCodeLink> CodeLinks { get; init; } = new();

    // ─── Convenience helpers for rules ───

    /// <summary>
    /// Finds all GameObjects with the given name across all scenes.
    /// </summary>
    public IEnumerable<GameObjectInfo> FindGameObjectsByName(string name)
    {
        foreach (var scene in ProjectInfo.Scenes)
        {
            foreach (var go in scene.GameObjects)
            {
                if (string.Equals(go.Name, name, StringComparison.Ordinal))
                {
                    yield return go;
                }
            }
        }
    }

    /// <summary>
    /// Finds a scene by name (case-sensitive, ordinal).
    /// </summary>
    public SceneInfo? FindScene(string name)
    {
        return ProjectInfo.Scenes
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds a GameObject by name in a specific scene.
    /// Returns null if the scene or GameObject is not found.
    /// </summary>
    public GameObjectInfo? FindGameObjectInScene(string sceneName, string gameObjectName)
    {
        var scene = FindScene(sceneName);
        if (scene == null) return null;

        return scene.GameObjects
            .FirstOrDefault(go => string.Equals(go.Name, gameObjectName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Resolves a GameObject name to its fileId (first match).
    /// Returns 0 if not found.
    /// </summary>
    public long GetFileId(string gameObjectName)
    {
        return FindGameObjectsByName(gameObjectName)
            .Select(go => go.FileId)
            .FirstOrDefault();
    }
}