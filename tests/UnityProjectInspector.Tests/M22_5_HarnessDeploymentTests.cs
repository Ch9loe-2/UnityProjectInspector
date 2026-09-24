using System.Security.Cryptography;
using System.Text;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// M22.5 Harness Deployment Integration Tests.
///
/// Proves that HarnessDeployer can perform a complete
/// "deploy → compile → cleanup → restore" cycle on a real,
/// third-party Unity project that has NO pre-existing Harness,
/// and handles all error/failure modes safely.
///
/// Requires:
///   - A real Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
///   - 3D Industrial Monitor project at 3DIndustrialMonitorPath
///   - The UnityProjectInspector repo (contains GenericRuntimeBridge source)
///
/// Cleanup contract — Harness-owned changes are 100% restored:
///   - GenericRuntimeBridge.cs + .meta
///   - GenericPlayerBuild.cs + .meta
///   - __InspectorBuildSettingsFix.cs + .meta (if created)
///   - EditorBuildSettings.asset (if modified — SHA-256 verified restoration)
///
/// NOT responsible for restoring (outside M22.5 contract):
///   - Library/, Temp/, Build/ directories
///   - Unity auto-import metadata changes in ProjectSettings/*.asset
///   - Non-Harness file modifications in Assets/
///
/// Test cases (6 total):
///   M22_5_01: Full positive cycle on 3DIM, A/B/C independently verified, SHA-256 verified
///   M22_5_02: Invalid path -> DeploymentException, no dirty files
///   M22_5_03: Pre-existing GenericRuntimeBridge.cs -> no overwrite, original unchanged
///   M22_5_04: Pre-existing .meta only -> no overwrite, no injection, original unchanged
///   M22_5_05: Partial deployment rollback -> injected files cleaned, no leftover
///   M22_5_06: Pre-existing Build/ directory -> untouched after cleanup
/// </summary>
[CollectionDefinition("HarnessDeploymentTests", DisableParallelization = true)]
public class HarnessDeploymentTestCollection { }

[Collection("HarnessDeploymentTests")]
[Trait("Category", "Integration")]
public class M22_5_HarnessDeploymentTests : IDisposable
{
    // ─── Paths ───
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";
    private const string ThreeDimPath =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor";
    private const string InspectorRepoRoot =
        "/Users/tangluyi/Documents/GitHub/UnityProjectInspector";
    private readonly string _tempDir;

    public M22_5_HarnessDeploymentTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), $"M22_5_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 1: Positive — Full deploy -> compile -> cleanup on 3DIM
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// M22_5_01: Full positive cycle on 3D Industrial Monitor.
    ///
    /// Verifies:
    ///   - Deployment succeeds (no pre-existing conflicts)
    ///   - A = ScriptsCompiled: Unity compiles injected scripts
    ///   - B = MethodRecognized: GenericPlayerBuild.Build found and invoked
    ///   - C = PlayerBuildSucceeded: independently reported (not required for M22.5 success)
    ///   - Cleanup deletes all injected files + .meta
    ///   - SHA-256 of EditorBuildSettings.asset matches pre-deployment
    ///   - Byte-for-byte identical restoration
    ///   - No harness-related files in git status
    ///   - Pre-existing Build/ directory (if any) remains untouched
    ///
    /// A and B are REQUIRED for M22.5 deployment success.
    /// C is independently reported but NOT required — a third-party project may
    /// have player-build configuration issues unrelated to harness script correctness.
    /// </summary>
    [Fact]
    public async Task M22_5_01_Positive_DeployCompileCleanup3DIM()
    {
        AssertPrerequisites();

        // ── Baseline ──
        var buildSettingsPath = Path.Combine(
            ThreeDimPath, "ProjectSettings", "EditorBuildSettings.asset");
        var baselineSha = HarnessSnapshot.ComputeFileSha256(buildSettingsPath);
        var baselineBuildSettingsBytes = File.Exists(buildSettingsPath)
            ? File.ReadAllBytes(buildSettingsPath) : null;

        // Check pre-existing Build/ dir state
        var preExistingBuildDir = Path.Combine(ThreeDimPath, "Build");
        bool buildDirExistedBefore = Directory.Exists(preExistingBuildDir);
        string[]? buildDirFilesBefore = buildDirExistedBefore
            ? Directory.GetFiles(preExistingBuildDir, "*", SearchOption.AllDirectories)
            : null;

        // ── Deploy ──
        var deployer = new HarnessDeployer(InspectorRepoRoot);
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = ThreeDimPath,
            BuildMethod = "GenericPlayerBuild.Build",
            BuildTimeoutSeconds = 300,
        };

        HarnessSnapshot? snapshot = null;

        try
        {
            snapshot = await deployer.DeployAsync(options);

            // ── Assert deployment state ──
            Assert.NotNull(snapshot);
            Assert.True(snapshot.DeployCompletedSuccessfully);

            Assert.NotEmpty(snapshot.FilesInjectedByUs);
            Assert.Contains(snapshot.FilesInjectedByUs,
                f => f.EndsWith("GenericRuntimeBridge.cs", StringComparison.Ordinal));
            Assert.Contains(snapshot.FilesInjectedByUs,
                f => f.EndsWith("GenericPlayerBuild.cs", StringComparison.Ordinal));

            // 3DIM has SampleScene enabled so BuildSettings should NOT be modified
            Assert.False(snapshot.BuildSettingsModified,
                "3DIM has SampleScene enabled — BuildSettings should NOT be modified");

            // Files should actually exist on disk
            foreach (var f in snapshot.FilesInjectedByUs)
                Assert.True(File.Exists(f), $"Injected file should exist on disk: {f}");

            // EditorBuildSettings.asset unchanged (no fix needed)
            var postDeploySha = HarnessSnapshot.ComputeFileSha256(buildSettingsPath);
            Assert.Equal(baselineSha, postDeploySha);

            // ════════════════════════════════════════════════════════
            // Test A: Three independent compile states
            // ════════════════════════════════════════════════════════
            Assert.NotNull(deployer.LastCompileResult);

            // A = ScriptsCompiled: Unity scripts compiled successfully (no CS errors)
            Assert.True(deployer.LastCompileResult!.ScriptsCompiled,
                "A = ScriptsCompiled: Unity must compile injected scripts without errors");

            // B = MethodRecognized: GenericPlayerBuild.Build was found and invoked
            // Evidence: log contains "[GenericPlayerBuild]" (marker from method body)
            Assert.True(deployer.LastCompileResult.MethodRecognized,
                "B = MethodRecognized: GenericPlayerBuild.Build must be found and executed by Unity -executeMethod");

            // C = PlayerBuildSucceeded: independently reported, NOT required
            // 3DIM may fail player build for project-specific config reasons.
            // M22.5 contract only requires A AND B.
            var playerBuildSucceeded = deployer.LastCompileResult.PlayerBuildSucceeded;
            System.Console.WriteLine(
                $"[M22_5_01] A=ScriptsCompiled={deployer.LastCompileResult.ScriptsCompiled}, " +
                $"B=MethodRecognized={deployer.LastCompileResult.MethodRecognized}, " +
                $"C=PlayerBuildSucceeded={playerBuildSucceeded}, " +
                $"ExitCode={deployer.LastCompileResult.ExitCode}");
        }
        catch (Exception)
        {
            // If deploy failed, clean up before rethrowing
            if (snapshot != null)
                await deployer.CleanupAsync(snapshot, options);
            throw;
        }

        // ── Cleanup ──
        if (snapshot != null)
            await deployer.CleanupAsync(snapshot, options);

        // ── Assert restoration ──
        Assert.NotNull(snapshot);

        // 1. Injected files deleted
        foreach (var f in snapshot!.FilesInjectedByUs)
        {
            Assert.False(File.Exists(f),
                $"Injected file should be deleted after cleanup: {f}");
        }

        // 2. .meta files deleted
        foreach (var f in snapshot.FilesInjectedByUs)
        {
            Assert.False(File.Exists(f + ".meta"),
                $"Injected .meta should be deleted after cleanup: {f}.meta");
        }

        // 3. SHA-256 of EditorBuildSettings.asset matches baseline
        var cleanupSha = HarnessSnapshot.ComputeFileSha256(buildSettingsPath);
        Assert.Equal(baselineSha, cleanupSha);

        // 4. Byte-for-byte identical restoration
        if (baselineBuildSettingsBytes != null)
        {
            var restoredBytes = File.ReadAllBytes(buildSettingsPath);
            Assert.True(baselineBuildSettingsBytes.AsSpan().SequenceEqual(restoredBytes),
                "EditorBuildSettings.asset must be byte-for-byte identical to pre-deployment");
        }

        // 5. No harness-related files in git status
        // NOTE: Unity Editor import side-effects (e.g. URP-Balanced.asset changes)
        // are normal and outside M22.5 cleanup contract.
        var finalGitStatus = GetGitStatus(ThreeDimPath);
        Assert.DoesNotContain("GenericRuntimeBridge", finalGitStatus);
        Assert.DoesNotContain("GenericPlayerBuild", finalGitStatus);
        Assert.DoesNotContain("__InspectorBuildSettingsFix", finalGitStatus);

        // 6. (Test D) Pre-existing Build/ directory remains untouched
        if (buildDirExistedBefore)
        {
            Assert.True(Directory.Exists(preExistingBuildDir),
                "Pre-existing Build/ directory must still exist after cleanup");
            var buildDirFilesAfter = Directory.GetFiles(
                preExistingBuildDir, "*", SearchOption.AllDirectories);
            Assert.Equal(buildDirFilesBefore!.Length, buildDirFilesAfter.Length);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 2: Negative — Invalid project path
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// M22_5_02: Deploy to an invalid (non-existent) path must throw
    /// DeploymentException and leave no dirty files behind.
    /// </summary>
    [Fact]
    public async Task M22_5_02_Negative_InvalidPath_ThrowsDeploymentException()
    {
        var invalidPath = Path.Combine(_tempDir, "nonexistent_project");
        var deployer = new HarnessDeployer(InspectorRepoRoot);
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = invalidPath,
        };

        var ex = await Assert.ThrowsAsync<HarnessDeployer.DeploymentException>(
            () => deployer.DeployAsync(options));

        Assert.Contains("valid Unity project", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(invalidPath, ex.Message);

        // Verify no files were created
        Assert.False(Directory.Exists(invalidPath),
            "No directory should be created for invalid path");
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 3: Protection — Pre-existing GenericRuntimeBridge.cs
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// M22_5_03: If the target project already has a file at
    /// Assets/Scripts/GenericRuntimeBridge.cs, deployment must fail
    /// with DeploymentException. The pre-existing file must remain
    /// completely untouched (no deletion, no modification).
    ///
    /// Uses a temporary synthetic Unity project for isolation.
    /// </summary>
    [Fact]
    public async Task M22_5_03_Protection_PreExistingBridge_ThrowsNoOverwrite()
    {
        // ── Setup ──
        var synthProject = Path.Combine(_tempDir, "Synth_PreExisting_CS");
        CreateMinimalUnityProject(synthProject);

        var scriptsDir = Path.Combine(synthProject, "Assets", "Scripts");
        Directory.CreateDirectory(scriptsDir);
        var preExistingBridge = Path.Combine(scriptsDir, "GenericRuntimeBridge.cs");
        var preExistingContent = "// Pre-existing — should never be overwritten";
        File.WriteAllText(preExistingBridge, preExistingContent);

        var preDeploySha = HarnessSnapshot.ComputeFileSha256(preExistingBridge);

        // ── Act ──
        var deployer = new HarnessDeployer(InspectorRepoRoot);
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = synthProject,
        };

        var ex = await Assert.ThrowsAsync<HarnessDeployer.DeploymentException>(
            () => deployer.DeployAsync(options));

        Assert.Contains("already contains", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GenericRuntimeBridge", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The message says "will NOT overwrite" — checking "overwrite" word absence
        // would be fragile because "NOT overwrite" naturally contains "overwrite".
        // Instead verify the semantic: file is unchanged (checked below).

        // ── Verify original file completely untouched ──
        Assert.True(File.Exists(preExistingBridge),
            "Pre-existing GenericRuntimeBridge.cs must still exist");
        var postDeploySha = HarnessSnapshot.ComputeFileSha256(preExistingBridge);
        Assert.Equal(preDeploySha, postDeploySha);
        var actualContent = File.ReadAllText(preExistingBridge);
        Assert.Equal(preExistingContent, actualContent);

        // ── Verify no other injected files exist ──
        var injectedBuildEntry = Path.Combine(synthProject, "Assets", "Editor", "GenericPlayerBuild.cs");
        Assert.False(File.Exists(injectedBuildEntry),
            "GenericPlayerBuild.cs should NOT have been injected");
        Assert.False(File.Exists(injectedBuildEntry + ".meta"),
            "GenericPlayerBuild.cs.meta should NOT have been injected");
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 4 (Test B): .meta pre-existing conflict
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// M22_5_04: If only the .meta file exists (no .cs), deployment must fail.
    ///
    /// Scenario: GenericRuntimeBridge.cs.meta exists but GenericRuntimeBridge.cs does not.
    /// The .meta file must remain completely untouched (no deletion, no modification).
    /// The .cs file must NOT be injected.
    /// No other harness files must be injected.
    /// </summary>
    [Fact]
    public async Task M22_5_04_Protection_PreExistingMeta_ThrowsNoOverwrite()
    {
        // ── Setup ──
        var synthProject = Path.Combine(_tempDir, "Synth_PreExisting_Meta");
        CreateMinimalUnityProject(synthProject);

        // Create ONLY the .meta file — .cs does NOT exist
        var scriptsDir = Path.Combine(synthProject, "Assets", "Scripts");
        Directory.CreateDirectory(scriptsDir);
        var preExistingMeta = Path.Combine(scriptsDir, "GenericRuntimeBridge.cs.meta");
        var preExistingMetaContent = "guid: abcdef1234567890abcdef1234567890";
        File.WriteAllText(preExistingMeta, preExistingMetaContent);

        var preDeploySha = HarnessSnapshot.ComputeFileSha256(preExistingMeta);

        // ── Act ──
        var deployer = new HarnessDeployer(InspectorRepoRoot);
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = synthProject,
        };

        var ex = await Assert.ThrowsAsync<HarnessDeployer.DeploymentException>(
            () => deployer.DeployAsync(options));

        Assert.Contains("already contains", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".cs.meta", ex.Message, StringComparison.OrdinalIgnoreCase);

        // ── Verify .meta completely untouched ──
        Assert.True(File.Exists(preExistingMeta),
            "Pre-existing .meta must still exist");
        Assert.Equal(preExistingMetaContent, File.ReadAllText(preExistingMeta));
        var postDeploySha = HarnessSnapshot.ComputeFileSha256(preExistingMeta);
        Assert.Equal(preDeploySha, postDeploySha);

        // ── Verify .cs was NOT injected ──
        var csPath = Path.Combine(scriptsDir, "GenericRuntimeBridge.cs");
        Assert.False(File.Exists(csPath),
            "GenericRuntimeBridge.cs must NOT be injected when only .meta exists");

        // ── Verify no other files injected ──
        var buildEntryCs = Path.Combine(synthProject, "Assets", "Editor", "GenericPlayerBuild.cs");
        Assert.False(File.Exists(buildEntryCs));
        Assert.False(File.Exists(buildEntryCs + ".meta"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 5 (Test C): Deployment-stage failure after harness injection
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// M22_5_05: Deployment-stage failure after harness injection causes rollback.
    ///
    /// Scenario: Both GenericRuntimeBridge.cs and GenericPlayerBuild.cs are
    /// injected successfully. The subsequent Build Settings stage fails
    /// (no .unity files found). DeployAsync's catch block triggers rollback
    /// and deletes all injected files.
    ///
    /// This test does NOT need a real Unity Editor — the failure happens before
    /// Unity CLI is invoked.
    /// </summary>
    [Fact]
    public async Task M22_5_05_Negative_PartialDeploymentRollback()
    {
        // ── Setup ──
        var synthProject = Path.Combine(_tempDir, "Synth_Rollback");
        CreateMinimalUnityProject(synthProject);

        // Override EditorBuildSettings.asset with NO enabled scenes
        var buildSettingsPath = Path.Combine(
            synthProject, "ProjectSettings", "EditorBuildSettings.asset");
        File.WriteAllText(buildSettingsPath, """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
EditorBuildSettings:
  m_Scenes: []
""");

        // Delete the test scene so FixBuildSettingsViaEditorAPI cannot find .unity files
        var sceneDir = Path.Combine(synthProject, "Assets", "Scenes");
        foreach (var f in Directory.GetFiles(sceneDir, "*.unity"))
            File.Delete(f);

        // Ensure injection target directories exist
        Directory.CreateDirectory(Path.Combine(synthProject, "Assets", "Scripts"));
        Directory.CreateDirectory(Path.Combine(synthProject, "Assets", "Editor"));

        // ── Act ──
        var deployer = new HarnessDeployer(InspectorRepoRoot);
        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = synthProject,
        };

        var ex = await Assert.ThrowsAsync<HarnessDeployer.DeploymentException>(
            () => deployer.DeployAsync(options));

        // ── Assert: Rollback cleaned everything ──
        // GenericRuntimeBridge.cs must NOT exist (injected then rolled back)
        var bridgePath = Path.Combine(synthProject, "Assets", "Scripts", "GenericRuntimeBridge.cs");
        Assert.False(File.Exists(bridgePath),
            "GenericRuntimeBridge.cs must be rolled back after failed deployment");
        Assert.False(File.Exists(bridgePath + ".meta"),
            "GenericRuntimeBridge.cs.meta must be rolled back after failed deployment");

        // GenericPlayerBuild.cs must NOT exist (injected then rolled back)
        var buildEntryPath = Path.Combine(synthProject, "Assets", "Editor", "GenericPlayerBuild.cs");
        Assert.False(File.Exists(buildEntryPath),
            "GenericPlayerBuild.cs must be rolled back after failed deployment");
        Assert.False(File.Exists(buildEntryPath + ".meta"),
            "GenericPlayerBuild.cs.meta must be rolled back after failed deployment");

        // EditorBuildSettings.asset must NOT have been modified
        // (hasEnabledScene was false, but FixBuildSettingsViaEditorAPI threw before
        //  writing any temp script or modifying Build Settings)
        var settingsContent = File.ReadAllText(buildSettingsPath);
        Assert.Contains("m_Scenes: []", settingsContent);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 6 (Test D): Build/ directory protection
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// M22_5_06: CleanupAsync must NOT touch a pre-existing Build/ directory.
    ///
    /// M22.5 cleanup contract only covers Harness-owned files (injected .cs + .meta,
    /// EditorBuildSettings.asset). Pre-existing directories like Build/ are outside
    /// the contract and must remain untouched.
    ///
    /// This test calls CleanupAsync directly with a synthetic snapshot to verify
    /// the contract without needing a real Unity deployment.
    /// </summary>
    [Fact]
    public async Task M22_5_06_Protection_BuildDirectoryUntouched()
    {
        // ── Setup ──
        var synthProject = Path.Combine(_tempDir, "Synth_BuildDir");
        CreateMinimalUnityProject(synthProject);

        // Create pre-existing Build/ directory with content
        var buildDir = Path.Combine(synthProject, "Build");
        Directory.CreateDirectory(buildDir);
        var buildDummyFile = Path.Combine(buildDir, "pre_existing_build.txt");
        var buildDummyContent = "Pre-existing build artifact — must survive cleanup";
        File.WriteAllText(buildDummyFile, buildDummyContent);

        // Create dummy injected files (simulating what HarnessDeployer would have done)
        var scriptsDir = Path.Combine(synthProject, "Assets", "Scripts");
        var editorDir = Path.Combine(synthProject, "Assets", "Editor");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(editorDir);

        var injectedBridge = Path.Combine(scriptsDir, "GenericRuntimeBridge.cs");
        var injectedBuildEntry = Path.Combine(editorDir, "GenericPlayerBuild.cs");
        File.WriteAllText(injectedBridge, "// dummy injected file");
        File.WriteAllText(injectedBuildEntry, "// dummy injected file");

        var snapshot = new HarnessSnapshot
        {
            FilesInjectedByUs = new List<string> { injectedBridge, injectedBuildEntry },
            BuildSettingsModified = false,
            DeployCompletedSuccessfully = true,
        };

        // ── Act ──
        var deployer = new HarnessDeployer(InspectorRepoRoot);
        var options = new RuntimeRunOptions
        {
            ProjectPath = synthProject,
            UnityExecutable = UnityExecutable,
        };

        await deployer.CleanupAsync(snapshot, options);

        // ── Assert: Injected files deleted ──
        Assert.False(File.Exists(injectedBridge),
            "Injected GenericRuntimeBridge.cs must be deleted by cleanup");
        Assert.False(File.Exists(injectedBuildEntry),
            "Injected GenericPlayerBuild.cs must be deleted by cleanup");

        // ── Assert: Build/ directory and contents untouched ──
        Assert.True(Directory.Exists(buildDir),
            "Pre-existing Build/ directory must still exist after cleanup");
        Assert.True(File.Exists(buildDummyFile),
            "Pre-existing Build/ contents must still exist after cleanup");
        Assert.Equal(buildDummyContent, File.ReadAllText(buildDummyFile));
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static void AssertPrerequisites()
    {
        Assert.True(File.Exists(UnityExecutable),
            $"Unity Editor not found at {UnityExecutable}. " +
            "This integration test requires Unity 2022.3.62f3c1 installed.");

        Assert.True(Directory.Exists(ThreeDimPath),
            $"3D Industrial Monitor project not found at {ThreeDimPath}. " +
            "This integration test requires the 3DIndustrialMonitor project cloned.");

        Assert.True(Directory.Exists(InspectorRepoRoot),
            $"Inspector repo root not found at {InspectorRepoRoot}. " +
            "This is the source of GenericRuntimeBridge.cs and GenericPlayerBuild.cs.");
    }

    /// <summary>
    /// Creates a minimal synthetic Unity project structure:
    /// Assets/Scenes/ (with dummy .unity), ProjectSettings/, Packages/manifest.json
    /// </summary>
    private static void CreateMinimalUnityProject(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "Assets", "Scenes"));
        Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(path, "Packages"));

        var scenePath = Path.Combine(path, "Assets", "Scenes", "TestScene.unity");
        File.WriteAllText(scenePath, """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
SceneSettings:
  m_ObjectHideFlags: 0
""");

        var manifestPath = Path.Combine(path, "Packages", "manifest.json");
        File.WriteAllText(manifestPath, """
{
  "dependencies": {}
}
""");
    }

    /// <summary>
    /// Returns the git status of a repository as a string.
    /// Used to check no harness-related files remain after cleanup.
    /// </summary>
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