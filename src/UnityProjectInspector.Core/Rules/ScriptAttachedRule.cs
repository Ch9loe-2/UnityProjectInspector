using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a specific GameObject has a MonoBehaviour component
/// that references a specific C# script class.
///
/// Uses pre-parsed ComponentInfo.ScriptGuid from SceneParser, cross-referenced
/// with InspectionContext.GameObjectScriptNames (populated via ScriptResolver).
///
/// Does NOT re-parse YAML, C#, or .meta files.
///
/// Distinguishes script class name from GameObject name:
///   GameObject = "MonitoringCanvas", script class = "PanelSwitcher" → uses class name only
/// </summary>
public class ScriptAttachedRule : IRule
{
    public string Id => "SCRIPT_ATTACHED";
    public string Name => "Script Attached";

    /// <summary>The GameObject name to check.</summary>
    public string GameObjectName { get; }

    /// <summary>The expected script class name (e.g. "PanelSwitcher", "DeviceController").</summary>
    public string ExpectedClassName { get; }

    /// <summary>Optional scene name to scope the search.</summary>
    public string? SceneName { get; }

    public ScriptAttachedRule(string gameObjectName, string expectedClassName, string? sceneName = null)
    {
        GameObjectName = gameObjectName;
        ExpectedClassName = expectedClassName;
        SceneName = sceneName;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        // Find the target GameObject
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
                    Message = $"Cannot check script — scene '{SceneName}' not found",
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
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{GameObjectName}' not found",
            };
        }

        // Priority 1: Use GameObjectScriptNames (populated via ScriptResolver if available)
        if (context.GameObjectScriptNames.TryGetValue(targetGo.FileId, out var scriptNames))
        {
            var match = scriptNames
                .FirstOrDefault(sn => string.Equals(sn, ExpectedClassName, StringComparison.Ordinal));

            if (match != null)
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.Passed,
                    Severity = RuleSeverity.Info,
                    Message = $"GameObject '{GameObjectName}' has script '{ExpectedClassName}' attached",
                };
            }

            // GameObject found but no matching script name
            var allScripts = string.Join(", ", scriptNames.Select(s => $"'{s}'"));
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"GameObject '{GameObjectName}' has scripts {allScripts}, expected '{ExpectedClassName}'",
            };
        }

        // Priority 2: Fallback — check component types directly for "MonoBehaviour"
        // This is a less reliable path: if no ScriptMap was populated, we can only
        // confirm that SOME MonoBehaviour exists, not which one.
        var hasMonoBehaviour = targetGo.Components
            .Any(c => string.Equals(c.Type, "MonoBehaviour", StringComparison.OrdinalIgnoreCase));

        if (hasMonoBehaviour)
        {
            // Has MonoBehaviour but can't verify which script without ScriptMap
            // Check if any Component has a ScriptGuid to get the best guess
            var scriptGuids = targetGo.Components
                .Where(c => c.ScriptGuid != null)
                .Select(c => c.ScriptGuid)
                .Distinct()
                .ToList();

            if (scriptGuids.Count > 0)
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.NotEvaluated,
                    Severity = RuleSeverity.Warning,
                    Message = $"GameObject '{GameObjectName}' has {scriptGuids.Count} MonoBehaviour(s) but ScriptMap is not available in context. Cannot verify script class '{ExpectedClassName}'",
                };
            }

            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.NotEvaluated,
                Severity = RuleSeverity.Warning,
                Message = $"GameObject '{GameObjectName}' has unnamed MonoBehaviour(s) — cannot verify script class '{ExpectedClassName}'",
            };
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Failed,
            Severity = RuleSeverity.Error,
            Message = $"GameObject '{GameObjectName}' has no MonoBehaviour components",
        };
    }
}