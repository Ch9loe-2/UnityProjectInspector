using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests.Merge;

/// <summary>
/// M15 Integration Test: Static + Runtime Result Merge.
///
/// This test exercises the full end-to-end pipeline:
///   Real Unity Editor → Real Player Build → Real Standalone Player
///   → IPC → ClickButton → Scene Transition → Runtime Evidence
///   → Runtime Assertions → Runtime Result
///   → ResultMerger with static RuleResult → CompositeInspectionResult
///
/// This proves that M14's Runtime capabilities are truly integrated
/// into the verification result system through the ResultMerger.
///
/// Requires:
///   - Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
///   - M14_RuntimeActionFixture in the repo's experiments/ directory
///   - Project must have been set up (M14_Setup.InitializeScenes)
/// </summary>
[CollectionDefinition("M15RuntimeMergeIntegration", DisableParallelization = true)]
public class M15RuntimeMergeIntegrationCollection { }

[Collection("M15RuntimeMergeIntegration")]
[Trait("Category", "Integration")]
public class M15RuntimeMergeIntegrationTests
{
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private static readonly string? ProjectPath;

    static M15RuntimeMergeIntegrationTests()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..", ".."));

        var candidate = Path.Combine(
            repoRoot, "experiments", "M14_RuntimeActionFixture");

        ProjectPath = Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Full M15 Integration Scenario:
    ///
    /// Static evidence:
    ///   SceneExists("MainMenu") — StaticOnly → Passed
    ///
    /// Runtime scenario:
    ///   Build M14RuntimePlayer
    ///   → Launch Player
    ///   → ClickButton(MainMenuButton)
    ///   → Wait(1000ms)
    ///   → ObserveActiveScene
    ///   → AssertActiveScene("TargetScene")
    ///   → AssertNoExceptions
    ///   → RuntimeResultStatus.Passed
    ///
    /// Merge:
    ///   Static Passed + Runtime Passed + RuntimeRequired → Composite Passed
    ///
    /// This proves that M14's Runtime capability is integrated
    /// into the merged verification result system.
    /// </summary>
    [Fact]
    public async Task M15_Merge_StaticAndRuntimeClickButton_ReturnsCompositePassed()
    {
        AssertPrerequisites();

        // ─── Static Inspection (simulated) ───────────────────────
        // In a real pipeline, this would come from RuleEngine.
        // For this integration test, we construct it directly
        // as we're testing the merge, not the static parser.
        var rule = new RuleDefinition
        {
            Id = "maze-start",
            Name = "Start button enters Maze2D",
            Type = "CodeEvidence",
            Target = "MainMenuButton",
            ExpectedMethod = "Start2D",
            EvidenceRequirement = "RuntimeRequired",
        };

        var staticResult = new RuleResult
        {
            RuleId = "maze-start",
            RuleName = "Start button enters Maze2D",
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Warning,
            Message = "Static: StartButton → GameManager.Start2D() → SceneManager.LoadScene('TargetScene')",
        };

        // ─── Runtime Inspection (real Player) ────────────────────
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = ProjectPath!,
            ProductName = "M14RuntimePlayer",
            BuildMethod = "M14_PlayerBuild.Build",
            SessionDirEnvVar = "M14_SESSION_DIR",
            ReadyTimeoutSeconds = 30,
            CommandTimeoutSeconds = 30,
            PlayerTimeoutSeconds = 60,
            BuildTimeoutSeconds = 180,
        };

        var script = new RuntimeTestScript
        {
            Name = "Click MainMenuButton → Load TargetScene (M15 Merge)",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                new ClickButtonAction { ActionId = "click_button", GameObjectName = "MainMenuButton" },
                new WaitAction { ActionId = "scene_wait", Milliseconds = 1000 },
                new ObserveActiveSceneAction { ActionId = "observe_scene" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                new AssertActiveScene { AssertionId = "assert_scene", ExpectedSceneName = "TargetScene" },
                new AssertNoExceptions { AssertionId = "assert_no_ex" },
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(options, script);

        // Verify Runtime passed
        Assert.Equal(RuntimeResultStatus.Passed, session.Result);

        // ─── Merge ───────────────────────────────────────────────
        var composite = ResultMerger.Merge(rule, staticResult, session);

        // ─── Assertions ──────────────────────────────────────────
        Assert.Equal("maze-start", composite.RuleId);
        Assert.Equal("RuntimeRequired", rule.EvidenceRequirement);
        Assert.Equal(EvidenceRequirement.RuntimeRequired, composite.Requirement);

        // Static status should be preserved
        Assert.Equal(RuleStatus.Passed, composite.StaticStatus);

        // Runtime should have been passed
        Assert.NotNull(composite.RuntimeStatus);
        Assert.Equal(RuleStatus.Passed, composite.RuntimeStatus!.Value);

        // FINAL: Composite must be Passed
        Assert.Equal(RuleStatus.Passed, composite.FinalStatus);

        // Message should be descriptive
        Assert.NotNull(composite.Message);
        Assert.Contains("passed", composite.Message, StringComparison.OrdinalIgnoreCase);

        // Evidence from real Player is available on the session
        Assert.NotEmpty(session.Evidence);
        Assert.Contains(session.Evidence, e =>
            e.Type == "Action:ClickButton" && e.Obtained);
        Assert.Contains(session.Evidence, e =>
            e.Type == "Action:ObserveActiveScene" && e.Obtained);
    }

    /// <summary>
    /// StaticOnly rule with Runtime NotEvaluated → Composite Passed.
    /// Verifies that tools do NOT start Unity for every check.
    /// </summary>
    [Fact]
    public void M15_Merge_StaticOnly_NoRuntime_ReturnsPassed()
    {
        var rule = new RuleDefinition
        {
            Id = "scene.exists",
            Name = "MainMenu scene exists",
            Type = "SceneExists",
            Target = "MainMenu",
            // No EvidenceRequirement → defaults to StaticOnly
        };

        var staticResult = new RuleResult
        {
            RuleId = "scene.exists",
            RuleName = "MainMenu scene exists",
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = "Scene 'MainMenu' exists in project.",
        };

        // No runtime session at all — null
        var composite = ResultMerger.Merge(rule, staticResult, null);

        Assert.Equal(RuleStatus.Passed, composite.FinalStatus);
        Assert.Equal(EvidenceRequirement.StaticOnly, composite.Requirement);
        Assert.Null(composite.RuntimeStatus);
    }

    /// <summary>
    /// StaticOnly rule, StaticFailed → Composite Failed.
    /// No runtime needed — static failure is final.
    /// </summary>
    [Fact]
    public void M15_Merge_StaticOnly_StaticFailed_ReturnsFailed()
    {
        var rule = new RuleDefinition
        {
            Id = "scene.exists",
            Name = "MissingScene exists",
            Type = "SceneExists",
            Target = "MissingScene",
        };

        var staticResult = new RuleResult
        {
            RuleId = "scene.exists",
            RuleName = "MissingScene exists",
            Status = RuleStatus.Failed,
            Severity = RuleSeverity.Error,
            Message = "Scene 'MissingScene' not found in project.",
        };

        var composite = ResultMerger.Merge(rule, staticResult, null);

        Assert.Equal(RuleStatus.Failed, composite.FinalStatus);
        Assert.Equal(EvidenceRequirement.StaticOnly, composite.Requirement);
    }

    /// <summary>
    /// RuntimeRequired but Runtime NotEvaluated → Composite NotEvaluated.
    /// Cannot pass when runtime evidence is missing for a RuntimeRequired rule.
    /// </summary>
    [Fact]
    public void M15_Merge_RuntimeRequired_MissingRuntime_ReturnsNotEvaluated()
    {
        var rule = new RuleDefinition
        {
            Id = "runtime-check",
            Name = "Runtime check",
            Type = "CodeEvidence",
            Target = "SomeGO",
            ExpectedMethod = "DoThing",
            EvidenceRequirement = "RuntimeRequired",
        };

        var staticResult = new RuleResult
        {
            RuleId = "runtime-check",
            RuleName = "Runtime check",
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Warning,
            Message = "Static analysis passed.",
        };

        // Null session — no runtime was ever performed
        var composite = ResultMerger.Merge(rule, staticResult, null);

        Assert.Equal(RuleStatus.NotEvaluated, composite.FinalStatus);
        Assert.Equal(EvidenceRequirement.RuntimeRequired, composite.Requirement);
        Assert.Contains("not satisfied", composite.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPrerequisites()
    {
        Assert.True(File.Exists(UnityExecutable),
            $"Unity Editor not found at {UnityExecutable}. " +
            $"This integration test requires Unity 2022.3.62f3c1 installed.");

        Assert.NotNull(ProjectPath);
        Assert.True(Directory.Exists(ProjectPath),
            $"M14 project not found. This test requires experiments/M14_RuntimeActionFixture.");
    }
}