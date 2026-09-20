using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// M14 Runtime Action Pipeline Integration Tests.
///
/// These tests exercise the full end-to-end pipeline:
///   Real Unity Editor → Real Player Build → Real Standalone Player
///   → File-based IPC → Action Pipeline (Wait, ClickButton, ObserveActiveScene, ReadLogs)
///   → Assertions → RuntimeResultStatus
///
/// Requires:
///   - Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
///   - M14_RuntimeActionFixture in the repo's experiments/ directory
///   - Project must have been set up (M14_Setup.InitializeScenes)
///     and Player built (or cached from a previous build)
/// </summary>
[CollectionDefinition("M14RuntimeIntegration", DisableParallelization = true)]
public class M14RuntimeIntegrationCollection { }

[Collection("M14RuntimeIntegration")]
[Trait("Category", "Integration")]
public class M14RuntimeIntegrationTests
{
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private static readonly string? ProjectPath;

    static M14RuntimeIntegrationTests()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..", ".."));

        var candidate = Path.Combine(
            repoRoot, "experiments", "M14_RuntimeActionFixture");

        ProjectPath = Directory.Exists(candidate) ? candidate : null;
    }

    [Fact]
    public async Task RunAsync_M14Project_ClickButton_TransitionsToTargetScene()
    {
        AssertPrerequisites();

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
            Name = "Click MainMenuButton → Load TargetScene",
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

        Assert.Equal(RuntimeResultStatus.Passed, session.Result);
        Assert.Contains("passed", (session.Message ?? "").ToLowerInvariant());
    }

    [Fact]
    public async Task RunAsync_M14Project_ClickInvalidButton_Fails()
    {
        AssertPrerequisites();

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
            Name = "Click nonexistent button",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                new ClickButtonAction { ActionId = "click_invalid", GameObjectName = "DoesNotExist" },
                new ObserveActiveSceneAction { ActionId = "observe_scene" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                new AssertActiveScene { AssertionId = "assert_scene", ExpectedSceneName = "MainMenu" },
                new AssertNoExceptions { AssertionId = "assert_no_ex" },
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(options, script);

        // Button click failed, but scene stays MainMenu and no exceptions.
        // Evidence must show the click action failure.
        var clickEvidence = session.Evidence.FirstOrDefault(e => e.Type == "Action:ClickButton");
        Assert.NotNull(clickEvidence);
        Assert.False(clickEvidence.Obtained, "ClickButton action should have Obtained=false (failed)");
        Assert.Contains("not found", (clickEvidence.Message ?? ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_M14Project_WrongSceneAssertion_Fails()
    {
        AssertPrerequisites();

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
            Name = "Wrong scene assertion test",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                new ClickButtonAction { ActionId = "click_button", GameObjectName = "MainMenuButton" },
                new WaitAction { ActionId = "scene_wait", Milliseconds = 1000 },
                new ObserveActiveSceneAction { ActionId = "observe_scene" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                // Expect wrong scene name
                new AssertActiveScene { AssertionId = "assert_wrong", ExpectedSceneName = "WrongScene" },
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(options, script);

        Assert.Equal(RuntimeResultStatus.Failed, session.Result);
    }

    [Fact]
    public async Task RunAsync_M14Project_ExpiredCommandTimeout_ReturnsTimeout()
    {
        AssertPrerequisites();

        // Use extremely short CommandTimeoutSeconds to force a command timeout
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = ProjectPath!,
            ProductName = "M14RuntimePlayer",
            BuildMethod = "M14_PlayerBuild.Build",
            SessionDirEnvVar = "M14_SESSION_DIR",
            ReadyTimeoutSeconds = 30,
            CommandTimeoutSeconds = 1, // Very short — the Wait 3000ms command will time out
            PlayerTimeoutSeconds = 60,
            BuildTimeoutSeconds = 180,
        };

        var script = new RuntimeTestScript
        {
            Name = "Command timeout test",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "long_wait", Milliseconds = 3000 },
                new ObserveActiveSceneAction { ActionId = "observe" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                // No assertions — testing that the pipeline survives a timeout
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(options, script);

        // The Wait command should time out, causing some evidence action to show a timeout pattern
        // The overall result will likely be NotEvaluated or a mix
        var timedOutEvidence = session.Evidence.Any(e =>
            e.Type.Contains("ActionTimeout") || (e.Message != null && e.Message.Contains("timeout")));
        Assert.True(timedOutEvidence,
            $"Expected timeout evidence in session. Evidence types: {string.Join(", ", session.Evidence.Select(e => e.Type))}");
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