using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// M21 Generic Unity Runtime Harness Integration Tests.
///
/// These tests exercise the full end-to-end pipeline using the GENERIC harness:
///   Unity Editor → Player Build → File-based IPC → GenericRuntimeBridge → Actions
///   → Assertions → RuntimeResultStatus
///
/// Cross-fixture validation:
///   Same RuntimeRunner drives both M14 and Generic fixture tests.
///   M14 tests use explicit M14_SESSION_DIR/M14RuntimePlayer/M14_PlayerBuild.
///   Generic tests use NEW defaults (UNITY_INSPECTOR_SESSION_DIR/RuntimePlayer).
///
/// Requires:
///   - Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
///   - GenericUnityRuntimeHarness with GenericRuntimeBridge + GenericPlayerBuild
///   - Project set up and Player built (Build/RuntimePlayer.app exists)
/// </summary>
[CollectionDefinition("GenericHarnessIntegration", DisableParallelization = true)]
public class GenericHarnessIntegrationCollection { }

[Collection("GenericHarnessIntegration")]
[Trait("Category", "Integration")]
public class M21GenericHarnessIntegrationTests
{
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private static readonly string? ProjectPath;

    static M21GenericHarnessIntegrationTests()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..", ".."));

        var candidate = Path.Combine(
            repoRoot, "experiments", "GenericUnityRuntimeHarness");

        ProjectPath = Directory.Exists(candidate) ? candidate : null;
    }

    private static RuntimeRunOptions DefaultOptions() => new()
    {
        UnityExecutable = UnityExecutable,
        ProjectPath = ProjectPath!,
        ProductName = "RuntimePlayer",
        BuildMethod = "GenericPlayerBuild.Build",
        SessionDirEnvVar = "UNITY_INSPECTOR_SESSION_DIR",
        ReadyTimeoutSeconds = 30,
        CommandTimeoutSeconds = 30,
        PlayerTimeoutSeconds = 60,
        BuildTimeoutSeconds = 180,
    };

    /// <summary>
    /// M21.1: Generic Harness builds, launches, writes ready marker, sends Quit,
    /// and produces final evidence.
    /// </summary>
    [Fact]
    public async Task M21_01_HarnessBuildsAndQuitsWithEvidence()
    {
        AssertPrerequisites();

        var runner = new RuntimeRunner();
        var script = new RuntimeTestScript
        {
            Name = "Smoke test: init → quit",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
            },
            Assertions = new List<RuntimeAssertion>(),
        };

        var session = await runner.RunAsync(DefaultOptions(), script);

        Assert.NotNull(session);
        Assert.NotEmpty(session.Evidence);

        // Expect at least one evidence with an observed scene name
        var sceneEvidence = session.Evidence.FirstOrDefault(e => e.Type == "Action:ObserveActiveScene"
            || e.Type.Contains("RuntimeEvidence"));
        Assert.NotNull(sceneEvidence);

        // Session diagnostics
        Assert.NotNull(session.Message);
    }

    /// <summary>
    /// M21.2: Click StartButton → transitions to TargetScene, assertion passes.
    /// Full Runtime Flow with Wait → ClickButton(StartButton) → ObserveActiveScene → Quit.
    /// </summary>
    [Fact]
    public async Task M21_02_ClickStartButton_TransitionsToTargetScene()
    {
        AssertPrerequisites();

        var script = new RuntimeTestScript
        {
            Name = "Click StartButton → Load TargetScene",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                new ClickButtonAction { ActionId = "click_button", GameObjectName = "StartButton" },
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
        var session = await runner.RunAsync(DefaultOptions(), script);

        Assert.Equal(RuntimeResultStatus.Passed, session.Result);
        Assert.Contains("passed", (session.Message ?? "").ToLowerInvariant());
    }

    /// <summary>
    /// M21.3: Click nonexistent button → Runtime Failed (action failure).
    /// </summary>
    [Fact]
    public async Task M21_03_ClickInvalidButton_Fails()
    {
        AssertPrerequisites();

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
                new AssertActiveScene { AssertionId = "assert_scene", ExpectedSceneName = "MainScene" },
                new AssertNoExceptions { AssertionId = "assert_no_ex" },
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(DefaultOptions(), script);

        var clickEvidence = session.Evidence.FirstOrDefault(e => e.Type == "Action:ClickButton");
        Assert.NotNull(clickEvidence);
        Assert.False(clickEvidence.Obtained, "ClickButton action should have Obtained=false (failed)");
        Assert.Contains("not found", (clickEvidence.Message ?? ""), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// M21.4: Wrong scene assertion → Assertion Failed
    /// (expect WrongScene, get TargetScene).
    /// </summary>
    [Fact]
    public async Task M21_04_WrongSceneAssertion_Fails()
    {
        AssertPrerequisites();

        var script = new RuntimeTestScript
        {
            Name = "Wrong scene assertion test",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                new ClickButtonAction { ActionId = "click_button", GameObjectName = "StartButton" },
                new WaitAction { ActionId = "scene_wait", Milliseconds = 1000 },
                new ObserveActiveSceneAction { ActionId = "observe_scene" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                new AssertActiveScene { AssertionId = "assert_wrong", ExpectedSceneName = "WrongScene" },
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(DefaultOptions(), script);

        Assert.Equal(RuntimeResultStatus.Failed, session.Result);
    }

    /// <summary>
    /// M21.5: Expired command timeout → timeout evidence produced.
    /// </summary>
    [Fact]
    public async Task M21_05_ExpiredCommandTimeout_ReturnsTimeout()
    {
        AssertPrerequisites();

        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = ProjectPath!,
            ProductName = "RuntimePlayer",
            BuildMethod = "GenericPlayerBuild.Build",
            SessionDirEnvVar = "UNITY_INSPECTOR_SESSION_DIR",
            ReadyTimeoutSeconds = 30,
            CommandTimeoutSeconds = 1, // Very short — Wait 3000ms command times out
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
            Assertions = new List<RuntimeAssertion>(),
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(options, script);

        var timedOutEvidence = session.Evidence.Any(e =>
            e.Type.Contains("ActionTimeout") || (e.Message != null && e.Message.Contains("timeout")));
        Assert.True(timedOutEvidence,
            $"Expected timeout evidence. Types: {string.Join(", ", session.Evidence.Select(e => e.Type))}");
    }

    /// <summary>
    /// M21.6: Full Runtime Flow with all action types — ReadLogs, ObserveActiveScene,
    /// ClickButton. Verifies all actions work in sequence.
    /// </summary>
    [Fact]
    public async Task M21_06_FullRuntimeFlow_AllActionsPass()
    {
        AssertPrerequisites();

        var script = new RuntimeTestScript
        {
            Name = "Full Runtime Flow",
            Actions = new List<RuntimeAction>
            {
                new ReadLogsAction { ActionId = "logs_before" },
                new WaitAction { ActionId = "init_wait", Milliseconds = 200 },
                new ClickButtonAction { ActionId = "click_start", GameObjectName = "StartButton" },
                new WaitAction { ActionId = "scene_wait", Milliseconds = 1000 },
                new ObserveActiveSceneAction { ActionId = "observe_target" },
                new ReadLogsAction { ActionId = "logs_after" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                new AssertActiveScene { AssertionId = "assert_target", ExpectedSceneName = "TargetScene" },
                new AssertNoExceptions { AssertionId = "assert_no_errors" },
            },
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(DefaultOptions(), script);

        Assert.Equal(RuntimeResultStatus.Passed, session.Result);
        Assert.True(session.Evidence.Count >= 4,
            $"Expected ≥4 evidence items, got {session.Evidence.Count}");
    }

    /// <summary>
    /// M21.7: Process cleanup — Dispose removes the session directory.
    /// </summary>
    [Fact]
    public async Task M21_07_ProcessCleanup_DisposeKillsPlayer()
    {
        AssertPrerequisites();

        var runner = new RuntimeRunner() as ISupportsSessionSharing;
        Assert.NotNull(runner);

        using var scope = await runner.CreateSessionAsync(DefaultOptions());
        Assert.NotNull(scope);

        var sessionDir = scope.SessionDir;
        Assert.True(Directory.Exists(sessionDir), "Session directory should exist after CreateSessionAsync");

        // Wait briefly for the Player to be ready
        await Task.Delay(500);

        // Dispose — kill process + clean up session dir
        scope.Dispose();

        // Session directory should be cleaned up
        Assert.False(Directory.Exists(sessionDir),
            "Session directory should be deleted after Dispose");
    }

    private static void AssertPrerequisites()
    {
        Assert.True(File.Exists(UnityExecutable),
            $"Unity Editor not found at {UnityExecutable}. " +
            $"This integration test requires Unity 2022.3.62f3c1 installed.");

        Assert.NotNull(ProjectPath);
        Assert.True(Directory.Exists(ProjectPath),
            $"Generic Harness project not found. Requires experiments/GenericUnityRuntimeHarness.");
    }
}