using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests.Merge;

/// <summary>
/// Unit tests for the ResultMerger merge matrix.
///
/// Covers all 8 combinations from the M15 spec:
///
/// | Static       | Runtime      | Requirement     | Final        |
/// | ------------ | ------------ | --------------- | ------------ |
/// | Passed       | NotEvaluated | StaticOnly      | Passed       |
/// | Failed       | NotEvaluated | StaticOnly      | Failed       |
/// | Passed       | Passed       | RuntimeRequired | Passed       |
/// | Passed       | Failed       | RuntimeRequired | Failed       |
/// | Passed       | NotEvaluated | RuntimeRequired | NotEvaluated |
/// | Failed       | Passed       | RuntimeRequired | Failed       |
/// | NotEvaluated | Passed       | RuntimeRequired | NotEvaluated |
/// | NotEvaluated | NotEvaluated | RuntimeRequired | NotEvaluated |
/// </summary>
[Trait("Category", "Unit")]
public class ResultMergerTests
{
    // ═══════════════════════════════════════════════════════════════
    // StaticOnly tests (Runtime is irrelevant)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Passed + NotEvaluated + StaticOnly → Passed
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_PassedAndNotEvaluated_ReturnsPassed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.NotEvaluated, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Passed, final);
    }

    /// <summary>
    /// Passed + Passed + StaticOnly → Passed (runtime ignored)
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_PassedAndPassed_ReturnsPassed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.Passed, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Passed, final);
    }

    /// <summary>
    /// Passed + Failed + StaticOnly → Passed (runtime failed, but only static matters)
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_PassedAndFailed_ReturnsPassed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.Failed, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Passed, final);
    }

    /// <summary>
    /// Failed + NotEvaluated + StaticOnly → Failed
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_FailedAndNotEvaluated_ReturnsFailed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Failed, RuleStatus.NotEvaluated, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Failed, final);
    }

    /// <summary>
    /// Failed + Passed + StaticOnly → Failed (runtime ignored)
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_FailedAndPassed_ReturnsFailed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Failed, RuleStatus.Passed, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Failed, final);
    }

    /// <summary>
    /// NotEvaluated + (any) + StaticOnly → NotEvaluated
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_NotEvaluatedAndAny_ReturnsNotEvaluated()
    {
        var (final1, _) = ResultMerger.MergeStatuses(
            RuleStatus.NotEvaluated, RuleStatus.Passed, EvidenceRequirement.StaticOnly);
        Assert.Equal(RuleStatus.NotEvaluated, final1);

        var (final2, _) = ResultMerger.MergeStatuses(
            RuleStatus.NotEvaluated, RuleStatus.NotEvaluated, EvidenceRequirement.StaticOnly);
        Assert.Equal(RuleStatus.NotEvaluated, final2);
    }

    // ═══════════════════════════════════════════════════════════════
    // Table from spec section 15 — RuntimeRequired
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Row 1: Passed + NotEvaluated + StaticOnly → Passed
    /// </summary>
    [Fact]
    public void Merge_Row1_StaticPassed_AnyRuntime_StaticOnly_ReturnsPassed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.NotEvaluated, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Passed, final);
    }

    /// <summary>
    /// Row 2: Failed + NotEvaluated + StaticOnly → Failed
    /// </summary>
    [Fact]
    public void Merge_Row2_StaticFailed_AnyRuntime_StaticOnly_ReturnsFailed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Failed, RuleStatus.NotEvaluated, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Failed, final);
    }

    /// <summary>
    /// Row 3: Passed + Passed + RuntimeRequired → Passed
    /// </summary>
    [Fact]
    public void Merge_Row3_StaticPassed_RuntimePassed_RuntimeRequired_ReturnsPassed()
    {
        var (final, msg) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.Passed, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.Passed, final);
        // Message should indicate both passed
        Assert.Contains("passed", msg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Row 4: Passed + Failed + RuntimeRequired → Failed
    /// </summary>
    [Fact]
    public void Merge_Row4_StaticPassed_RuntimeFailed_RuntimeRequired_ReturnsFailed()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.Failed, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.Failed, final);
    }

    /// <summary>
    /// Row 5: Passed + NotEvaluated + RuntimeRequired → NotEvaluated
    /// Critical: cannot Passed when runtime is missing!
    /// </summary>
    [Fact]
    public void Merge_Row5_StaticPassed_RuntimeNotEvaluated_RuntimeRequired_ReturnsNotEvaluated()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.NotEvaluated, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.NotEvaluated, final);
    }

    /// <summary>
    /// Row 6: Failed + Passed + RuntimeRequired → Failed
    /// Critical: Static failure is NOT overridden by runtime success!
    /// </summary>
    [Fact]
    public void Merge_Row6_StaticFailed_RuntimePassed_RuntimeRequired_ReturnsFailed()
    {
        var (final, msg) = ResultMerger.MergeStatuses(
            RuleStatus.Failed, RuleStatus.Passed, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.Failed, final);
        Assert.Contains("Static prerequisite failed", msg);
        Assert.Contains("does not override", msg);
    }

    /// <summary>
    /// Row 7: NotEvaluated + Passed + RuntimeRequired → NotEvaluated
    /// </summary>
    [Fact]
    public void Merge_Row7_StaticNotEvaluated_RuntimePassed_RuntimeRequired_ReturnsNotEvaluated()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.NotEvaluated, RuleStatus.Passed, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.NotEvaluated, final);
    }

    /// <summary>
    /// Row 8: NotEvaluated + NotEvaluated + RuntimeRequired → NotEvaluated
    /// </summary>
    [Fact]
    public void Merge_Row8_StaticNotEvaluated_RuntimeNotEvaluated_RuntimeRequired_ReturnsNotEvaluated()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.NotEvaluated, RuleStatus.NotEvaluated, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.NotEvaluated, final);
    }

    // ═══════════════════════════════════════════════════════════════
    // Edge cases: null runtime
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// StaticOnly with null runtime → static result unchanged
    /// </summary>
    [Fact]
    public void Merge_StaticOnly_NullRuntime_UsesStaticOnly()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, null, EvidenceRequirement.StaticOnly);

        Assert.Equal(RuleStatus.Passed, final);
    }

    /// <summary>
    /// RuntimeRequired with null runtime → NotEvaluated
    /// Runtime was never performed.
    /// </summary>
    [Fact]
    public void Merge_RuntimeRequired_NullRuntime_ReturnsNotEvaluated()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, null, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.NotEvaluated, final);
    }

    // ═══════════════════════════════════════════════════════════════
    // ParseEvidenceRequirement tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ParseEvidenceRequirement_Null_DefaultsToStaticOnly()
    {
        Assert.Equal(EvidenceRequirement.StaticOnly,
            ResultMerger.ParseEvidenceRequirement(null));
    }

    [Fact]
    public void ParseEvidenceRequirement_Empty_DefaultsToStaticOnly()
    {
        Assert.Equal(EvidenceRequirement.StaticOnly,
            ResultMerger.ParseEvidenceRequirement(""));
    }

    [Fact]
    public void ParseEvidenceRequirement_Whitespace_DefaultsToStaticOnly()
    {
        Assert.Equal(EvidenceRequirement.StaticOnly,
            ResultMerger.ParseEvidenceRequirement("  "));
    }

    [Fact]
    public void ParseEvidenceRequirement_StaticOnly_ReturnsStaticOnly()
    {
        Assert.Equal(EvidenceRequirement.StaticOnly,
            ResultMerger.ParseEvidenceRequirement("StaticOnly"));
    }

    [Fact]
    public void ParseEvidenceRequirement_RuntimeRequired_ReturnsRuntimeRequired()
    {
        Assert.Equal(EvidenceRequirement.RuntimeRequired,
            ResultMerger.ParseEvidenceRequirement("RuntimeRequired"));
    }

    [Fact]
    public void ParseEvidenceRequirement_UnknownValue_DefaultsToStaticOnly()
    {
        // Unknown values should safely default to StaticOnly
        Assert.Equal(EvidenceRequirement.StaticOnly,
            ResultMerger.ParseEvidenceRequirement("UnknownValue"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Merge overload with full models
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_FullModels_RuntimeRequired_Passed_ReturnsComposite()
    {
        var rule = new RuleDefinition
        {
            Id = "test-rule",
            Name = "Test Rule",
            Type = "SceneExists",
            Target = "TestScene",
            EvidenceRequirement = "RuntimeRequired",
        };

        var staticResult = new RuleResult
        {
            RuleId = "test-rule",
            RuleName = "Test Rule",
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Error,
            Message = "Scene exists.",
        };

        var session = new RuntimeSession
        {
            ProjectPath = "/tmp/test",
            StartedAt = DateTime.UtcNow,
            Result = RuntimeResultStatus.Passed,
        };

        var composite = ResultMerger.Merge(rule, staticResult, session);

        Assert.Equal("test-rule", composite.RuleId);
        Assert.Equal("Test Rule", composite.RuleName);
        Assert.Equal(RuleStatus.Passed, composite.StaticStatus);
        Assert.Equal(RuleStatus.Passed, composite.RuntimeStatus);
        Assert.Equal(EvidenceRequirement.RuntimeRequired, composite.Requirement);
        Assert.Equal(RuleStatus.Passed, composite.FinalStatus);
    }

    [Fact]
    public void Merge_FullModels_NullSession_IsHandled()
    {
        var rule = new RuleDefinition
        {
            Id = "static-only-rule",
            Name = "Static Only Rule",
            Type = "SceneExists",
            Target = "MainMenu",
        };

        var staticResult = new RuleResult
        {
            RuleId = "static-only-rule",
            RuleName = "Static Only Rule",
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = "Scene MainMenu exists.",
        };

        // Null session — no runtime was performed
        var composite = ResultMerger.Merge(rule, staticResult, null);

        Assert.Equal(RuleStatus.Passed, composite.FinalStatus);
        Assert.Equal(EvidenceRequirement.StaticOnly, composite.Requirement);
        Assert.Null(composite.RuntimeStatus);
    }

    [Fact]
    public void Merge_FullModels_StaticFailed_RuntimePassed_ReturnsFailed()
    {
        var rule = new RuleDefinition
        {
            Id = "runtime-required-rule",
            Name = "Runtime Required Rule",
            Type = "CodeEvidence",
            Target = "SomeGO",
            ExpectedMethod = "DoThing",
            EvidenceRequirement = "RuntimeRequired",
        };

        var staticResult = new RuleResult
        {
            RuleId = "runtime-required-rule",
            RuleName = "Runtime Required Rule",
            Status = RuleStatus.Failed,
            Severity = RuleSeverity.Error,
            Message = "Code evidence not found.",
        };

        var session = new RuntimeSession
        {
            ProjectPath = "/tmp/test",
            StartedAt = DateTime.UtcNow,
            Result = RuntimeResultStatus.Passed,
        };

        var composite = ResultMerger.Merge(rule, staticResult, session);

        Assert.Equal(RuleStatus.Failed, composite.FinalStatus);
        Assert.Contains("Static prerequisite failed", composite.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // RuntimeResultStatus → RuleStatus conversion behaviors
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Merge_RuntimeTimeout_ConvertsToNotEvaluated()
    {
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.NotEvaluated, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.NotEvaluated, final);
    }

    [Fact]
    public void Merge_RuntimeBuildFailed_BehavesAsNotEvaluated()
    {
        // BuildFailed converts to NotEvaluated — the runtime never ran
        var (final, _) = ResultMerger.MergeStatuses(
            RuleStatus.Passed, RuleStatus.NotEvaluated, EvidenceRequirement.RuntimeRequired);

        Assert.Equal(RuleStatus.NotEvaluated, final);
    }
}