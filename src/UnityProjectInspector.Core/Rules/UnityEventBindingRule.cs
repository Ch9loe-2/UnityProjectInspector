using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a specific UnityEvent binding exists:
///   Source GameObject → Target GameObject.MethodName
///
/// Uses pre-parsed UnityEventBindingInfo — does NOT re-parse UnityEvent YAML.
///
/// Input:
///   sourceGameObjectName — The GameObject that owns the UnityEvent
///   targetMethodName — The method name in the binding
///   targetGameObjectName — (optional) The GameObject that contains the target method
/// </summary>
public class UnityEventBindingRule : IRule
{
    public string Id => "UNITYEVENT_BINDING";
    public string Name => "UnityEvent Binding Exists";

    public string SourceGameObjectName { get; }
    public string? TargetGameObjectName { get; }
    public string TargetMethodName { get; }

    public UnityEventBindingRule(
        string sourceGameObjectName,
        string targetMethodName,
        string? targetGameObjectName = null)
    {
        SourceGameObjectName = sourceGameObjectName;
        TargetMethodName = targetMethodName;
        TargetGameObjectName = targetGameObjectName;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        var sourceFileId = context.GetFileId(SourceGameObjectName);

        if (sourceFileId == 0)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.NotEvaluated,
                Severity = RuleSeverity.Warning,
                Message = $"Cannot check UnityEvent binding — source GameObject '{SourceGameObjectName}' not found",
            };
        }

        // Find matching bindings from this source GameObject
        var matchingBindings = context.EventBindings
            .Where(b => b.SourceGameObjectFileId == sourceFileId)
            .ToList();

        if (matchingBindings.Count == 0)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"No UnityEvent bindings found on '{SourceGameObjectName}'",
            };
        }

        // Check method name
        var methodMatch = matchingBindings
            .FirstOrDefault(b => string.Equals(b.MethodName, TargetMethodName, StringComparison.Ordinal));

        if (methodMatch == null)
        {
            var methods = string.Join(", ", matchingBindings.Select(b => $"'{b.MethodName}'"));
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"UnityEvent binding on '{SourceGameObjectName}' targets method '{TargetMethodName}' not found. Existing bindings: {methods}",
            };
        }

        // If target GameObject name is specified, verify it matches
        if (TargetGameObjectName != null)
        {
            var targetGoFileId = context.GetFileId(TargetGameObjectName);

            if (targetGoFileId == 0)
            {
                // Target GameObject doesn't exist in scene at all
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.Failed,
                    Severity = RuleSeverity.Error,
                    Message = $"UnityEvent binding on '{SourceGameObjectName}' targets method '{TargetMethodName}', but target GameObject '{TargetGameObjectName}' was not found in the project",
                };
            }

            if (methodMatch.TargetGameObjectFileId != targetGoFileId)
            {
                var actualTargetName = context.FindGameObjectsByName(TargetGameObjectName)
                    .Select(go => go.Name)
                    .FirstOrDefault() ?? $"fileId {methodMatch.TargetGameObjectFileId}";

                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.Failed,
                    Severity = RuleSeverity.Error,
                    Message = $"UnityEvent binding on '{SourceGameObjectName}' does not target '{TargetGameObjectName}'. Actual target fileId: {methodMatch.TargetGameObjectFileId}",
                };
            }
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"UnityEvent binding '{SourceGameObjectName}' → '{TargetMethodName}' exists" +
                      (TargetGameObjectName != null ? $" on '{TargetGameObjectName}'" : ""),
        };
    }
}