using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a GameObject is a direct child of a specific parent GameObject.
///
/// Uses existing GameObjectInfo.ParentFileId and ParentFileId lookup from SceneParser.
/// Does NOT re-parse Unity YAML.
///
/// Limitation: When multiple GameObjects share the same name, this rule matches
/// the first one found (via FirstOrDefault). If a specific child or parent is
/// ambiguous, the result may be misleading. In practice, Unity UI hierarchies
/// (e.g. Btn_DeviceList under MonitoringCanvas) are usually unique.
/// </summary>
public class GameObjectHierarchyRule : IRule
{
    public string Id => "GAMEOBJECT_HIERARCHY";
    public string Name => "GameObject Hierarchy";

    /// <summary>The child GameObject name to check.</summary>
    public string ChildName { get; }

    /// <summary>The expected parent GameObject name.</summary>
    public string ExpectedParentName { get; }

    /// <summary>Optional scene name to scope the search. If null, searches all scenes.</summary>
    public string? SceneName { get; }

    public GameObjectHierarchyRule(string childName, string expectedParentName, string? sceneName = null)
    {
        ChildName = childName;
        ExpectedParentName = expectedParentName;
        SceneName = sceneName;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        // Find the child GameObject
        GameObjectInfo? child = null;

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
                    Message = $"Cannot check hierarchy — scene '{SceneName}' not found",
                };
            }

            child = context.FindGameObjectInScene(SceneName, ChildName);
        }
        else
        {
            child = context.FindGameObjectsByName(ChildName).FirstOrDefault();
        }

        if (child == null)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Child GameObject '{ChildName}' not found in any scene",
            };
        }

        // If child has no parent (ParentFileId == 0), fail immediately
        if (child.ParentFileId == 0)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{ChildName}' is a root object (no parent) — expected parent '{ExpectedParentName}'",
            };
        }

        // Find the parent GameObject by ParentFileId
        // Build a parent lookup from the same scene(s)
        var parentGo = FindParentByName(context, child.ParentFileId);

        if (parentGo == null)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{ChildName}' has a parent (fileId={child.ParentFileId}) but no GameObject with name '{ExpectedParentName}' was found at that id",
            };
        }

        if (!string.Equals(parentGo.Name, ExpectedParentName, StringComparison.Ordinal))
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{ChildName}' parent is '{parentGo.Name}', expected '{ExpectedParentName}'",
            };
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"GameObject '{ChildName}' is a child of '{ExpectedParentName}'",
        };
    }

    /// <summary>
    /// Finds a GameObject by its fileId across all scenes in the context.
    /// </summary>
    private static GameObjectInfo? FindParentByName(InspectionContext context, long parentFileId)
    {
        foreach (var scene in context.ProjectInfo.Scenes)
        {
            foreach (var go in scene.GameObjects)
            {
                if (go.FileId == parentFileId)
                {
                    return go;
                }
            }
        }

        return null;
    }
}