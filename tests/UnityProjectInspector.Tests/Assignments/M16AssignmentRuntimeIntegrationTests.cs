using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// M16 Real Runtime Integration Test:
/// Full Assignment workflow with real Unity Player runtime.
///
/// Pipeline:
///   Load maze-2d-basic.json
///   → Validate
///   → Build InspectionContext from M14 fixture scenes
///   → Run static rules via RuleEngine
///   → Run RuntimeTestScript via true RuntimeRunner
///   → ResultMerger
///   → Composite AssignmentInspectionResult
///
/// Requires:
///   - Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
///   - M14_RuntimeActionFixture in the repo's experiments/ directory
/// </summary>
[CollectionDefinition("M16AssignmentIntegration", DisableParallelization = true)]
public class M16AssignmentIntegrationCollection { }

[Collection("M16AssignmentIntegration")]
[Trait("Category", "Integration")]
public class M16AssignmentRuntimeIntegrationTests
{
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private static readonly string? FixturePath;

    static M16AssignmentRuntimeIntegrationTests()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var candidate = Path.Combine(repoRoot, "experiments", "M14_RuntimeActionFixture");
        FixturePath = Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Real Runtime Integration:
    /// One RuntimeRequired requirement with click button → real Player build → scene transition.
    /// Verifies the full M16 merge pipeline with real runtime.
    /// </summary>
    [Fact]
    public async Task M16_Assignment_RuntimeClickButton_ReturnsCompositePassed()
    {
        AssertPrerequisites();

        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = FixturePath!,
            ProductName = "M14RuntimePlayer",
            BuildMethod = "M14_PlayerBuild.Build",
            SessionDirEnvVar = "M14_SESSION_DIR",
            ReadyTimeoutSeconds = 30,
            CommandTimeoutSeconds = 30,
            PlayerTimeoutSeconds = 60,
            BuildTimeoutSeconds = 180,
        };

        // Minimal assignment with one RuntimeRequired requirement
        var assignment = new AssignmentDefinition
        {
            Id = "maze-click-test",
            Name = "Maze Click Test",
            Description = "One runtime requirement",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "maze-click-runtime",
                    Name = "Click MainMenuButton → TargetScene",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.mainmenu.exists",
                            Name = "MainMenu exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                            Severity = "Error",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Click MainMenuButton → TargetScene",
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
                    },
                },
            },
        };

        // Context with the real MainMenu scene — SceneExists will Pass
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = FixturePath!,
                Scenes = new List<SceneInfo>
                {
                    new() { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var ruleEngine = new RuleEngine();
        var runtimeRunner = new RuntimeRunner();
        var workflowRunner = new InspectionWorkflowRunner(ruleEngine, runtimeRunner);

        var result = await workflowRunner.RunAsync(assignment, context, options);

        Assert.NotNull(result);
        Assert.Single(result.RequirementResults);

        var reqResult = result.RequirementResults[0];
        Assert.NotNull(reqResult.CompositeResult);
        Assert.Equal("maze-click-runtime", reqResult.CompositeResult!.RuleId);
        Assert.Equal(EvidenceRequirement.RuntimeRequired, reqResult.CompositeResult.Requirement);

        // Static SceneExists(MainMenu) → Passed (scene exists in the real fixture)
        // RuntimeRequired + StaticPassed + RuntimePassed → Passed (per merge rules)
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
    }

    /// <summary>
    /// Runtime failure scenario: Wrong scene assertion.
    /// Real Player → click → scene transition → wrong expected scene → Failed.
    /// </summary>
    [Fact]
    public async Task M16_Assignment_WrongSceneAssertion_Fails()
    {
        AssertPrerequisites();

        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = FixturePath!,
            ProductName = "M14RuntimePlayer",
            BuildMethod = "M14_PlayerBuild.Build",
            SessionDirEnvVar = "M14_SESSION_DIR",
            ReadyTimeoutSeconds = 30,
            CommandTimeoutSeconds = 30,
            PlayerTimeoutSeconds = 60,
            BuildTimeoutSeconds = 180,
        };

        var assignment = new AssignmentDefinition
        {
            Id = "maze-fail-test",
            Name = "Maze Fail Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "maze-fail-runtime",
                    Name = "Expect wrong scene",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.mainmenu",
                            Name = "MainMenu exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Wrong scene assertion",
                        Actions = new List<RuntimeAction>
                        {
                            new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                            new ClickButtonAction { ActionId = "click", GameObjectName = "MainMenuButton" },
                            new WaitAction { ActionId = "wait", Milliseconds = 1000 },
                            new ObserveActiveSceneAction { ActionId = "observe" },
                        },
                        Assertions = new List<RuntimeAssertion>
                        {
                            new AssertActiveScene { AssertionId = "assert_wrong", ExpectedSceneName = "WrongMaze" },
                        },
                    },
                },
            },
        };

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = FixturePath!,
                Scenes = new List<SceneInfo>(),
            },
        };

        var ruleEngine = new RuleEngine();
        var runtimeRunner = new RuntimeRunner();
        var workflowRunner = new InspectionWorkflowRunner(ruleEngine, runtimeRunner);

        var result = await workflowRunner.RunAsync(assignment, context, options);

        Assert.NotNull(result);

        // Runtime assertion fails → Requirement Failed → Assignment Failed
        // (Even though static is NotEvaluated, runtime explicit failure takes priority)
        var reqResult = result.RequirementResults[0];
        Assert.Equal(RuleStatus.Failed, reqResult.Status);

        // Assignment aggregation: any Failed → Failed
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
    }

    /// <summary>
    /// StaticOnly requirement — no Unity launch needed.
    /// </summary>
    [Fact]
    public async Task M16_Assignment_StaticOnly_DoesNotLaunchUnity()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "static-test",
            Name = "Static Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "static-req",
                    Name = "Static Req",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.exists",
                            Name = "Scene exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                },
            },
        };

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new() { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var ruleEngine = new RuleEngine();
        var workflowRunner = new InspectionWorkflowRunner(ruleEngine, new NeverCalledRuntimeRunner());
        var result = await workflowRunner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
    }

    private static void AssertPrerequisites()
    {
        Assert.True(File.Exists(UnityExecutable),
            $"Unity Editor not found at {UnityExecutable}.");
        Assert.NotNull(FixturePath);
        Assert.True(Directory.Exists(FixturePath),
            $"M14 fixture not found at {FixturePath}.");
    }
}