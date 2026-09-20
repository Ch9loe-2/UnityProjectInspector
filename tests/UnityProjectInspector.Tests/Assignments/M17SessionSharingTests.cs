using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// M17 Runtime Session Sharing tests.
///
/// Core assertion: multiple RuntimeRequired requirements in one Assignment
/// should share a single RuntimeSessionScope (1 Build, 1 Launch, 1 Cleanup).
///
/// A SessionCountingRunner tracks create/execute/finalize/dispose calls
/// to verify session lifecycle is managed at the Assignment level.
/// </summary>
[Trait("Category", "Unit")]
public class M17SessionSharingTests
{
    private readonly RuleEngine _ruleEngine;
    private readonly InspectionContext _contextWithMainMenu;

    public M17SessionSharingTests()
    {
        _ruleEngine = new RuleEngine();
        _contextWithMainMenu = new InspectionContext
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
    }

    private static RuntimeRunOptions DefaultOptions => new()
    {
        UnityExecutable = "/fake/unity",
        ProjectPath = "/fake/project",
    };

    private static RuntimeTestScript SimpleRuntimeTest(string name) => new()
    {
        Name = name,
        Actions = new() { new WaitAction { ActionId = "w1", Milliseconds = 1 } },
        Assertions = new(),
    };

    private static RequirementDefinition StaticReq(string id) => new()
    {
        Id = id,
        Name = id,
        EvidenceRequirement = "StaticOnly",
        StaticRules = new()
        {
            new()
            {
                Id = $"{id}.scene",
                Name = $"{id} scene",
                Type = "SceneExists",
                Target = "MainMenu",
            },
        },
    };

    private static RequirementDefinition RuntimeReq(string id, RuntimeTestScript? test = null) => new()
    {
        Id = id,
        Name = id,
        EvidenceRequirement = "RuntimeRequired",
        StaticRules = new()
        {
            new()
            {
                Id = $"{id}.scene",
                Name = $"{id} scene",
                Type = "SceneExists",
                Target = "MainMenu",
            },
        },
        RuntimeTest = test ?? SimpleRuntimeTest($"{id}_runtime"),
    };

    // ══════════════════════════════════════════════════════════════
    // Test 1: Two Runtime Requirements Share Session
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TwoRuntimeRequirements_ShareOneSession()
    {
        var countingRunner = new SessionCountingRunner(RuntimeResultStatus.Passed);
        var runner = new InspectionWorkflowRunner(_ruleEngine, countingRunner);

        var assignment = new AssignmentDefinition
        {
            Id = "shared-test",
            Name = "Shared Session Test",
            Requirements = new List<RequirementDefinition>
            {
                RuntimeReq("req-a"),
                RuntimeReq("req-b"),
            },
        };

        var result = await runner.RunAsync(assignment, _contextWithMainMenu, DefaultOptions);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Equal(2, result.RequirementResults.Count);

        // Core M17 assertion: 1 Build, 1 Launch, 1 Cleanup
        Assert.Equal(1, countingRunner.CreateCount);
        Assert.Equal(1, countingRunner.FinalizeCount);
        Assert.Equal(1, countingRunner.DisposeCount);

        // Two requirements → two ExecuteScript calls
        Assert.Equal(2, countingRunner.ExecuteCount);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 2: StaticOnly Does Not Create Session
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task StaticOnly_CreatesNoSession()
    {
        var countingRunner = new SessionCountingRunner(RuntimeResultStatus.Passed);
        var runner = new InspectionWorkflowRunner(_ruleEngine, countingRunner);

        var assignment = new AssignmentDefinition
        {
            Id = "static-only",
            Name = "Static Only",
            Requirements = new List<RequirementDefinition>
            {
                StaticReq("req-a"),
                StaticReq("req-b"),
            },
        };

        var result = await runner.RunAsync(assignment, _contextWithMainMenu);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Equal(0, countingRunner.CreateCount);
        Assert.Equal(0, countingRunner.ExecuteCount);
        Assert.Equal(0, countingRunner.FinalizeCount);
        Assert.Equal(0, countingRunner.DisposeCount);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 3: Mixed Static + Runtime
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedStaticAndRuntime_OneSession()
    {
        var countingRunner = new SessionCountingRunner(RuntimeResultStatus.Passed);
        var runner = new InspectionWorkflowRunner(_ruleEngine, countingRunner);

        var assignment = new AssignmentDefinition
        {
            Id = "mixed-test",
            Name = "Mixed Static + Runtime",
            Requirements = new List<RequirementDefinition>
            {
                StaticReq("static-a"),
                RuntimeReq("runtime-a"),
                StaticReq("static-b"),
                RuntimeReq("runtime-b"),
            },
        };

        var result = await runner.RunAsync(assignment, _contextWithMainMenu, DefaultOptions);

        // Both static reqs pass, both runtime reqs pass → all Passed
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);

        // Core M17 assertion: 1 session for 2 runtime reqs
        Assert.Equal(1, countingRunner.CreateCount);
        Assert.Equal(1, countingRunner.FinalizeCount);
        Assert.Equal(1, countingRunner.DisposeCount);

        // Static reqs don't call ExecuteScript
        Assert.Equal(2, countingRunner.ExecuteCount);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 4: Requirement Failure Isolation — Assertion Failed ≠ Session Dead
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RequirementFailure_DoesNotKillSession()
    {
        // First script returns Failed (assertion failure), second returns Passed
        var countingRunner = new SessionCountingRunner(RuntimeResultStatus.Passed)
        {
            PerCallResults = new() { RuntimeResultStatus.Failed, RuntimeResultStatus.Passed },
        };
        var runner = new InspectionWorkflowRunner(_ruleEngine, countingRunner);

        var assignment = new AssignmentDefinition
        {
            Id = "fail-isolation",
            Name = "Failure Isolation",
            Requirements = new List<RequirementDefinition>
            {
                RuntimeReq("req-a"),
                RuntimeReq("req-b"),
            },
        };

        var result = await runner.RunAsync(assignment, _contextWithMainMenu, DefaultOptions);

        // req-a → Failed (assertion failure, requirements should still have their status)
        // req-b → Passed
        // Assignment → Failed (one requirement failed)
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
        Assert.Equal(2, result.RequirementResults.Count);

        // Verify individual requirement statuses
        Assert.Equal(RuleStatus.Failed, result.RequirementResults[0].Status);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[1].Status);

        // Core M17: A's assertion failure does NOT kill the session
        // → B still runs on the same session
        // → only 1 Create, 1 Finalize, 1 Dispose
        Assert.Equal(2, countingRunner.ExecuteCount);
        Assert.Equal(1, countingRunner.FinalizeCount);
        Assert.Equal(1, countingRunner.DisposeCount);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 5: Session Failure Cascades — Session Dead = Subsequent NotEvaluated
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SessionFailure_CascadesToSubsequent()
    {
        // First execute returns ProcessExited (session crash), second shouldn't execute normally
        var countingRunner = new SessionCountingRunner(RuntimeResultStatus.Passed)
        {
            PerCallResults = new() { RuntimeResultStatus.ProcessExited, RuntimeResultStatus.Passed },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, countingRunner);

        var assignment = new AssignmentDefinition
        {
            Id = "session-fail-cascade",
            Name = "Session Failure Cascade",
            Requirements = new List<RequirementDefinition>
            {
                RuntimeReq("req-a"),
                RuntimeReq("req-b"),
            },
        };

        var result = await runner.RunAsync(assignment, _contextWithMainMenu, DefaultOptions);

        // req-a: ProcessExited → ConvertRuntimeStatus → NotEvaluated
        //        Since static passed, merge: Static Passed + Runtime NotEval
        //        → CompositeInspectionResult with RuntimeStatus=NotEvaluated
        //        → FinalStatus is from runtimeStatus (converted from ProcessExited) → NotEvaluated
        //        Wait — let me think more carefully.
        //        ConvertRuntimeStatus: ProcessExited → RuleStatus.NotEvaluated
        //        req-a Status = Composite.FinalStatus = NotEvaluated (since runtimeStatus=NotEvaluated, it's the only status)
        // req-b: SessionFailed → skip runtime → runtimeStatus remains null
        //        BuildRequirementResult: runtimeStatus=null, static=Passed
        //        → primaryStatic exists → Merge(evidReq, Passed, null, null)
        //        → Composite with StaticStatus=Passed, RuntimeStatus=null
        //        → reqStatus = composite.FinalStatus
        //        Hmm, need to check what Merge does with null runtimeStatus...
        //        Actually for req-b, runtimeStatus is null (never set), evidenceReq=RuntimeRequired
        //        The merge: when runtime is null but evidenceReq=RuntimeRequired... 
        //        Let me look at ResultMerger.Merge behavior.

        // After careful analysis:
        // req-a: static Passed → runtime ProcessExited → Convert → NotEvaluated
        //        Merge(RuntimeRequired, Passed, NotEvaluated, null) = needs checking
        // req-b: state.SessionFailed → runtime skipped → runtimeStatus stays null
        //        BuildRequirementResult with runtimeStatus=null
        //        primaryStatic exists → Merge(RuntimeRequired, Passed, null, null)
        //        depends on Merge behavior
        
        // The exact status depends on ResultMerger.Merge semantics.
        // We know the session DID NOT share execute for req-b because
        // SessionCountingRunner killed the process.
        // Let's verify: the first execute killed the mock process.
        // InspectionWorkflowRunner checks scope.IsUsable (PlayerProcess.HasExited).
        // After kill, HasExited=true → BUT the scope is NOT IsFailed (playerProcess != null).
        // Then: state.SessionFailed = true (set by EvaluateRequirementWithSessionAsync line 251)
        // req-b: sees state.SessionFailed=true → runtimeStatus = NotEvaluated (line 227)
        
        // So: req-b also has runtimeStatus=NotEvaluated (from SessionFailed path)
        // Merge(RuntimeRequired, Passed, NotEvaluated, null) = ?
        
        // All we need to verify is the counting:
        // A session failure at ProcessExited means:
        // - After req-a's Execute returns ProcessExited, the InspectionWorkflowRunner
        //   checks HasExited → kills → sets state.SessionFailed
        // - req-b's Evaluate sees SessionFailed → NotEvaluated
        // - Final: no Passed, at least one NotEvaluated → NotEvaluated

        Assert.Equal(RuleStatus.NotEvaluated, result.FinalStatus);

        // Verify per-requirement status
        Assert.Equal(2, result.RequirementResults.Count);

        // req-a had a runtime, and we explicitly set its result.
        // Since Session failed, the individual script was executed (ExecuteCount++)
        // but the ConvertRuntimeStatus maps ProcessExited → NotEvaluated
        Assert.Equal(RuleStatus.NotEvaluated, result.RequirementResults[0].Status);

        // req-b had SessionFailed → runtime not executed
        Assert.Equal(RuleStatus.NotEvaluated, result.RequirementResults[1].Status);

        // ExecuteCount: only req-a actually called ExecuteScriptOnScopeAsync.
        // req-a returned ProcessExited, which killed the mock process, and
        // state.SessionFailed was set to true.
        // req-b saw SessionFailed → skipped execution → runtimeStatus=NotEvaluated
        Assert.Equal(1, countingRunner.ExecuteCount);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 6: Cleanup After Completion
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cleanup_AfterAssignmentComplete()
    {
        var countingRunner = new SessionCountingRunner(RuntimeResultStatus.Passed);
        var runner = new InspectionWorkflowRunner(_ruleEngine, countingRunner);

        var assignment = new AssignmentDefinition
        {
            Id = "cleanup-test",
            Name = "Cleanup Test",
            Requirements = new List<RequirementDefinition>
            {
                RuntimeReq("req-a"),
            },
        };

        var result = await runner.RunAsync(assignment, _contextWithMainMenu, DefaultOptions);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);

        // Even with 1 requirement, we still have 1 session
        Assert.Equal(1, countingRunner.CreateCount);
        Assert.Equal(1, countingRunner.FinalizeCount);
        Assert.Equal(1, countingRunner.DisposeCount);
    }
}

/// <summary>
/// Mock IRuntimeRunner + ISupportsSessionSharing that tracks
/// session lifecycle call counts.
///
/// Used by M17 tests to verify that InspectionWorkflowRunner
/// creates one scope, executes multiple scripts against it,
/// finalizes once, and disposes once.
/// </summary>
public class SessionCountingRunner : IRuntimeRunner, ISupportsSessionSharing
{
    private readonly RuntimeResultStatus _defaultResult;

    /// <summary>Number of times CreateSessionAsync was called.</summary>
    public int CreateCount { get; private set; }

    /// <summary>Number of times ExecuteScriptOnScopeAsync was called.</summary>
    public int ExecuteCount { get; private set; }

    /// <summary>Number of times FinalizeScopeAsync was called.</summary>
    public int FinalizeCount { get; private set; }

    /// <summary>Number of times scope.Dispose was called.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// If set, overrides the result returned by ALL ExecuteScriptOnScopeAsync calls.
    /// Used to simulate session-level failures.
    /// </summary>
    public RuntimeResultStatus? ExecuteResultOverride { get; set; }

    /// <summary>
    /// Per-call result overrides, consumed in order. Each call consumes one entry.
    /// Once exhausted, falls back to ExecuteResultOverride or _defaultResult.
    /// Used to simulate per-requirement assertion failures (e.g. first fails, second passes).
    /// </summary>
    public List<RuntimeResultStatus>? PerCallResults { get; set; }

    public SessionCountingRunner(RuntimeResultStatus defaultResult)
    {
        _defaultResult = defaultResult;
    }

    // ── IRuntimeRunner ──────────────────────────────────────────

    public Task<RuntimeSession> RunAsync(RuntimeRunOptions options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            StartedAt = DateTime.UtcNow,
            Result = _defaultResult,
        });
    }

    public Task<RuntimeSession> RunAsync(RuntimeRunOptions options, RuntimeTestScript script, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            StartedAt = DateTime.UtcNow,
            Result = _defaultResult,
        });
    }

    // ── ISupportsSessionSharing ─────────────────────────────────

    public async Task<RuntimeSessionScope> CreateSessionAsync(RuntimeRunOptions options, CancellationToken cancellationToken = default)
    {
        CreateCount++;

        var session = new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            UnityVersion = "Mock",
            StartedAt = DateTime.UtcNow,
            Result = _defaultResult,
        };

        // Use a real Process object — /bin/sleep is clean and killable
        var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "/bin/sleep";
        process.StartInfo.Arguments = "30";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();

        var scope = new TrackedSessionScope(
            session,
            process,
            process.Id,
            "/tmp/mock-session",
            "/tmp/mock-session/commands",
            "/tmp/mock-session/results",
            "/tmp/mock-session/evidence.json",
            "/tmp/mock-session/done.marker",
            30, 100,
            () => DisposeCount++);

        return await Task.FromResult(scope);
    }

    public async Task<RuntimeSession> ExecuteScriptOnScopeAsync(RuntimeSessionScope scope, RuntimeTestScript script, CancellationToken cancellationToken = default)
    {
        ExecuteCount++;

        // Per-call override takes priority, consumed in order
        RuntimeResultStatus result;
        if (PerCallResults != null && PerCallResults.Count > 0)
        {
            result = PerCallResults[0];
            PerCallResults.RemoveAt(0);
        }
        else
        {
            result = ExecuteResultOverride ?? _defaultResult;
        }

        // If the result is a session-level failure (ProcessExited/Timeout),
        // simulate the process having exited so InspectionWorkflowRunner
        // will mark state.SessionFailed for subsequent requirements.
        if (result is RuntimeResultStatus.ProcessExited or RuntimeResultStatus.Timeout)
        {
            // Wait and set a flag to make scope.IsUsable return false
            // by actually killing the sleep process
            try
            {
                // Use kill -9 (SIGKILL) — cannot be ignored by process
                using var killer = new System.Diagnostics.Process();
                killer.StartInfo.FileName = "/bin/kill";
                killer.StartInfo.Arguments = $"-9 {scope.PlayerPid}";
                killer.StartInfo.UseShellExecute = false;
                killer.Start();
                killer.WaitForExit(2000);

                // Poll until process is tracked as exited (Refresh + HasExited)
                for (int retry = 0; retry < 20; retry++)
                {
                    scope.PlayerProcess.Refresh();
                    if (scope.PlayerProcess.HasExited) break;
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch { }
        }

        return await Task.FromResult(new RuntimeSession
        {
            ProjectPath = scope.Session.ProjectPath ?? "/tmp",
            UnityVersion = scope.Session.UnityVersion,
            StartedAt = DateTime.UtcNow,
            Result = result,
            Evidence = new List<RuntimeEvidence>(scope.Session.Evidence),
        });
    }

    public async Task FinalizeScopeAsync(RuntimeSessionScope scope, CancellationToken cancellationToken = default)
    {
        FinalizeCount++;
        scope.IsFinalized = true;
        await Task.CompletedTask;
    }
}

/// <summary>
/// A RuntimeSessionScope subclass that tracks when Dispose() is called.
/// </summary>
public class TrackedSessionScope : RuntimeSessionScope
{
    private readonly Action _onDispose;

    public TrackedSessionScope(
        RuntimeSession session,
        System.Diagnostics.Process playerProcess,
        int playerPid,
        string sessionDir,
        string commandsDir,
        string resultsDir,
        string evidenceFile,
        string doneMarker,
        int commandTimeoutSeconds,
        int pollIntervalMs,
        Action onDispose)
        : base(session, playerProcess, playerPid, sessionDir, commandsDir, resultsDir, evidenceFile, doneMarker, commandTimeoutSeconds, pollIntervalMs)
    {
        _onDispose = onDispose;
    }

    public override void Dispose()
    {
        _onDispose?.Invoke();
        base.Dispose();
    }
}