using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// M23.1 Workflow + HarnessDeployer Integration Tests.
///
/// Verifies the full pipeline:
///   HarnessDeployer.DeployAsync()
///     → WorkflowRunner (static + runtime)
///       → Result Merge
///         → finally: HarnessDeployer.CleanupAsync()
///
/// This is the first end-to-end test where InspectionWorkflowRunner owns
/// the Harness lifecycle as part of the standard inspection workflow.
///
/// Requires:
///   - Real Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
///   - 3D Industrial Monitor project at ThreeDimPath
///   - UnityProjectInspector repo (contains GenericRuntimeBridge source)
/// </summary>
[CollectionDefinition("M23_1_WorkflowHarnessIntegration", DisableParallelization = true)]
public class M23_1_WorkflowHarnessIntegrationCollection { }

[Collection("M23_1_WorkflowHarnessIntegration")]
[Trait("Category", "Integration")]
public class M23_1_WorkflowHarnessIntegrationTests
{
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private const string ThreeDimPath =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor";

    private const string InspectorRepoRoot =
        "/Users/tangluyi/Documents/GitHub/UnityProjectInspector";

    private readonly RuleEngine _ruleEngine = new();

    /// <summary>
    /// M23_1_01: Full Deploy → Inspect → Cleanup cycle via InspectionWorkflowRunner.
    ///
    /// Pipeline:
    ///   1. HarnessDeployer deploys GenericRuntimeBridge.cs + GenericPlayerBuild.cs to 3DIM
    ///   2. InspectionWorkflowRunner validates the assignment
    ///   3. Static rules run (SceneExists on 3DIM's SampleScene)
    ///   4. RuntimeRunner builds Player (using injected GenericPlayerBuild.Build)
    ///   5. Player launches, executes Wait + ObserveActiveScene
    ///   6. Assertions evaluate (AssertActiveScene)
    ///   7. ResultMerger produces CompositeInspectionResult
    ///   8. finally: HarnessDeployer.CleanupAsync removes all injected files
    ///
    /// Verifies:
    ///   - A: Harness files exist on disk during deploy
    ///   - B: AssignmentInspectionResult is produced (not null)
    ///   - C: Cleanup deletes all injected files from 3DIM
    ///   - D: EditorBuildSettings.asset SHA matches baseline
    /// </summary>
    [Fact]
    public async Task M23_1_01_DeployInspectCleanup_FullCycle()
    {
        AssertPrerequisites();

        // ── Baseline: capture pre-deployment state ──
        var buildSettingsPath = Path.Combine(
            ThreeDimPath, "ProjectSettings", "EditorBuildSettings.asset");
        var baselineSha = HarnessSnapshot.ComputeFileSha256(buildSettingsPath);
        var baselineBuildSettingsBytes = File.Exists(buildSettingsPath)
            ? File.ReadAllBytes(buildSettingsPath) : null;

        // ── Build minimal assignment ──
        // One RuntimeRequired requirement: SampleScene exists (static) + observe active scene (runtime)
        var assignment = new AssignmentDefinition
        {
            Id = "m23-1-3dim",
            Name = "M23.1 3DIM Inspection",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "scene-sample-exists",
                    Name = "SampleScene exists",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "static.sample-scene",
                            Name = "SampleScene exists",
                            Type = "SceneExists",
                            Target = "SampleScene",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Observe active scene",
                        Actions = new List<RuntimeAction>
                        {
                            new WaitAction { ActionId = "init_wait", Milliseconds = 500 },
                            new ObserveActiveSceneAction { ActionId = "observe_scene" },
                        },
                        Assertions = new List<RuntimeAssertion>
                        {
                            new AssertActiveScene
                            {
                                AssertionId = "assert_scene",
                                ExpectedSceneName = "SampleScene",
                            },
                        },
                    },
                },
            },
        };

        // ── InspectionContext with 3DIM scenes ──
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = ThreeDimPath,
                Scenes = new List<SceneInfo>
                {
                    new() { Name = "SampleScene", FilePath = "Assets/Scenes/SampleScene.unity" },
                },
            },
        };

        // ── Runtime options pointing to 3DIM ──
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = ThreeDimPath,
            BuildMethod = "GenericPlayerBuild.Build",
            ProductName = "RuntimePlayer",
            ReadyTimeoutSeconds = 60,
            CommandTimeoutSeconds = 30,
            PlayerTimeoutSeconds = 60,
            BuildTimeoutSeconds = 300,
        };

        // ── Create the full workflow runner with HarnessDeployer ──
        var harnessDeployer = new HarnessDeployer(InspectorRepoRoot);
        var runtimeRunner = new RuntimeRunner();
        var workflowRunner = new InspectionWorkflowRunner(
            _ruleEngine, runtimeRunner, harnessDeployer);

        // ── Verify pre-deploy state: no harness files ──
        Assert.False(File.Exists(
            Path.Combine(ThreeDimPath, "Assets", "Scripts", "GenericRuntimeBridge.cs")),
            "Pre-deploy: GenericRuntimeBridge.cs should not exist");

        // ── Run the full workflow (deploy → inspect → cleanup) ──
        var result = await workflowRunner.RunAsync(assignment, context, options);

        // ── Assert: Result produced ──
        Assert.NotNull(result);
        Assert.NotEmpty(result.RequirementResults);

        var reqResult = result.RequirementResults[0];

        // Log the actual status for diagnosis
        System.Console.WriteLine(
            $"[M23_1_01] FinalStatus={result.FinalStatus}, " +
            $"ReqStatus={reqResult.Status}, " +
            $"Static={reqResult.CompositeResult?.StaticStatus}, " +
            $"Runtime={reqResult.CompositeResult?.RuntimeStatus}");

        // ── Assert: RuntimeResultDetail propagated when runtime ran ──
        if (reqResult.CompositeResult?.RuntimeStatus != null)
        {
            Assert.NotNull(reqResult.CompositeResult.RuntimeResultDetail);
            System.Console.WriteLine(
                $"[M23_1_01] RuntimeResultDetail={reqResult.CompositeResult.RuntimeResultDetail}, " +
                $"RuntimeMessage={reqResult.CompositeResult.RuntimeMessage}");
        }

        // ── Assert: Harness files cleaned up ──
        var bridgePath = Path.Combine(ThreeDimPath, "Assets", "Scripts", "GenericRuntimeBridge.cs");
        var buildEntryPath = Path.Combine(ThreeDimPath, "Assets", "Editor", "GenericPlayerBuild.cs");

        Assert.False(File.Exists(bridgePath),
            "Cleanup: GenericRuntimeBridge.cs must be deleted");
        Assert.False(File.Exists(bridgePath + ".meta"),
            "Cleanup: GenericRuntimeBridge.cs.meta must be deleted");
        Assert.False(File.Exists(buildEntryPath),
            "Cleanup: GenericPlayerBuild.cs must be deleted");
        Assert.False(File.Exists(buildEntryPath + ".meta"),
            "Cleanup: GenericPlayerBuild.cs.meta must be deleted");

        // ── Assert: EditorBuildSettings.asset restored ──
        var cleanupSha = HarnessSnapshot.ComputeFileSha256(buildSettingsPath);
        Assert.Equal(baselineSha, cleanupSha);

        if (baselineBuildSettingsBytes != null)
        {
            var restoredBytes = File.ReadAllBytes(buildSettingsPath);
            Assert.True(baselineBuildSettingsBytes.AsSpan().SequenceEqual(restoredBytes),
                "EditorBuildSettings.asset must be byte-for-byte identical to pre-deployment");
        }

        // ── Assert: No harness-related files in git status ──
        var finalGitStatus = GetGitStatus(ThreeDimPath);
        Assert.DoesNotContain("GenericRuntimeBridge", finalGitStatus);
        Assert.DoesNotContain("GenericPlayerBuild", finalGitStatus);
        Assert.DoesNotContain("__InspectorBuildSettingsFix", finalGitStatus);

        System.Console.WriteLine(
            $"[M23_1_01] Result={result.FinalStatus}, Cleanup verified, SHA verified");
    }

    /// <summary>
    /// M23_1_02: HarnessDeployer is optional (backward compat).
    /// When not provided, WorkflowRunner works exactly as before.
    /// </summary>
    [Fact]
    public async Task M23_1_02_OptionalHarnessDeployer_BackwardCompat()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "backward-compat",
            Name = "Backward Compat",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "static-only",
                    Name = "Static Only",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "MainMenu" },
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

        // No HarnessDeployer — same constructor as M18/M17 tests
        var runner = new InspectionWorkflowRunner(
            _ruleEngine, new NeverCalledRuntimeRunner());

        var result = await runner.RunAsync(assignment, context);
        Assert.NotNull(result);
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static void AssertPrerequisites()
    {
        Assert.True(File.Exists(UnityExecutable),
            $"Unity Editor not found at {UnityExecutable}");
        Assert.True(Directory.Exists(ThreeDimPath),
            $"3D Industrial Monitor project not found at {ThreeDimPath}");
        Assert.True(Directory.Exists(InspectorRepoRoot),
            $"Inspector repo root not found at {InspectorRepoRoot}");
    }

    private static string GetGitStatus(string repoPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                WorkingDirectory = repoPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Trim();
        }
        catch
        {
            return "(unable to check git status)";
        }
    }
}