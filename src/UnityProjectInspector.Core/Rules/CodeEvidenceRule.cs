using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks the complete static evidence chain from a UnityEvent binding
/// through C# source analysis to verify a specific method call exists
/// with statically determinable arguments.
///
/// This rule does NOT parse C# or UnityEvent YAML.
/// It only reads pre-computed UnityEventCodeLink data from Milestone 5.
///
/// Behavior by LinkStatus:
///   Resolved + static args matching → Passed
///   Resolved + args mismatch         → Failed
///   PartiallyResolved + call found   → NotEvaluated (cannot statically determine args)
///   Unresolved                        → Failed (evidence chain broken)
///   Method call not found in target   → Failed
/// </summary>
public class CodeEvidenceRule : IRule
{
    public string Id => "CODE_EVIDENCE";
    public string Name => "Code Evidence Chain";

    /// <summary>The source GameObject that fires the UnityEvent.</summary>
    public string SourceGameObjectName { get; }

    /// <summary>The method name on the target script (e.g. "Start2D").</summary>
    public string TargetMethodName { get; }

    /// <summary>
    /// The expected call target inside that method (e.g. "SceneManager.LoadScene").
    /// If null, only the method's existence is checked.
    /// </summary>
    public string? ExpectedCallTarget { get; }

    /// <summary>
    /// The expected argument value (without quotes, e.g. "Maze2D").
    /// The rule will compare against the source-code representation.
    /// Only checked when ExpectedCallTarget is also specified.
    /// </summary>
    public string? ExpectedArgument { get; }

    public CodeEvidenceRule(
        string sourceGameObjectName,
        string targetMethodName,
        string? expectedCallTarget = null,
        string? expectedArgument = null)
    {
        SourceGameObjectName = sourceGameObjectName;
        TargetMethodName = targetMethodName;
        ExpectedCallTarget = expectedCallTarget;
        ExpectedArgument = expectedArgument;
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
                Message = $"Cannot check code evidence — source GameObject '{SourceGameObjectName}' not found",
            };
        }

        // Find the code link for this source + method
        var link = context.CodeLinks
            .FirstOrDefault(l =>
                l.SourceGameObjectFileId == sourceFileId &&
                string.Equals(l.MethodName, TargetMethodName, StringComparison.Ordinal));

        if (link == null)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"No code evidence chain found: '{SourceGameObjectName}' → '{TargetMethodName}'",
            };
        }

        // Check resolution status
        if (link.Status == LinkStatus.Unresolved)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Code evidence chain is broken: {link.StatusMessage ?? "Could not resolve target script or method"}",
            };
        }

        // If no specific call target to check, just verify the method exists and is resolved
        if (ExpectedCallTarget == null)
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Passed,
                Severity = RuleSeverity.Info,
                Message = $"Method '{TargetMethodName}' in '{link.ClassName}' exists (status: {link.Status})",
            };
        }

        // Find the expected call inside the target method
        var matchingCall = link.Calls
            .FirstOrDefault(c => string.Equals(c.FullTarget, ExpectedCallTarget, StringComparison.Ordinal));

        if (matchingCall == null)
        {
            var actualCalls = link.Calls.Count > 0
                ? string.Join(", ", link.Calls.Select(c => $"'{c.FullTarget}'"))
                : "(no calls found)";

            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Expected call '{ExpectedCallTarget}' not found in method '{TargetMethodName}'. Found calls: {actualCalls}",
            };
        }

        // Call target found — check argument
        if (ExpectedArgument != null)
        {
            if (!matchingCall.AreAllArgumentsStatic)
            {
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.NotEvaluated,
                    Severity = RuleSeverity.Warning,
                    Message = $"Call '{ExpectedCallTarget}' found in '{TargetMethodName}', but arguments cannot be statically determined (runtime variables). Cannot verify scene name '{ExpectedArgument}'",
                };
            }

            // Check if any argument matches the expected value (in source-code representation)
            var expectedArgInSource = $"\"{ExpectedArgument}\"";
            var argMatch = matchingCall.Arguments
                .Any(a => string.Equals(a, expectedArgInSource, StringComparison.Ordinal));

            if (!argMatch)
            {
                var args = string.Join(", ", matchingCall.Arguments);
                return new RuleResult
                {
                    RuleId = Id,
                    RuleName = Name,
                    Status = RuleStatus.Failed,
                    Severity = RuleSeverity.Error,
                    Message = $"Call '{ExpectedCallTarget}' found in '{TargetMethodName}' but argument is '{args}', expected '{expectedArgInSource}'",
                };
            }
        }

        // All checks passed
        var argDetails = ExpectedArgument != null
            ? $" → {ExpectedCallTarget}(\"{ExpectedArgument}\")"
            : $" → {ExpectedCallTarget}";

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"Complete evidence chain verified: '{SourceGameObjectName}' → '{TargetMethodName}'{argDetails}",
        };
    }
}