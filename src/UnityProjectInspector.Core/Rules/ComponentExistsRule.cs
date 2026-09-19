using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a GameObject has a specific component type attached.
///
/// Input: GameObject name + component type name (e.g. "StartButton" + "Button").
/// Uses the existing SceneParser's known type names (e.g. "Transform", "Button",
/// "Camera", "MonoBehaviour", etc.).
///
/// Limitation: Only component types recognized by SceneParser's KnownComponentTypes
/// can be checked. Unknown or unrecognized component types cannot be verified.
/// </summary>
public class ComponentExistsRule : IRule
{
    public string Id => "COMPONENT_EXISTS";
    public string Name => "Component Exists";

    public string GameObjectName { get; }
    public string ComponentType { get; }

    /// <summary>
    /// Optional scene name to scope the search.
    /// </summary>
    public string? SceneName { get; }

    public ComponentExistsRule(string gameObjectName, string componentType, string? sceneName = null)
    {
        GameObjectName = gameObjectName;
        ComponentType = componentType;
        SceneName = sceneName;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        // Find the GameObject
        GameObjectInfo? targetGo = null;

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
                    Message = $"Cannot check component on '{GameObjectName}' — scene '{SceneName}' not found",
                };
            }

            targetGo = context.FindGameObjectInScene(SceneName, GameObjectName);
        }
        else
        {
            targetGo = context.FindGameObjectsByName(GameObjectName).FirstOrDefault();
        }

        if (targetGo == null)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.NotEvaluated,
                Severity = RuleSeverity.Warning,
                Message = $"Cannot check component '{ComponentType}' — GameObject '{GameObjectName}' not found",
            };
        }

        var hasComponent = targetGo.Components
            .Any(c => string.Equals(c.Type, ComponentType, StringComparison.OrdinalIgnoreCase));

        if (!hasComponent)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{GameObjectName}' does not have component '{ComponentType}'",
            };
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"GameObject '{GameObjectName}' has component '{ComponentType}'",
        };
    }
}