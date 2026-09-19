using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a GameObject with the specified name exists in the project.
///
/// Input: GameObject name (e.g. "StartButton", "Player")
/// Optionally scoped to a specific scene.
/// Passes when at least one GameObject with that name is found.
/// Fails when no GameObject matches.
/// </summary>
public class GameObjectExistsRule : IRule
{
    public string Id => "GAMEOBJECT_EXISTS";
    public string Name => "GameObject Exists";

    /// <summary>
    /// The GameObject name to search for.
    /// </summary>
    public string GameObjectName { get; }

    /// <summary>
    /// Optional scene name to scope the search.
    /// If null, searches all scenes.
    /// </summary>
    public string? SceneName { get; }

    public GameObjectExistsRule(string gameObjectName, string? sceneName = null)
    {
        GameObjectName = gameObjectName;
        SceneName = sceneName;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        // If scoped to a scene, verify the scene exists first
        if (SceneName != null)
        {
            var scene = context.FindScene(SceneName);
            if (scene == null)
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.NotEvaluated,
                    Severity = RuleSeverity.Warning,
                    Message = $"Cannot check for GameObject '{GameObjectName}' — scene '{SceneName}' not found",
                };
            }

            var go = context.FindGameObjectInScene(SceneName, GameObjectName);
            if (go == null)
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.Failed,
                    Severity = RuleSeverity.Error,
                    Message = $"GameObject '{GameObjectName}' not found in scene '{SceneName}'",
                };
            }

            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Passed,
                Severity = RuleSeverity.Info,
                Message = $"GameObject '{GameObjectName}' exists in scene '{SceneName}'",
            };
        }

        // Search all scenes
        var matches = context.FindGameObjectsByName(GameObjectName).ToList();

        if (matches.Count == 0)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{GameObjectName}' not found in any scene",
            };
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"GameObject '{GameObjectName}' found ({matches.Count} match{(matches.Count > 1 ? "es" : "")})",
        };
    }
}