using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// Validates an AssignmentDefinition for structural correctness.
///
/// Checks performed:
///   Assignment.Id not null/empty
///   Assignment.Name not null/empty
///   Assignment.Requirements not null
///   No duplicate Requirement.Id
///   Each Requirement.Id not null/empty
///   Each Requirement.Name not null/empty
///   RuntimeRequired + RuntimeTest=null → Error
///   StaticOnly + RuntimeTest!=null → Warning (not an error)
///   Each StaticRules entry is creatable by RuleFactory
/// </summary>
public class InspectionWorkflowValidator
{
    private readonly List<WorkflowIssue> _issues = new();

    /// <summary>
    /// Validates the assignment and returns any issues found.
    /// </summary>
    public IReadOnlyList<WorkflowIssue> Validate(AssignmentDefinition assignment)
    {
        _issues.Clear();

        if (assignment == null)
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                "assignment",
                "Assignment definition is null"));
            return _issues;
        }

        // ─── Assignment level ────────────────────────────────
        if (string.IsNullOrWhiteSpace(assignment.Id))
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                "assignment.id",
                "Assignment Id must not be null or empty"));
        }

        if (string.IsNullOrWhiteSpace(assignment.Name))
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                "assignment.name",
                "Assignment Name must not be null or empty"));
        }

        if (assignment.Requirements == null)
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                "assignment.requirements",
                "Assignment Requirements list must not be null"));
            return _issues;
        }

        // ─── Duplicate Requirement IDs ────────────────────────
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < assignment.Requirements.Count; i++)
        {
            var req = assignment.Requirements[i];
            if (req == null)
            {
                _issues.Add(new WorkflowIssue(
                    WorkflowIssueSeverity.Error,
                    $"assignment.requirements[{i}]",
                    "Requirement entry is null"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(req.Id))
            {
                _issues.Add(new WorkflowIssue(
                    WorkflowIssueSeverity.Error,
                    $"assignment.requirements[{i}].id",
                    "Requirement Id must not be null or empty"));
            }
            else if (!seenIds.Add(req.Id))
            {
                _issues.Add(new WorkflowIssue(
                    WorkflowIssueSeverity.Error,
                    $"assignment.requirements[{i}].id",
                    $"Duplicate requirement Id: '{req.Id}'"));
            }
        }

        // ─── Per-requirement validation ────────────────────────
        for (int i = 0; i < assignment.Requirements.Count; i++)
        {
            var req = assignment.Requirements[i];
            if (req == null) continue;

            ValidateRequirement(req, i);
        }

        return _issues;
    }

    private void ValidateRequirement(RequirementDefinition req, int index)
    {
        var prefix = $"assignment.requirements[{index}]";

        // Name
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                $"{prefix}.name",
                $"Requirement '{req.Id ?? "(no id)"}' Name must not be null or empty"));
        }

        // EvidenceRequirement
        var evidenceReq = ParseEvidenceRequirement(req.EvidenceRequirement);

        // RuntimeRequired + no RuntimeTest = Error
        if (evidenceReq == Models.Rules.EvidenceRequirement.RuntimeRequired && req.RuntimeTest == null)
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                $"{prefix}.runtimeTest",
                $"Requirement '{req.Id}' declares RuntimeRequired but has no RuntimeTest. " +
                "This configuration is invalid — runtime cannot be performed."));
        }

        // RuntimeRequired + RuntimeTest has no actions → Error
        if (evidenceReq == Models.Rules.EvidenceRequirement.RuntimeRequired
            && req.RuntimeTest != null
            && (req.RuntimeTest.Actions == null || req.RuntimeTest.Actions.Count == 0))
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Error,
                $"{prefix}.runtimeTest.actions",
                $"Requirement '{req.Id}' declares RuntimeRequired but its RuntimeTest has no actions. " +
                "At least one action is needed to produce evidence for assertions."));
        }

        // StaticOnly + RuntimeTest != null = Warning
        if (evidenceReq == Models.Rules.EvidenceRequirement.StaticOnly && req.RuntimeTest != null)
        {
            _issues.Add(new WorkflowIssue(
                WorkflowIssueSeverity.Warning,
                $"{prefix}.runtimeTest",
                $"Requirement '{req.Id}' declares StaticOnly but contains a RuntimeTest. " +
                "The RuntimeTest will not be executed."));
        }

        // Validate each static rule can be created by RuleFactory
        if (req.StaticRules != null)
        {
            for (int j = 0; j < req.StaticRules.Count; j++)
            {
                var ruleDef = req.StaticRules[j];
                if (ruleDef == null)
                {
                    _issues.Add(new WorkflowIssue(
                        WorkflowIssueSeverity.Error,
                        $"{prefix}.staticRules[{j}]",
                        $"Requirement '{req.Id}' has a null static rule entry"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ruleDef.Id))
                {
                    _issues.Add(new WorkflowIssue(
                        WorkflowIssueSeverity.Error,
                        $"{prefix}.staticRules[{j}].id",
                        $"Requirement '{req.Id}' static rule has null/empty Id"));
                }

                if (string.IsNullOrWhiteSpace(ruleDef.Name))
                {
                    _issues.Add(new WorkflowIssue(
                        WorkflowIssueSeverity.Error,
                        $"{prefix}.staticRules[{j}].name",
                        $"Requirement '{req.Id}' static rule has null/empty Name"));
                }

                if (string.IsNullOrWhiteSpace(ruleDef.Type))
                {
                    _issues.Add(new WorkflowIssue(
                        WorkflowIssueSeverity.Error,
                        $"{prefix}.staticRules[{j}].type",
                        $"Requirement '{req.Id}' static rule has null/empty Type"));
                }
                else
                {
                    // Verify RuleFactory can create this rule
                    try
                    {
                        Rules.RuleFactory.Create(ruleDef);
                    }
                    catch (Exception ex)
                    {
                        _issues.Add(new WorkflowIssue(
                            WorkflowIssueSeverity.Error,
                            $"{prefix}.staticRules[{j}].type",
                            $"Requirement '{req.Id}' static rule type '{ruleDef.Type}' " +
                            $"cannot be created by RuleFactory: {ex.Message}"));
                    }
                }
            }
        }
    }

    private static Models.Rules.EvidenceRequirement ParseEvidenceRequirement(string? value)
    {
        return Merge.ResultMerger.ParseEvidenceRequirement(value);
    }
}

/// <summary>
/// An issue found during workflow validation.
/// </summary>
/// <param name="Severity">Error or Warning.</param>
/// <param name="Path">JSON path to the problematic field.</param>
/// <param name="Message">Human-readable description.</param>
public record WorkflowIssue(
    WorkflowIssueSeverity Severity,
    string Path,
    string Message
);

/// <summary>
/// Severity of a workflow validation issue.
/// </summary>
public enum WorkflowIssueSeverity
{
    /// <summary>Configuration cannot run — must be fixed.</summary>
    Error,
    /// <summary>Configuration may run but has a suspicious pattern.</summary>
    Warning,
}