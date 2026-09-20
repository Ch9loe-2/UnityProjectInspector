using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// Orchestrates the full inspection workflow for an AssignmentDefinition.
///
/// Responsibilities (delegation only, no re-implementation):
///   1. Validate assignment via InspectionWorkflowValidator
///   2. Run static rules via RuleEngine for each requirement
///   3. Run runtime tests via RuntimeRunner when required
///   4. Merge results via ResultMerger
///   5. Aggregate individual requirement results into assignment result
///
/// This is a stateless orchestrator — all state is passed in or
/// returned in result objects.
/// </summary>
public class InspectionWorkflowRunner
{
    private readonly RuleEngine _ruleEngine;
    private readonly IRuntimeRunner _runtimeRunner;

    public InspectionWorkflowRunner(RuleEngine ruleEngine, IRuntimeRunner runtimeRunner)
    {
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _runtimeRunner = runtimeRunner ?? throw new ArgumentNullException(nameof(runtimeRunner));
    }

    /// <summary>
    /// Runs the full inspection workflow for the given assignment.
    /// </summary>
    /// <param name="assignment">The assignment definition to evaluate.</param>
    /// <param name="context">Inspection context with parsed project data.</param>
    /// <param name="runtimeOptions">Runtime options (required if any requirement needs
    /// runtime verification). May be null if no RuntimeRequired requirements exist.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>AssignmentInspectionResult with per-requirement and final status.</returns>
    public async Task<AssignmentInspectionResult> RunAsync(
        AssignmentDefinition assignment,
        InspectionContext context,
        RuntimeRunOptions? runtimeOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(context);

        // Phase 1: Validate
        var validator = new InspectionWorkflowValidator();
        var issues = validator.Validate(assignment);
        var errors = issues.Where(i => i.Severity == WorkflowIssueSeverity.Error).ToList();

        if (errors.Count > 0)
        {
            var errorMessages = string.Join("; ", errors.Select(e => e.Message));
            return new AssignmentInspectionResult
            {
                Assignment = assignment,
                FinalStatus = RuleStatus.NotEvaluated,
                Message = $"Assignment configuration is invalid: {errorMessages}",
            };
        }

        // Phase 2: Evaluate each requirement
        var requirementResults = new List<RequirementInspectionResult>();
        RuntimeSession? runtimeSession = null;

        foreach (var req in assignment.Requirements)
        {
            var result = await EvaluateRequirementAsync(
                req, context, runtimeOptions, runtimeSession, cancellationToken);

            // If a runtime session was produced, reuse it for subsequent requirements
            // that share the same runtime test (avoids launching Unity multiple times).

            requirementResults.Add(result);
        }

        // Phase 3: Aggregate
        return AggregateResults(assignment, requirementResults);
    }

    private async Task<RequirementInspectionResult> EvaluateRequirementAsync(
        RequirementDefinition req,
        InspectionContext context,
        RuntimeRunOptions? runtimeOptions,
        RuntimeSession? cachedSession,
        CancellationToken cancellationToken)
    {
        var evidenceReq = ResultMerger.ParseEvidenceRequirement(req.EvidenceRequirement);

        // ─── Step 1: Run static rules ─────────────────────────
        var staticResults = new List<RuleResult>();
        if (req.StaticRules != null && req.StaticRules.Count > 0)
        {
            var rules = new List<IRule>(req.StaticRules.Count);
            foreach (var ruleDef in req.StaticRules)
            {
                rules.Add(RuleFactory.Create(ruleDef));
            }
            staticResults = _ruleEngine.Run(context, rules);
        }

        // Determine overall static status:
        // Any Failed → Failed; any NotEvaluated + no Failed → NotEvaluated
        var staticStatus = AggregateStaticStatus(staticResults);

        // ─── Step 2: Run runtime if needed ────────────────────
        RuntimeSession? session = cachedSession;
        RuleStatus? runtimeStatus = null;

        if (evidenceReq == Models.Rules.EvidenceRequirement.RuntimeRequired)
        {
            if (staticStatus == RuleStatus.Failed)
            {
                // Static prerequisite failed — skip runtime
                // Runtime stays null — ResultMerger handles this
            }
            else if (runtimeOptions == null)
            {
                // Runtime required but no options provided
                // runtimeStatus stays null
            }
            else if (req.RuntimeTest == null)
            {
                // Configuration error — validator should have caught this
                // runtimeStatus stays null
            }
            else if (staticStatus == RuleStatus.Passed || staticStatus == RuleStatus.NotEvaluated)
            {
                // Run runtime
                try
                {
                    session = await _runtimeRunner.RunAsync(runtimeOptions, req.RuntimeTest, cancellationToken);
                    runtimeStatus = ConvertRuntimeStatus(session.Result);
                }
                catch
                {
                    runtimeStatus = RuleStatus.NotEvaluated;
                }
            }
        }

        // ─── Step 3: Merge ────────────────────────────────────
        // If there are no static rules, create a default "passed" static result
        // to allow the merger to work with runtime-only requirements.
        RuleResult? primaryStatic = null;
        if (req.StaticRules != null && req.StaticRules.Count > 0)
        {
            // Use the rule definition and first static result for merge
            var firstDef = req.StaticRules[0];
            var firstResult = staticResults.Count > 0 ? staticResults[0] : null;
            if (firstDef != null && firstResult != null)
            {
                primaryStatic = firstResult;
            }
        }

        // Build composite result
        CompositeInspectionResult? composite = null;
        if (primaryStatic != null && req.StaticRules is { Count: > 0 })
        {
            var ruleDef = req.StaticRules[0];
            var effectiveId = !string.IsNullOrEmpty(req.Id) ? req.Id :
                (ruleDef != null ? ruleDef.Id : "unknown");
            var effectiveName = !string.IsNullOrEmpty(req.Name) ? req.Name :
                (ruleDef != null ? ruleDef.Name : "Unknown");
            composite = ResultMerger.Merge(
                effectiveId,
                effectiveName,
                evidenceReq,
                staticStatus,
                runtimeStatus,
                null);
        }
        else if (runtimeStatus != null)
        {
            // Runtime-only requirement
            var finalStatus = runtimeStatus == RuleStatus.Passed ? RuleStatus.Passed : runtimeStatus.Value;
            composite = new CompositeInspectionResult
            {
                RuleId = req.Id ?? "runtime-only",
                RuleName = req.Name ?? "Runtime Only",
                StaticStatus = null,
                RuntimeStatus = runtimeStatus,
                Requirement = evidenceReq,
                FinalStatus = finalStatus,
                Message = runtimeStatus == RuleStatus.Passed
                    ? $"Runtime requirement passed."
                    : $"Runtime requirement failed.",
            };
        }

        // ─── Step 4: Determine overall requirement status ────
        RuleStatus reqStatus;
        string message;

        if (composite != null)
        {
            reqStatus = composite.FinalStatus;
            message = composite.Message;
        }
        else if (staticResults.Count > 0)
        {
            // StaticOnly with static results but no merge
            reqStatus = staticStatus;
            message = staticStatus == RuleStatus.Passed
                ? "All static checks passed."
                : $"Static check result: {staticStatus}";
        }
        else
        {
            reqStatus = RuleStatus.NotEvaluated;
            message = "No static rules or runtime tests to evaluate.";
        }

        return new RequirementInspectionResult
        {
            Requirement = req,
            StaticResults = staticResults,
            CompositeResult = composite,
            Status = reqStatus,
            Message = message,
        };
    }

    private static RuleStatus AggregateStaticStatus(List<RuleResult> results)
    {
        if (results.Count == 0)
            return RuleStatus.NotEvaluated;

        bool anyFailed = false;
        bool anyNotEvaluated = false;
        bool anyPassed = false;

        foreach (var r in results)
        {
            switch (r.Status)
            {
                case RuleStatus.Failed:
                    anyFailed = true;
                    break;
                case RuleStatus.NotEvaluated:
                    anyNotEvaluated = true;
                    break;
                case RuleStatus.Passed:
                    anyPassed = true;
                    break;
            }
        }

        if (anyFailed) return RuleStatus.Failed;
        if (!anyPassed && anyNotEvaluated) return RuleStatus.NotEvaluated;
        return RuleStatus.Passed;
    }

    private static AssignmentInspectionResult AggregateResults(
        AssignmentDefinition assignment,
        List<RequirementInspectionResult> requirementResults)
    {
        bool anyFailed = false;
        bool anyNotEvaluated = false;
        bool anyPassed = false;

        foreach (var rr in requirementResults)
        {
            switch (rr.Status)
            {
                case RuleStatus.Failed:
                    anyFailed = true;
                    break;
                case RuleStatus.NotEvaluated:
                    anyNotEvaluated = true;
                    break;
                case RuleStatus.Passed:
                    anyPassed = true;
                    break;
            }
        }

        RuleStatus finalStatus;
        string message;

        if (anyFailed)
        {
            finalStatus = RuleStatus.Failed;
            message = "Assignment failed: some requirements did not pass.";
        }
        else if (anyNotEvaluated)
        {
            finalStatus = RuleStatus.NotEvaluated;
            message = "Assignment could not be fully evaluated: some requirements have incomplete results.";
        }
        else if (anyPassed || requirementResults.Count == 0)
        {
            finalStatus = RuleStatus.Passed;
            message = "Assignment passed: all requirements satisfied.";
        }
        else
        {
            finalStatus = RuleStatus.NotEvaluated;
            message = "Assignment could not be evaluated.";
        }

        return new AssignmentInspectionResult
        {
            Assignment = assignment,
            RequirementResults = requirementResults,
            FinalStatus = finalStatus,
            Message = message,
        };
    }

    private static RuleStatus ConvertRuntimeStatus(RuntimeResultStatus status)
    {
        return status switch
        {
            RuntimeResultStatus.Passed => RuleStatus.Passed,
            RuntimeResultStatus.Failed => RuleStatus.Failed,
            _ => RuleStatus.NotEvaluated,
        };
    }
}