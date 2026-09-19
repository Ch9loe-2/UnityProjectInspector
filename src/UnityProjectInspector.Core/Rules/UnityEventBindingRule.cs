using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a specific UnityEvent binding exists:
///   Source GameObject → Target Class.MethodName
///
/// Uses pre-parsed UnityEventBindingInfo — does NOT re-parse UnityEvent YAML.
/// Matches targets by class name (from TargetAssemblyTypeName), NOT by GameObject name,
/// because the script class often lives on a differently-named GameObject.
/// When CodeLinks are available, also cross-validates via UnityEventCodeLink.ClassName.
///
/// Input:
///   sourceGameObjectName — The GameObject that owns the UnityEvent
///   targetMethodName — The method name in the binding
///   targetClassName — (optional) The script class name (e.g. "PanelSwitcher", "GameManager")
/// </summary>
public class UnityEventBindingRule : IRule
{
    public string Id => "UNITYEVENT_BINDING";
    public string Name => "UnityEvent Binding Exists";

    public string SourceGameObjectName { get; }
    public string TargetMethodName { get; }

    /// <summary>
    /// The expected target script class name (e.g. "PanelSwitcher", "GameManager").
    /// This is matched against UnityEventBindingInfo.TargetAssemblyTypeName
    /// and (when available) UnityEventCodeLink.ClassName.
    /// </summary>
    public string? TargetClassName { get; }

    public UnityEventBindingRule(
        string sourceGameObjectName,
        string targetMethodName,
        string? targetClassName = null)
    {
        SourceGameObjectName = sourceGameObjectName;
        TargetMethodName = targetMethodName;
        TargetClassName = targetClassName;
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

        // If target class name is specified, verify it matches the binding
        if (TargetClassName != null)
        {
            var matchResult = CheckTargetClassName(methodMatch, context, sourceFileId);

            if (matchResult != null)
            {
                return matchResult;
            }
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"UnityEvent binding '{SourceGameObjectName}' → '{TargetMethodName}' exists" +
                      (TargetClassName != null ? $" for class '{TargetClassName}'" : ""),
        };
    }

    /// <summary>
    /// Checks whether the binding's target class matches the expected class name.
    /// Returns a RuleResult if validation fails, or null if validation passes.
    /// </summary>
    private RuleResult? CheckTargetClassName(
        UnityEventBindingInfo binding,
        InspectionContext context,
        long sourceFileId)
    {
        // Priority 1: Cross-validate against CodeLinks if available.
        // CodeLinks have the most reliable class name resolution.
        var matchingLinks = context.CodeLinks
            .Where(l =>
                l.SourceGameObjectFileId == sourceFileId &&
                string.Equals(l.MethodName, TargetMethodName, StringComparison.Ordinal))
            .ToList();

        if (matchingLinks.Count > 0)
        {
            // Use CodeLink's resolved class name
            var link = matchingLinks[0];

            if (link.Status == LinkStatus.Unresolved)
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.NotEvaluated,
                    Severity = RuleSeverity.Warning,
                    Message = $"UnityEvent binding '{SourceGameObjectName}' → '{TargetMethodName}' — " +
                              $"code evidence chain is broken ({link.StatusMessage})",
                };
            }

            if (!string.Equals(link.ClassName, TargetClassName, StringComparison.Ordinal))
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.Failed,
                    Severity = RuleSeverity.Error,
                    Message = $"UnityEvent binding on '{SourceGameObjectName}' targets class " +
                              $"'{link.ClassName}', but rule expected '{TargetClassName}'",
                };
            }

            // CodeLink validates the class name — pass
            return null;
        }

        // Priority 2: Use TargetAssemblyTypeName from the binding itself.
        // Format: "ClassName, Assembly-CSharp" or just "ClassName".
        var assemblyTypeName = binding.TargetAssemblyTypeName;

        if (string.IsNullOrWhiteSpace(assemblyTypeName))
        {
            // No assembly type name available and no CodeLink — cannot verify class
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Passed,
                Severity = RuleSeverity.Info,
                Message = $"UnityEvent binding '{SourceGameObjectName}' → '{TargetMethodName}' exists " +
                          $"(cannot verify class name — no assembly type info in binding)",
            };
        }

        // Extract class name from "ClassName, Assembly-CSharp"
        var actualClassName = ExtractClassName(assemblyTypeName);

        if (!string.Equals(actualClassName, TargetClassName, StringComparison.Ordinal))
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"UnityEvent binding on '{SourceGameObjectName}' targets class " +
                          $"'{actualClassName}', but rule expected '{TargetClassName}'",
            };
        }

        return null; // Class name matches
    }

    /// <summary>
    /// Extracts the class name from TargetAssemblyTypeName.
    /// "PanelSwitcher, Assembly-CSharp" → "PanelSwitcher"
    /// "GameManager" → "GameManager"
    /// </summary>
    private static string ExtractClassName(string? assemblyTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyTypeName))
            return string.Empty;

        var commaIndex = assemblyTypeName.IndexOf(',');
        return commaIndex > 0
            ? assemblyTypeName[..commaIndex].Trim()
            : assemblyTypeName.Trim();
    }
}