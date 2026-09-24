using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// Mutable state holder for session failure tracking across async methods.
/// Async methods cannot use ref parameters.
/// </summary>
internal class SessionSharingState
{
    public bool SessionFailed { get; set; }
}

/// <summary>
/// Orchestrates the full inspection workflow for an AssignmentDefinition.
///
/// Responsibilities (delegation only, no re-implementation):
///   1. Validate assignment via InspectionWorkflowValidator
///   2. Run static rules via RuleEngine for each requirement
///   3. Run runtime tests via RuntimeRunner — shared across requirements
///      when the runner supports ISupportsSessionSharing
///   4. Merge results via ResultMerger
///   5. Aggregate individual requirement results into assignment result
///
/// Session ownership (M17+):
///   InspectionWorkflowRunner creates a RuntimeSessionScope at the Assignment level
///   when any requirement needs runtime verification. The same session is reused
///   across all RuntimeRequired requirements that pass static checks.
///
///   Lifecycle:
///     Assignment-level scope created (if any RuntimeRequired exists)
///       → For each RuntimeRequired requirement (if static not failed):
///           ExecuteScriptOnScopeAsync(scope, req.RuntimeTest)
///       → FinalizeScopeAsync + Dispose (after all requirements complete)
///
///   This is a stateless orchestrator with scoped session ownership.
/// </summary>
public class InspectionWorkflowRunner
{
    private readonly RuleEngine _ruleEngine;
    private readonly IRuntimeRunner _runtimeRunner;
    private readonly HarnessDeployer? _harnessDeployer;

    public InspectionWorkflowRunner(
        RuleEngine ruleEngine,
        IRuntimeRunner runtimeRunner,
        HarnessDeployer? harnessDeployer = null)
    {
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _runtimeRunner = runtimeRunner ?? throw new ArgumentNullException(nameof(runtimeRunner));
        _harnessDeployer = harnessDeployer;
    }

    /// <summary>
    /// Runs the full inspection workflow for the given assignment.
    ///
    /// When the runtime runner supports ISupportsSessionSharing, multiple
    /// RuntimeRequired requirements share a single Unity Player session.
    /// </summary>
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

        // Phase 2: Deploy Harness (if HarnessDeployer is configured AND runtime is needed)
        bool anyRuntimeRequired = assignment.Requirements.Any(
            r => ResultMerger.ParseEvidenceRequirement(r.EvidenceRequirement)
                 == EvidenceRequirement.RuntimeRequired);

        HarnessSnapshot? harnessSnapshot = null;

        if (_harnessDeployer != null && runtimeOptions != null && anyRuntimeRequired)
        {
            harnessSnapshot = await _harnessDeployer.DeployAsync(runtimeOptions);
        }

        try
        {
            // Check if the runtime runner supports session sharing
            var sessionSharingRunner = _runtimeRunner as ISupportsSessionSharing;

            // Phase 3: Evaluate — with or without session sharing
            if (sessionSharingRunner != null)
            {
                return await RunWithSessionSharingAsync(
                    assignment, context, runtimeOptions, sessionSharingRunner, cancellationToken);
            }

            // Fallback: per-requirement runner (backward compat)
            return await RunPerRequirementAsync(
                assignment, context, runtimeOptions, cancellationToken);
        }
        finally
        {
            // Guaranteed cleanup: remove deployed harness files
            if (harnessSnapshot != null && _harnessDeployer != null && runtimeOptions != null)
            {
                // CleanupAsync does not throw — logs warnings on failure
                await _harnessDeployer.CleanupAsync(harnessSnapshot, runtimeOptions);
            }
        }
    }

    /// <summary>
    /// Runs the assignment with session sharing.
    /// One shared RuntimeSessionScope across all RuntimeRequired requirements.
    /// </summary>
    private async Task<AssignmentInspectionResult> RunWithSessionSharingAsync(
        AssignmentDefinition assignment,
        InspectionContext context,
        RuntimeRunOptions? runtimeOptions,
        ISupportsSessionSharing sessionSharingRunner,
        CancellationToken cancellationToken)
    {
        var requirementResults = new List<RequirementInspectionResult>();
        var state = new SessionSharingState();

        // Check if any requirement needs runtime
        bool anyRuntimeRequired = assignment.Requirements.Any(
            r => ResultMerger.ParseEvidenceRequirement(r.EvidenceRequirement)
                 == EvidenceRequirement.RuntimeRequired);

        // Create shared session only if runtime is needed and options are provided
        RuntimeSessionScope? sharedScope = null;

        if (anyRuntimeRequired && runtimeOptions != null)
        {
            try
            {
                sharedScope = await sessionSharingRunner.CreateSessionAsync(
                    runtimeOptions, cancellationToken);

                // If session creation failed (build/launch error), mark for later
                if (sharedScope.IsFailed)
                {
                    state.SessionFailed = true;
                }
            }
            catch
            {
                state.SessionFailed = true;
            }
        }

        try
        {
            foreach (var req in assignment.Requirements)
            {
                var result = await EvaluateRequirementWithSessionAsync(
                    req, context, runtimeOptions, sessionSharingRunner,
                    sharedScope, state, cancellationToken);

                requirementResults.Add(result);
            }
        }
        finally
        {
            // Finalize and clean up the shared session
            if (sharedScope != null && !sharedScope.IsFailed)
            {
                try
                {
                    if (!sharedScope.IsFinalized)
                    {
                        await sessionSharingRunner.FinalizeScopeAsync(
                            sharedScope, CancellationToken.None);
                    }
                }
                catch
                {
                    // Best-effort finalization
                }
                finally
                {
                    sharedScope.Dispose();
                }
            }
        }

        return AggregateResults(assignment, requirementResults);
    }

    /// <summary>
    /// Evaluates a single requirement using the shared session scope.
    /// The state.SessionFailed flag is set to true if the session becomes unusable.
    /// </summary>
    private async Task<RequirementInspectionResult> EvaluateRequirementWithSessionAsync(
        RequirementDefinition req,
        InspectionContext context,
        RuntimeRunOptions? runtimeOptions,
        ISupportsSessionSharing sessionSharingRunner,
        RuntimeSessionScope? sharedScope,
        SessionSharingState state,
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

        // Determine overall static status
        var staticStatus = AggregateStaticStatus(staticResults);

        // ─── Step 2: Run runtime if needed ────────────────────
        RuleStatus? runtimeStatus = null;
        RuntimeResultStatus? runtimeResultDetail = null;
        string? runtimeMessage = null;

        if (evidenceReq == EvidenceRequirement.RuntimeRequired)
        {
            if (staticStatus == RuleStatus.Failed)
            {
                // Static prerequisite failed — skip runtime
            }
            else if (runtimeOptions == null)
            {
                // Runtime required but no options provided
            }
            else if (req.RuntimeTest == null)
            {
                // Configuration error
            }
            else if (state.SessionFailed || sharedScope == null || sharedScope.IsFailed)
            {
                // Session is dead — mark as NotEvaluated
                runtimeStatus = RuleStatus.NotEvaluated;
                if (sharedScope != null)
                {
                    runtimeResultDetail = sharedScope.Session.Result;
                    runtimeMessage = sharedScope.Session.Message;
                }
            }
            else if (staticStatus == RuleStatus.Passed || staticStatus == RuleStatus.NotEvaluated)
            {
                // Run runtime on the shared session
                try
                {
                    var reqSession = await sessionSharingRunner.ExecuteScriptOnScopeAsync(
                        sharedScope, req.RuntimeTest, cancellationToken);

                    runtimeStatus = ConvertRuntimeStatus(reqSession.Result);
                    runtimeResultDetail = reqSession.Result;
                    runtimeMessage = reqSession.Message;

                    // If the session itself failed (ProcessExited, etc.),
                    // mark it so subsequent requirements don't try to use it
                    if (reqSession.Result == RuntimeResultStatus.ProcessExited
                        || reqSession.Result == RuntimeResultStatus.Timeout)
                    {
                        if (!sharedScope.PlayerProcess.HasExited)
                        {
                            // Session is still alive, this was a requirement-level issue
                        }
                        else
                        {
                            // Session is dead
                            state.SessionFailed = true;
                        }
                    }
                }
                catch
                {
                    runtimeStatus = RuleStatus.NotEvaluated;
                    state.SessionFailed = true;
                }
            }
        }

        // ─── Step 3: Merge ────────────────────────────────────
        // (same as before, extract from EvaluateRequirementAsync)

        RuleResult? primaryStatic = null;
        if (req.StaticRules != null && req.StaticRules.Count > 0)
        {
            var firstDef = req.StaticRules[0];
            var firstResult = staticResults.Count > 0 ? staticResults[0] : null;
            if (firstDef != null && firstResult != null)
            {
                primaryStatic = firstResult;
            }
        }

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
                null,
                runtimeResultDetail,
                runtimeMessage);
        }
        else if (runtimeStatus != null)
        {
            var finalStatus = runtimeStatus == RuleStatus.Passed ? RuleStatus.Passed : runtimeStatus.Value;
            composite = new CompositeInspectionResult
            {
                RuleId = req.Id ?? "runtime-only",
                RuleName = req.Name ?? "Runtime Only",
                StaticStatus = null,
                RuntimeStatus = runtimeStatus,
                RuntimeResultDetail = runtimeResultDetail,
                RuntimeMessage = runtimeMessage,
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

    /// <summary>
    /// Original per-requirement evaluation path (for runners that don't support session sharing).
    /// Each RuntimeRequired requirement gets its own Build + Launch + Cleanup cycle.
    /// </summary>
    private async Task<AssignmentInspectionResult> RunPerRequirementAsync(
        AssignmentDefinition assignment,
        InspectionContext context,
        RuntimeRunOptions? runtimeOptions,
        CancellationToken cancellationToken)
    {
        var requirementResults = new List<RequirementInspectionResult>();
        RuntimeSession? runtimeSession = null;

        foreach (var req in assignment.Requirements)
        {
            var evidenceReq = ResultMerger.ParseEvidenceRequirement(req.EvidenceRequirement);

            // ─── Static ──────────────────────────────────────────
            var staticResults = new List<RuleResult>();
            if (req.StaticRules != null && req.StaticRules.Count > 0)
            {
                var rules = new List<IRule>(req.StaticRules.Count);
                foreach (var ruleDef in req.StaticRules)
                    rules.Add(RuleFactory.Create(ruleDef));
                staticResults = _ruleEngine.Run(context, rules);
            }

            var staticStatus = AggregateStaticStatus(staticResults);

            // ─── Runtime ─────────────────────────────────────────
            RuleStatus? runtimeStatus = null;
            RuntimeResultStatus? runtimeResultDetail = null;
            string? runtimeMessage = null;

            if (evidenceReq == EvidenceRequirement.RuntimeRequired)
            {
                if (staticStatus == RuleStatus.Failed)
                {
                    // Static prerequisite failed — skip
                }
                else if (runtimeOptions == null)
                {
                    // No options provided
                }
                else if (req.RuntimeTest == null)
                {
                    // Configuration error
                }
                else if (staticStatus == RuleStatus.Passed || staticStatus == RuleStatus.NotEvaluated)
                {
                    // Run runtime (per-requirement build+launch)
                    try
                    {
                        // Try per-requirement: create session if not already available,
                        // else reuse (old cachedSession pattern — mostly unused)
                        if (runtimeSession == null)
                        {
                            runtimeSession = await _runtimeRunner.RunAsync(
                                runtimeOptions, req.RuntimeTest, cancellationToken);
                        }
                        // Note: cachedSession is not truly reused here because
                        // the old pattern was never wired up. This fallback path
                        // matches the original M16 behavior exactly.
                        runtimeStatus = ConvertRuntimeStatus(runtimeSession.Result);
                        runtimeResultDetail = runtimeSession.Result;
                        runtimeMessage = runtimeSession.Message;
                    }
                    catch
                    {
                        runtimeStatus = RuleStatus.NotEvaluated;
                    }
                }
            }

            // ─── Merge ───────────────────────────────────────────
            // (same logic as EvaluateRequirementWithSessionAsync above)
            requirementResults.Add(BuildRequirementResult(req, staticResults, evidenceReq, staticStatus, runtimeStatus, runtimeResultDetail, runtimeMessage));
        }

        return AggregateResults(assignment, requirementResults);
    }

    /// <summary>
    /// Builds a RequirementInspectionResult from static/runtime results.
    /// Shared between session-sharing and per-requirement paths.
    /// </summary>
    private static RequirementInspectionResult BuildRequirementResult(
        RequirementDefinition req,
        List<RuleResult> staticResults,
        EvidenceRequirement evidenceReq,
        RuleStatus staticStatus,
        RuleStatus? runtimeStatus,
        RuntimeResultStatus? runtimeResultDetail = null,
        string? runtimeMessage = null)
    {
        RuleResult? primaryStatic = null;
        if (req.StaticRules != null && req.StaticRules.Count > 0)
        {
            var firstResult = staticResults.Count > 0 ? staticResults[0] : null;
            if (firstResult != null)
                primaryStatic = firstResult;
        }

        CompositeInspectionResult? composite = null;
        if (primaryStatic != null && req.StaticRules is { Count: > 0 })
        {
            var ruleDef = req.StaticRules[0];
            var effectiveId = !string.IsNullOrEmpty(req.Id) ? req.Id :
                (ruleDef != null ? ruleDef.Id : "unknown");
            var effectiveName = !string.IsNullOrEmpty(req.Name) ? req.Name :
                (ruleDef != null ? ruleDef.Name : "Unknown");
            composite = ResultMerger.Merge(
                effectiveId, effectiveName, evidenceReq,
                staticStatus, runtimeStatus, null,
                runtimeResultDetail, runtimeMessage);
        }
        else if (runtimeStatus != null)
        {
            var finalStatus = runtimeStatus == RuleStatus.Passed
                ? RuleStatus.Passed : runtimeStatus.Value;
            composite = new CompositeInspectionResult
            {
                RuleId = req.Id ?? "runtime-only",
                RuleName = req.Name ?? "Runtime Only",
                StaticStatus = null,
                RuntimeStatus = runtimeStatus,
                RuntimeResultDetail = runtimeResultDetail,
                RuntimeMessage = runtimeMessage,
                Requirement = evidenceReq,
                FinalStatus = finalStatus,
                Message = runtimeStatus == RuleStatus.Passed
                    ? "Runtime requirement passed."
                    : "Runtime requirement failed.",
            };
        }

        RuleStatus reqStatus;
        string message;

        if (composite != null)
        {
            reqStatus = composite.FinalStatus;
            message = composite.Message;
        }
        else if (staticResults.Count > 0)
        {
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