using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests.Merge;

/// <summary>
/// M23.6 — RuntimeResultDetail / RuntimeMessage propagation tests.
///
/// Verifies that the raw RuntimeResultStatus and runtime message
/// are correctly propagated through the ResultMerger and preserved
/// in CompositeInspectionResult, even when ConvertRuntimeStatus
/// maps them to RuleStatus.NotEvaluated.
///
/// Each RuntimeResultStatus value (ProcessExited, Timeout, BuildFailed,
/// NotEvaluated, Passed, Failed) is tested independently.
///
/// Also verifies backward compatibility: when the new params are not
/// passed (null by default), the fields remain null.
/// </summary>
[Trait("Category", "Unit")]
public class M23_6_RuntimeResultPropagationTests
{
    // ═══════════════════════════════════════════════════════════════
    // String overload — RuntimeResultDetail preservation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// ProcessExited → RuntimeResultDetail preserved as ProcessExited.
    /// This is the most critical case: the raw failure info that was
    /// previously lost.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_ProcessExited_NotLost()
    {
        var composite = ResultMerger.Merge(
            "rule-1", "Rule 1", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.NotEvaluated,
            customMessage: null,
            runtimeResultDetail: RuntimeResultStatus.ProcessExited,
            runtimeMessage: "Player exited (code 139) before becoming ready.");

        // RuntimeStatus (RuleStatus) is NotEvaluated — this is correct behavior
        Assert.Equal(RuleStatus.NotEvaluated, composite.RuntimeStatus);
        // FinalStatus reflects the merge decision
        Assert.Equal(RuleStatus.NotEvaluated, composite.FinalStatus);
        // BUT the original runtime detail is preserved
        Assert.Equal(RuntimeResultStatus.ProcessExited, composite.RuntimeResultDetail);
        Assert.NotNull(composite.RuntimeMessage);
        Assert.Contains("code 139", composite.RuntimeMessage);
    }

    /// <summary>
    /// Timeout → RuntimeResultDetail preserved as Timeout.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_Timeout_NotLost()
    {
        var composite = ResultMerger.Merge(
            "rule-2", "Rule 2", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.NotEvaluated,
            runtimeResultDetail: RuntimeResultStatus.Timeout,
            runtimeMessage: "Runtime timed out after 60 seconds (WaitForReady).");

        Assert.Equal(RuleStatus.NotEvaluated, composite.RuntimeStatus);
        Assert.Equal(RuleStatus.NotEvaluated, composite.FinalStatus);
        Assert.Equal(RuntimeResultStatus.Timeout, composite.RuntimeResultDetail);
        Assert.Contains("timed out", composite.RuntimeMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BuildFailed → RuntimeResultDetail preserved as BuildFailed.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_BuildFailed_NotLost()
    {
        var composite = ResultMerger.Merge(
            "rule-3", "Rule 3", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.NotEvaluated,
            runtimeResultDetail: RuntimeResultStatus.BuildFailed,
            runtimeMessage: "Player build failed: Unity returned exit code 2.");

        Assert.Equal(RuleStatus.NotEvaluated, composite.RuntimeStatus);
        Assert.Equal(RuleStatus.NotEvaluated, composite.FinalStatus);
        Assert.Equal(RuntimeResultStatus.BuildFailed, composite.RuntimeResultDetail);
        Assert.Contains("exit code 2", composite.RuntimeMessage);
    }

    /// <summary>
    /// NotEvaluated (from RuntimeSession default) → RuntimeResultDetail preserved as NotEvaluated.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_NotEvaluated_IsPreserved()
    {
        var composite = ResultMerger.Merge(
            "rule-4", "Rule 4", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.NotEvaluated,
            runtimeResultDetail: RuntimeResultStatus.NotEvaluated);

        Assert.Equal(RuleStatus.NotEvaluated, composite.RuntimeStatus);
        Assert.Equal(RuleStatus.NotEvaluated, composite.FinalStatus);
        Assert.Equal(RuntimeResultStatus.NotEvaluated, composite.RuntimeResultDetail);
    }

    /// <summary>
    /// Passed (from successful runtime) → RuntimeResultDetail preserved as Passed.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_Passed_IsPreserved()
    {
        var composite = ResultMerger.Merge(
            "rule-5", "Rule 5", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.Passed,
            runtimeResultDetail: RuntimeResultStatus.Passed,
            runtimeMessage: "All assertions passed.");

        Assert.Equal(RuleStatus.Passed, composite.RuntimeStatus);
        Assert.Equal(RuleStatus.Passed, composite.FinalStatus);
        Assert.Equal(RuntimeResultStatus.Passed, composite.RuntimeResultDetail);
        Assert.Equal("All assertions passed.", composite.RuntimeMessage);
    }

    /// <summary>
    /// Failed (runtime assertion failed) → RuntimeResultDetail preserved as Failed.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_Failed_IsPreserved()
    {
        var composite = ResultMerger.Merge(
            "rule-6", "Rule 6", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.Failed,
            runtimeResultDetail: RuntimeResultStatus.Failed,
            runtimeMessage: "Assertion 'ExpectedSceneName = MainMenu' failed.");

        Assert.Equal(RuleStatus.Failed, composite.RuntimeStatus);
        Assert.Equal(RuleStatus.Failed, composite.FinalStatus);
        Assert.Equal(RuntimeResultStatus.Failed, composite.RuntimeResultDetail);
        Assert.Contains("Assertion", composite.RuntimeMessage);
    }

    /// <summary>
    /// When runtimeResultDetail is not passed, it must be null
    /// (preserving backward compat for callers that don't need it).
    /// </summary>
    [Fact]
    public void Merge_StringOverload_RuntimeResultDetail_NullByDefault()
    {
        var composite = ResultMerger.Merge(
            "rule-7", "Rule 7", EvidenceRequirement.StaticOnly,
            RuleStatus.Passed, null);

        Assert.Null(composite.RuntimeResultDetail);
    }

    /// <summary>
    /// When runtimeMessage is not passed, it must be null.
    /// </summary>
    [Fact]
    public void Merge_StringOverload_RuntimeMessage_NullByDefault()
    {
        var composite = ResultMerger.Merge(
            "rule-8", "Rule 8", EvidenceRequirement.StaticOnly,
            RuleStatus.Passed, null);

        Assert.Null(composite.RuntimeMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // Full models overload — backward compat (no RuntimeResultDetail)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Full models Merge overload does NOT accept RuntimeResultDetail —
    /// it stays null by default. This verifies that the new params
    /// don't break the existing API surface.
    /// </summary>
    [Fact]
    public void Merge_FullModels_NullSession_NoRuntimeDetail()
    {
        var rule = new RuleDefinition
        {
            Id = "static-only-rule",
            Name = "Static Only",
            Type = "SceneExists",
            Target = "MainMenu",
        };

        var staticResult = new RuleResult
        {
            RuleId = "static-only-rule",
            RuleName = "Static Only",
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = "OK.",
        };

        var composite = ResultMerger.Merge(rule, staticResult, null);

        Assert.Null(composite.RuntimeResultDetail);
        Assert.Null(composite.RuntimeMessage);
    }

    // ═══════════════════════════════════════════════════════════════
    // Cross-type consistency: RuntimeStatus vs RuntimeResultDetail
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// When RuntimeResultDetail is ProcessExited, RuntimeStatus must
    /// be NotEvaluated (per ConvertRuntimeStatus rules).
    /// This confirms the two fields have different semantics.
    /// </summary>
    [Fact]
    public void RuntimeResultDetail_ProcessExited_Means_RuntimeStatus_NotEvaluated()
    {
        var composite = ResultMerger.Merge(
            "rule-x1", "Cross-Type", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.NotEvaluated,
            runtimeResultDetail: RuntimeResultStatus.ProcessExited);

        Assert.Equal(RuleStatus.NotEvaluated, composite.RuntimeStatus);
        Assert.Equal(RuntimeResultStatus.ProcessExited, composite.RuntimeResultDetail);
    }

    /// <summary>
    /// When RuntimeResultDetail is Timeout, RuntimeStatus must be
    /// NotEvaluated (per ConvertRuntimeStatus rules).
    /// </summary>
    [Fact]
    public void RuntimeResultDetail_Timeout_Means_RuntimeStatus_NotEvaluated()
    {
        var composite = ResultMerger.Merge(
            "rule-x2", "Cross-Type", EvidenceRequirement.RuntimeRequired,
            RuleStatus.Passed, RuleStatus.NotEvaluated,
            runtimeResultDetail: RuntimeResultStatus.Timeout);

        Assert.Equal(RuleStatus.NotEvaluated, composite.RuntimeStatus);
        Assert.Equal(RuntimeResultStatus.Timeout, composite.RuntimeResultDetail);
    }
}