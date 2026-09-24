using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityProjectInspector.Core.Parsing;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// M22.5 Harness Deployment Layer.
///
/// Deploys GenericRuntimeBridge.cs + GenericPlayerBuild.cs to a target Unity project,
/// verifies compilation, and safely restores the project to its original state.
///
/// Cleanup contract — Harness-owned changes are 100% restored:
///   - GenericRuntimeBridge.cs + .meta
///   - GenericPlayerBuild.cs + .meta
///   - __InspectorBuildSettingsFix.cs + .meta (if created)
///   - EditorBuildSettings.asset (if modified → original bytes + SHA-256 verified)
///
/// NOT responsible for restoring (Unity import side effects are outside M22.5 contract):
///   - Library/, Temp/, obj/ directories
///   - Unity auto-import metadata changes in ProjectSettings/*.asset
///   - Non-Harness file modifications in Assets/
///
/// Design constraints:
///   - Never overwrites existing files or .meta (throws DeploymentException)
///   - Uses Unity Editor API (EditorBuildSettings.scenes) for Build Settings changes
///   - Three independent compile verification states (A/B/C — never merged into one bool)
///   - SHA-256 verified restoration of EditorBuildSettings.asset
///   - No modifications to the Inspector's own Runtime core (RuntimeRunner, etc.)
///   - Cleanup only touches files that were injected by us
///   - Any exception after Harness-owned mutation triggers rollback
/// </summary>
public class HarnessDeployer
{
    private readonly string _repoRoot;

    /// <summary>Captures the result of running a Unity CLI process (with timeout handling).</summary>
    private readonly struct UnityProcessRunResult
    {
        public bool Exited { get; init; }
        public int ExitCode { get; init; }
    }

    // ─── Source paths (in Inspector repo) ───
    private const string BridgeSourceRelPath =
        "experiments/GenericUnityRuntimeHarness/Assets/Scripts/GenericRuntimeBridge.cs";
    private const string BuildEntrySourceRelPath =
        "experiments/GenericUnityRuntimeHarness/Assets/Editor/GenericPlayerBuild.cs";

    // ─── Target paths (in the student/third-party project) ───
    private const string BridgeTargetRelPath = "Assets/Scripts/GenericRuntimeBridge.cs";
    private const string BuildEntryTargetRelPath = "Assets/Editor/GenericPlayerBuild.cs";

    // ─── Temp editor script (for Build Settings API fix) ───
    private const string TempSetupScriptName = "__InspectorBuildSettingsFix";
    private const string TempSetupRelPath = "Assets/Editor/__InspectorBuildSettingsFix.cs";

    /// <summary>
    /// Creates a HarnessDeployer that reads harness source files from the
    /// Inspector repo at <paramref name="repoRoot"/>.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the UnityProjectInspector repository root.</param>
    /// <exception cref="ArgumentException">Thrown if repoRoot is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if repoRoot does not exist.</exception>
    public HarnessDeployer(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(repoRoot);
        if (!Directory.Exists(repoRoot))
            throw new DirectoryNotFoundException($"Repository root not found: {repoRoot}");
        _repoRoot = repoRoot;
    }

    /// <summary>
    /// Thrown when any deployment precondition fails (invalid project, pre-existing files, etc.).
    /// DeploymentException indicates the deployment has failed. Harness-owned changes are rolled
    /// back on a best-effort basis; cleanup failures are reported as warnings.
    /// </summary>
    public class DeploymentException : Exception
    {
        public DeploymentException(string message) : base(message) { }
        public DeploymentException(string message, Exception inner) : base(message, inner) { }
    }

    // ════════════════════════════════════════════════════════════════
    // DeployAsync
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Deploys the Generic Harness files to the target Unity project.
    ///
    /// Steps:
    ///   1. Validate project (Assets/, ProjectSettings/, Packages/ presence)
    ///   2. Best-effort project lock guard (Library/ilpp.pid — warning only)
    ///   3. Snapshot EditorBuildSettings.asset (original bytes + SHA-256)
    ///   4. Assert no pre-existing files at ALL four injection targets (.cs + .meta for both files)
    ///   5. Inject GenericRuntimeBridge.cs + GenericPlayerBuild.cs
    ///   6. If Build Settings have no enabled scene → fix via Unity Editor API
    ///   7. Verify compilation (Unity CLI + three independent states A/B/C)
    ///   8. On verification failure → rollback all Harness-owned mutations + throw DeploymentException
    ///   9. Mark snapshot as deployed successfully
    ///
    /// Rollback guarantee: any exception after step 5 (first file injection) triggers
    /// full cleanup of all Harness-owned changes before rethrowing.
    /// Exceptions before step 5 do NOT mutate the project, so no rollback needed.
    /// </summary>
    /// <remarks>The last compile result from the most recent DeployAsync or VerifyCompileAsync call.
    /// Null if no verification has been run yet.
    /// Consumers (including tests) can read this to inspect independent A/B/C states.</remarks>
    public CompileResult? LastCompileResult { get; private set; }

    /// <exception cref="DeploymentException">If any precondition fails or compile fails.</exception>
    public async Task<HarnessSnapshot> DeployAsync(RuntimeRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var projectPath = options.ProjectPath;

        // ── Step 1: Validate project (no mutation) ──
        if (!UnityProjectScanner.IsValidUnityProject(projectPath))
            throw new DeploymentException(
                $"Not a valid Unity project. Expected Assets/, ProjectSettings/, Packages/ directories. " +
                $"Path: {projectPath}");

        // ── Step 2: Best-effort project lock guard (no mutation) ──
        WarnIfProjectPossiblyOpen(projectPath);

        // ── Step 3: Snapshot EditorBuildSettings.asset (no mutation) ──
        var buildSettingsPath = Path.Combine(projectPath, "ProjectSettings", "EditorBuildSettings.asset");
        byte[]? originalBuildSettings = null;
        string? originalBuildSettingsSha = null;

        if (File.Exists(buildSettingsPath))
        {
            originalBuildSettings = File.ReadAllBytes(buildSettingsPath);
            originalBuildSettingsSha = HarnessSnapshot.ComputeSha256(originalBuildSettings);
        }

        // ── Step 4: Check pre-existing files at ALL four targets (.cs + .meta) ──
        // NEVER overwrite or delete pre-existing content.
        var bridgeTarget = Path.GetFullPath(Path.Combine(projectPath, BridgeTargetRelPath));
        var buildEntryTarget = Path.GetFullPath(Path.Combine(projectPath, BuildEntryTargetRelPath));

        var preExisting = CheckPreExistingConflicts(bridgeTarget, buildEntryTarget);
        if (preExisting.Count > 0)
        {
            throw new DeploymentException(
                $"Target Unity project already contains files that conflict with injection. " +
                $"Pre-existing files:{Environment.NewLine}" +
                string.Join(Environment.NewLine, preExisting.Select(p => $"  {p}")) +
                $"{Environment.NewLine}" +
                $"HarnessDeployer will NOT overwrite existing files to protect student work. " +
                $"Remove or rename these files manually, or use a project without them.");
        }

        // ── Determine if Build Settings need modification (read-only) ──
        bool hasEnabledScene = HasAtLeastOneEnabledScene(buildSettingsPath);

        var injectedByUs = new List<string>();
        var snapshot = new HarnessSnapshot
        {
            FilesInjectedByUs = injectedByUs,
            FilesPreExisting = preExisting,
            OriginalBuildSettingsBytes = originalBuildSettings,
            OriginalBuildSettingsSha256 = originalBuildSettingsSha,
            BuildSettingsModified = false,
        };

        // ═══════════════════════════════════════════════════════════
        // MUTATION PHASE — any exception here triggers rollback
        // ═══════════════════════════════════════════════════════════
        try
        {
            // ── Step 5: Inject harness files ──
            InjectFile(projectPath, _repoRoot, BridgeSourceRelPath, BridgeTargetRelPath, injectedByUs);
            InjectFile(projectPath, _repoRoot, BuildEntrySourceRelPath, BuildEntryTargetRelPath, injectedByUs);

            // ── Step 6: Fix Build Settings if needed ──
            if (!hasEnabledScene)
            {
                await FixBuildSettingsViaEditorAPI(projectPath, options, injectedByUs);
                snapshot.BuildSettingsModified = true;
            }

            // ── Step 7: Verify compilation ──
            var compileResult = await VerifyCompileAsync(projectPath, options.UnityExecutable);
            LastCompileResult = compileResult;

            // ── Step 8: Require A (ScriptsCompiled) AND B (MethodRecognized) ──
            // C (PlayerBuildSucceeded) is independently reported but NOT required for
            // M22.5 deployment success — the third-party project may have player-build
            // configuration issues unrelated to harness script correctness.
            if (!compileResult.ScriptsCompiled || !compileResult.MethodRecognized)
            {
                throw new DeploymentException(
                    $"Unity compilation verification failed after injecting harness files. " +
                    $"ScriptsCompiled: {compileResult.ScriptsCompiled}. " +
                    $"MethodRecognized: {compileResult.MethodRecognized}. " +
                    $"PlayerBuildSucceeded: {compileResult.PlayerBuildSucceeded}. " +
                    $"Exit code: {compileResult.ExitCode}. " +
                    $"Error excerpt:{Environment.NewLine}{compileResult.ErrorExcerpt}");
            }

            snapshot.DeployCompletedSuccessfully = true;
            return snapshot;
        }
        catch (DeploymentException) when (injectedByUs.Count == 0)
        {
            // Exception occurred BEFORE any file mutation — no rollback needed.
            // This includes: pre-existing conflicts, invalid project, build settings fix failures
            // (build settings fix writes a temp script tracked in injectedByUs; if that fails
            // before writing, injectedByUs is empty; if after writing, injectedByUs has it).
            throw;
        }
        catch (Exception)
        {
            // Exception occurred AFTER injection started — rollback all Harness-owned mutations.
            await CleanupInternalAsync(projectPath, injectedByUs, snapshot);
            throw;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // VerifyCompileAsync
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Result of a Unity compilation verification. Three INDEPENDENT states:
    ///
    /// A — ScriptsCompiled:    Unity script compilation succeeded (no CS errors).
    /// B — MethodRecognized:   GenericPlayerBuild.Build was found AND invoked by Unity.
    /// C — PlayerBuildSucceeded: BuildPipeline.BuildPlayer completed successfully.
    ///
    /// These three states MUST NEVER be merged into a single "CompileSucceeded" bool.
    /// Each answers a completely different question about what happened in Unity.
    ///
    /// M22.5 deployment requires A AND B. C is independently reported but NOT required —
    /// a third-party project may have player-build configuration issues unrelated to
    /// harness script correctness.
    /// </summary>
    public record CompileResult
    {
        /// <summary>True if the Unity process timed out (did not exit within the wait window).
        /// When true, ExitCode is meaningless (process was still running) and all A/B/C states
        /// should be treated as unknown.</summary>
        public bool TimedOut { get; init; }

        /// <summary>Exit code of the Unity CLI process. Meaningless when TimedOut is true.</summary>
        public int ExitCode { get; init; }

        /// <summary>
        /// A: Unity script compilation succeeded.
        /// True = no "error CS\d+", "Scripts have compiler errors", or "Aborting" in log.
        /// False = script compilation failed (injected code has errors).
        /// Unknown when TimedOut is true (log may be incomplete).
        /// </summary>
        public bool ScriptsCompiled { get; init; }

        /// <summary>
        /// B: GenericPlayerBuild.Build was found and invoked by Unity.
        /// True = GenericPlayerBuild.Build method body produced its signature marker
        ///         ("[GenericPlayerBuild]") in the Unity log.
        /// False = method not found (will appear in log as "Could not find method").
        /// Unknown when TimedOut is true (log may be incomplete).
        ///
        /// This uses ONLY the method-body marker "[GenericPlayerBuild]", NOT bare string
        /// matching of "GenericPlayerBuild" (which would match error messages about the
        /// method not being found).
        /// </summary>
        public bool MethodRecognized { get; init; }

        /// <summary>
        /// C: BuildPipeline.BuildPlayer completed successfully.
        /// True = exit code 0.
        /// False = exit code != 0 (player build failed for project-specific reasons).
        /// Unknown when TimedOut is true.
        /// </summary>
        public bool PlayerBuildSucceeded { get; init; }

        /// <summary>Short excerpt from the build log if compilation failed.</summary>
        public string ErrorExcerpt { get; init; } = string.Empty;

        /// <summary>Stdout output from Unity CLI.</summary>
        public string Stdout { get; init; } = string.Empty;
    }

    /// <summary>
    /// Runs Unity CLI with -executeMethod GenericPlayerBuild.Build and
    /// performs three INDEPENDENT verifications:
    ///
    /// A (ScriptsCompiled):
    ///   No "error CS\d+", "Scripts have compiler errors", "Compilation failed", or "Aborting"
    ///
    /// B (MethodRecognized):
    ///   Log contains "[GenericPlayerBuild]" — a marker ONLY produced by the method body
    ///   (GenericPlayerBuild.cs Debug.Log). This is the only reliable way to prove the
    ///   method was found and invoked. NOT bare "GenericPlayerBuild" string matching.
    ///
    /// C (PlayerBuildSucceeded):
    ///   Exit code == 0.
    /// </summary>
    public async Task<CompileResult> VerifyCompileAsync(string projectPath, string unityExecutable)
    {
        var logFile = Path.Combine(
            Path.GetTempPath(), $"harness_compile_{Guid.NewGuid():N}.log");

        var psi = new ProcessStartInfo
        {
            FileName = unityExecutable,
            Arguments = $"-batchmode -quit " +
                        $"-projectPath \"{projectPath}\" " +
                        $"-executeMethod GenericPlayerBuild.Build " +
                        $"-logFile \"{logFile}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = process.WaitForExit(300_000); // 5 min for full import + compile + build

        // ── Handle timeout: kill Unity before doing anything else ──
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            // Wait for the killed process to fully exit
            try { process.WaitForExit(10_000); } catch { /* best-effort */ }
        }

        // Only read ExitCode from a process that has actually exited
        var exitCode = exited ? process.ExitCode : -1;

        var stdout = await stdoutTask;
        var _ = await stderrTask;

        // If process timed out, return a timeout result immediately
        // (log content may be incomplete — Unity was mid-build)
        if (!exited)
        {
            // Best-effort clean up temp log before returning
            try { if (File.Exists(logFile)) File.Delete(logFile); } catch { }

            return new CompileResult
            {
                TimedOut = true,
                ExitCode = -1,
                ScriptsCompiled = false,
                MethodRecognized = false,
                PlayerBuildSucceeded = false,
                ErrorExcerpt = "Unity process timed out (5 min). Process was terminated.",
                Stdout = stdout,
            };
        }

        // Read log file (primary source for error diagnosis)
        string logContent = "";
        try
        {
            if (File.Exists(logFile))
                logContent = File.ReadAllText(logFile);
        }
        catch
        {
            // best effort
        }

        // ── A: Script compilation check ──
        // Unity compiles all scripts BEFORE attempting -executeMethod.
        // If CS errors exist, the method is never invoked.
        bool hasCompilerErrors = ContainsPattern(logContent, @"error CS\d+") ||
                                 ContainsPattern(logContent, "Scripts have compiler errors") ||
                                 ContainsPattern(logContent, "Compilation failed") ||
                                 ContainsPattern(logContent, "Aborting");
        bool scriptsCompiled = !hasCompilerErrors;

        // ── B: Method recognition check ──
        // ONLY "[GenericPlayerBuild]" — a remark produced by GenericPlayerBuild.cs Debug.Log.
        // This is the unique marker for "method was found AND its body executed".
        //
        // We explicitly do NOT match bare "GenericPlayerBuild" because that would also match:
        //   "Could not find method 'GenericPlayerBuild.Build' specified via -executeMethod"
        // which is the OPPOSITE of what we want to detect.
        //
        // We also do NOT match "Building with" or "Build succeeded" — these are too generic.
        bool methodRecognized = ContainsPattern(logContent, "[GenericPlayerBuild]");

        // ── C: Player build success check ──
        // BuildPipeline.BuildPlayer succeeded iff exit code 0.
        bool playerBuildSucceeded = exitCode == 0;

        // Extract error excerpt for diagnostics
        string errorExcerpt = "";
        if (hasCompilerErrors || !methodRecognized)
        {
            errorExcerpt = ExtractLogExcerpt(logContent, 500);
        }

        // Clean up log file
        try
        {
            if (File.Exists(logFile))
                File.Delete(logFile);
        }
        catch
        {
            // best effort
        }

        return new CompileResult
        {
            ExitCode = exitCode,
            ScriptsCompiled = scriptsCompiled,
            MethodRecognized = methodRecognized,
            PlayerBuildSucceeded = playerBuildSucceeded,
            ErrorExcerpt = errorExcerpt,
            Stdout = stdout,
        };
    }

    // ════════════════════════════════════════════════════════════════
    // CleanupAsync (public API)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Removes injected files and restores EditorBuildSettings.asset.
    ///
    /// Only touches files that are in FilesInjectedByUs (the definitive record of
    /// what HarnessDeployer created). Never deletes pre-existing files.
    ///
    /// Does NOT touch Library/, Temp/, or Build/ directories — those are not
    /// Harness-owned and may contain pre-existing content or Unity-generated artifacts.
    ///
    /// Does NOT throw — logs warnings on failure to stderr.
    ///
    /// Restoration order:
    ///   1. Delete injected .cs files + corresponding .meta files
    ///   2. If BuildSettingsModified, restore original bytes + verify SHA-256
    /// </summary>
    public async Task CleanupAsync(HarnessSnapshot snapshot, RuntimeRunOptions options)
    {
        // Nothing to clean if deploy never started or no files injected
        if (!snapshot.DeployCompletedSuccessfully && snapshot.FilesInjectedByUs.Count == 0)
            return;

        var warnings = new List<string>();
        var projectPath = options.ProjectPath;

        // ── Delete injected .cs and .meta files ──
        // Only touches files that HarnessDeployer itself created.
        foreach (var filePath in snapshot.FilesInjectedByUs)
        {
            SafeDeleteFile(filePath, warnings);
            SafeDeleteFile(filePath + ".meta", warnings);
        }

        // ── Restore EditorBuildSettings.asset if we modified it ──
        if (snapshot.BuildSettingsModified && snapshot.OriginalBuildSettingsBytes != null)
        {
            var bsp = Path.Combine(projectPath, "ProjectSettings", "EditorBuildSettings.asset");
            try
            {
                File.WriteAllBytes(bsp, snapshot.OriginalBuildSettingsBytes);

                // Verify SHA-256 consistency
                var newSha = HarnessSnapshot.ComputeFileSha256(bsp);
                if (newSha != snapshot.OriginalBuildSettingsSha256)
                {
                    warnings.Add(
                        $"EditorBuildSettings.asset was restored but SHA-256 does not match original. " +
                        $"Expected: {snapshot.OriginalBuildSettingsSha256}, Got: {newSha}");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to restore EditorBuildSettings.asset: {ex.Message}");
            }
        }

        // Report warnings
        if (warnings.Count > 0)
        {
            var msg = $"[HarnessDeployer.Cleanup] Completed with {warnings.Count} warning(s):{Environment.NewLine}" +
                      string.Join(Environment.NewLine, warnings.Select(w => $"  - {w}"));
            Console.Error.WriteLine(msg);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Internal Cleanup (used by DeployAsync on failure)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Rollback helper called when DeployAsync fails after making Harness-owned mutations.
    /// Same logic as CleanupAsync but works with the partially-populated injectedByUs list.
    ///
    /// This is NOT a public API — it is called from the DeployAsync catch block.
    /// The cleanup result is best-effort (logged to warnings), and the original exception
    /// is always rethrown so the caller knows the deploy failed.
    /// </summary>
    private async Task CleanupInternalAsync(
        string projectPath,
        List<string> injectedByUs,
        HarnessSnapshot snapshot)
    {
        var warnings = new List<string>();

        foreach (var filePath in injectedByUs)
        {
            SafeDeleteFile(filePath, warnings);
            SafeDeleteFile(filePath + ".meta", warnings);
        }

        if (snapshot.BuildSettingsModified && snapshot.OriginalBuildSettingsBytes != null)
        {
            var bsp = Path.Combine(projectPath, "ProjectSettings", "EditorBuildSettings.asset");
            try
            {
                File.WriteAllBytes(bsp, snapshot.OriginalBuildSettingsBytes);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to restore EditorBuildSettings.asset during rollback: {ex.Message}");
            }
        }

        if (warnings.Count > 0)
        {
            var msg = $"[HarnessDeployer.Rollback] Rollback completed with {warnings.Count} warning(s):" +
                      $"{Environment.NewLine}" +
                      string.Join(Environment.NewLine, warnings.Select(w => $"  - {w}"));
            Console.Error.WriteLine(msg);
        }

        await Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════════
    // Private Helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks ALL FOUR potential conflict paths (.cs and .meta for both Bridge and BuildEntry).
    /// Returns the list of files that already exist. Empty list means no conflicts.
    ///
    /// A file is "pre-existing" if either the .cs or its .meta already exists at the
    /// injection target path. Both are treated as conflicts because:
    ///   - If .cs exists → we cannot overwrite it
    ///   - If only .meta exists → we cannot safely inject .cs and leave orphan .meta behind
    ///     (Cleanup would delete the .meta thinking it was ours if we don't track it,
    ///      and tracking a pre-existing .meta as "injected by us" would cause us to
    ///      delete student content during cleanup)
    /// </summary>
    private static List<string> CheckPreExistingConflicts(string bridgeTarget, string buildEntryTarget)
    {
        var conflicts = new List<string>();

        var bridgeMeta = bridgeTarget + ".meta";
        var buildEntryMeta = buildEntryTarget + ".meta";

        if (File.Exists(bridgeTarget))
            conflicts.Add(bridgeTarget);
        if (File.Exists(bridgeMeta))
            conflicts.Add(bridgeMeta);
        if (File.Exists(buildEntryTarget))
            conflicts.Add(buildEntryTarget);
        if (File.Exists(buildEntryMeta))
            conflicts.Add(buildEntryMeta);

        return conflicts;
    }

    /// <summary>
    /// Reads EditorBuildSettings.asset YAML and checks for at least one enabled scene.
    /// This is a READ-ONLY operation — does not modify anything.
    ///
    /// The YAML has the structure:
    ///   EditorBuildSettings:
    ///     m_Scenes:
    ///     - enabled: 1
    ///       path: Assets/Scenes/SomeScene.unity
    ///       guid: ...
    /// </summary>
    private static bool HasAtLeastOneEnabledScene(string buildSettingsPath)
    {
        if (!File.Exists(buildSettingsPath))
            return false;

        try
        {
            var text = File.ReadAllText(buildSettingsPath);

            // Look for "- enabled: 1" lines (indicates an enabled scene in Build Settings)
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- enabled: 1") || trimmed == "enabled: 1")
                    return true;
            }

            return false;
        }
        catch
        {
            // If we can't read the file, assume no enabled scenes (safe default)
            return false;
        }
    }

    /// <summary>
    /// Best-effort guard against deploying to a project that may be open in Unity Editor.
    ///
    /// Uses Library/ilpp.pid as the indicator. This is NOT a reliable project-lock mechanism:
    ///   - ilpp.pid may be stale (process already exited)
    ///   - The PID may have been recycled to a non-Unity process
    ///   - The file may not exist even though Editor is open (early startup)
    ///   - The file may be unreadable (permissions)
    ///
    /// When a running process is confirmed at the PID from ilpp.pid, we log a warning
    /// to stderr and continue. We do NOT throw — throwing would give a false sense of
    /// certainty in a mechanism that is fundamentally best-effort.
    ///
    /// The only truly reliable project lock check would be to parse the Unity Editor's
    /// process list for open project paths, which is beyond the scope of M22.5.
    /// </summary>
    private static void WarnIfProjectPossiblyOpen(string projectPath)
    {
        var pidFilePath = Path.Combine(projectPath, "Library", "ilpp.pid");
        if (!File.Exists(pidFilePath))
            return;

        try
        {
            var pidText = File.ReadAllText(pidFilePath).Trim();
            if (int.TryParse(pidText, out var pid) && pid > 0)
            {
                var isRunning = IsProcessRunning(pid);
                if (isRunning)
                {
                    Console.Error.WriteLine(
                        $"[HarnessDeployer] WARNING: Project at {projectPath} has a running " +
                        $"process at PID {pid} (from Library/ilpp.pid). " +
                        $"This may indicate the Unity Editor is currently editing this project. " +
                        $"Proceeding with deployment anyway (this is a best-effort check).");
                }
            }
        }
        catch
        {
            // If we can't read or parse the lock file, proceed silently.
            // The mechanism is best-effort; silence is acceptable.
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = $"-0 {pid}", // signal 0 = test if process exists
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            proc.Start();
            proc.WaitForExit(2000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Injects a file from the Inspector repo into the target Unity project.
    /// Creates parent directories if they don't exist.
    /// Throws DeploymentException if the source file is missing.
    /// </summary>
    private void InjectFile(
        string projectPath,
        string repoRoot,
        string sourceRelPath,
        string targetRelPath,
        List<string> injectedList)
    {
        var sourcePath = Path.GetFullPath(Path.Combine(repoRoot, sourceRelPath));
        var targetPath = Path.GetFullPath(Path.Combine(projectPath, targetRelPath));

        if (!File.Exists(sourcePath))
            throw new DeploymentException(
                $"Harness source file not found in Inspector repo: {sourcePath}");

        // Create parent directory if needed
        var targetDir = Path.GetDirectoryName(targetPath)!;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        // Copy file
        File.Copy(sourcePath, targetPath, overwrite: false);

        injectedList.Add(targetPath);
    }

    /// <summary>
    /// Fixes Build Settings by adding the first discovered .unity scene file
    /// as an enabled scene, using Unity Editor API (EditorBuildSettings.scenes).
    ///
    /// Approach: write a temporary editor script → run Unity CLI to execute it
    /// → remove the temporary script. This ensures we use the Unity API rather
    /// than directly manipulating YAML.
    /// </summary>
    private async Task FixBuildSettingsViaEditorAPI(
        string projectPath,
        RuntimeRunOptions options,
        List<string> injectedByUs)
    {
        // Find first .unity scene file
        var unityFiles = Directory.GetFiles(
            Path.Combine(projectPath, "Assets"),
            "*.unity",
            SearchOption.AllDirectories);

        if (unityFiles.Length == 0)
            throw new DeploymentException(
                "Cannot fix Build Settings: no .unity files found in the project's Assets/ directory.");

        var firstScene = unityFiles[0];

        // CRITICAL: Compute project-relative path. EditorBuildSettingsScene(string, bool)
        // expects a project-relative path (e.g. "Assets/Scenes/Scene.unity"), NOT an
        // absolute filesystem path. An absolute path causes "Failed to open" during
        // BuildPipeline.BuildPlayer because Unity resolves the path relative to the
        // project root.
        var relativeScenePath = Path.GetRelativePath(projectPath, firstScene);
        var tempSetupPath = Path.GetFullPath(Path.Combine(projectPath, TempSetupRelPath));

        // Write temporary editor script
        var tempDir = Path.GetDirectoryName(tempSetupPath)!;
        if (!Directory.Exists(tempDir))
            Directory.CreateDirectory(tempDir);

        var scriptContent = GenerateBuildSettingsFixScript(relativeScenePath);
        File.WriteAllText(tempSetupPath, scriptContent);

        // Track for cleanup (in case anything goes wrong)
        injectedByUs.Add(tempSetupPath);

        // Allow file system to settle
        await Task.Delay(200);

        // Run Unity CLI to execute the fix
        var logFile = Path.Combine(
            Path.GetTempPath(), $"buildsettings_fix_{Guid.NewGuid():N}.log");

        var psi = new ProcessStartInfo
        {
            FileName = options.UnityExecutable,
            Arguments = $"-batchmode -quit " +
                        $"-projectPath \"{projectPath}\" " +
                        $"-executeMethod {TempSetupScriptName}.EnsureFirstSceneInBuild " +
                        $"-logFile \"{logFile}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        UnityProcessRunResult unityResult;
        using (var process = new Process { StartInfo = psi })
        {
            process.Start();
            var exited = process.WaitForExit(120_000); // 2 min for import + compile + execute

            // Kill on timeout
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { process.WaitForExit(10_000); } catch { }
            }

            unityResult = new UnityProcessRunResult
            {
                Exited = exited,
                ExitCode = exited ? process.ExitCode : -1,
            };
        }

        // Read log for diagnostics
        string logContent = "";
        try
        {
            if (File.Exists(logFile))
                logContent = File.ReadAllText(logFile);
        }
        catch { /* best effort */ }

        // Check for compiler errors in the temp script itself
        bool tempScriptCompiled = !ContainsPattern(logContent, @"error CS\d+") &&
                                  !ContainsPattern(logContent, "Scripts have compiler errors");

        bool fixFailed = !unityResult.Exited ||
                         unityResult.ExitCode != 0 ||
                         !tempScriptCompiled;

        if (fixFailed)
        {
            // Attempt to clean up temp script, but DONT remove from injectedByUs
            // unless deletion actually succeeds. If deletion fails, the tracking
            // should remain so that cleanup/rollback can retry.
            var deleteFailed = false;
            var failWarnings = new List<string>();
            SafeDeleteFile(tempSetupPath, failWarnings);
            if (File.Exists(tempSetupPath))
            {
                deleteFailed = true;
                Console.Error.WriteLine(
                    $"[HarnessDeployer] WARNING: Could not delete temp setup script: {tempSetupPath}. " +
                    "It will remain tracked for rollback.");
            }
            SafeDeleteFile(tempSetupPath + ".meta", failWarnings);
            if (!deleteFailed)
            {
                injectedByUs.Remove(tempSetupPath);
            }

            var exitInfo = unityResult.Exited
                ? $"Exit code: {unityResult.ExitCode}"
                : "TIMEOUT (2 min)";

            throw new DeploymentException(
                $"Failed to fix Build Settings via Unity Editor API. {exitInfo}. " +
                $"Log excerpt:{Environment.NewLine}{ExtractLogExcerpt(logContent, 300)}");
        }

        // Remove temp setup script — no longer needed
        // Only remove from tracking after confirming deletion.
        var cleanupWarnings = new List<string>();
        SafeDeleteFile(tempSetupPath, cleanupWarnings);
        SafeDeleteFile(tempSetupPath + ".meta", cleanupWarnings);
        if (!File.Exists(tempSetupPath))
        {
            injectedByUs.Remove(tempSetupPath);
        }
        else
        {
            Console.Error.WriteLine(
                $"[HarnessDeployer] WARNING: Temp setup script survives deletion: {tempSetupPath}. " +
                "Tracking preserved for cleanup.");
        }

        // Clean up log file
        try { if (File.Exists(logFile)) File.Delete(logFile); } catch { }
    }

    /// <summary>
    /// Generates the C# source for the temporary Build Settings fix script.
    /// Uses UnityEditor.Build.EditorBuildSettings API to add the scene.
    /// </summary>
    private static string GenerateBuildSettingsFixScript(string firstScenePath)
    {
        var escapedPath = firstScenePath
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

        return $$"""
using UnityEditor;
using UnityEngine;

public static class __InspectorBuildSettingsFix
{
    /// <summary>
    /// Called via Unity CLI -executeMethod.
    /// Ensures at least one scene is enabled in Build Settings.
    /// If there are already enabled scenes, this is a no-op.
    /// Otherwise adds the first discovered .unity file.
    /// </summary>
    public static void EnsureFirstSceneInBuild()
    {
        var scenePath = "{{escapedPath}}";

        var existingScenes = EditorBuildSettings.scenes;

        // Check if any scene is already enabled
        foreach (var s in existingScenes)
        {
            if (s.enabled)
            {
                Debug.Log("[InspectorBuildSettingsFix] Build Settings already has enabled scenes. No change needed.");
                EditorApplication.Exit(0);
                return;
            }
        }

        // Add the first scene as enabled
        var newScenes = new EditorBuildSettingsScene[existingScenes.Length + 1];
        existingScenes.CopyTo(newScenes, 0);
        newScenes[newScenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);

        EditorBuildSettings.scenes = newScenes;

        Debug.Log($"[InspectorBuildSettingsFix] Added scene to Build Settings: {scenePath}");
        EditorApplication.Exit(0);
    }
}
""";
    }

    // ════════════════════════════════════════════════════════════════
    // File & String Helpers
    // ════════════════════════════════════════════════════════════════

    private static void SafeDeleteFile(string path, List<string> warnings)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to delete {path}: {ex.Message}");
        }
    }

    private static bool ContainsPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }
        catch
        {
            // Fallback: simple contains search
            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ExtractLogExcerpt(string logContent, int maxChars)
    {
        if (string.IsNullOrEmpty(logContent))
            return "(no log content)";

        // Try to find the error section — look backwards from "error CS" or "Aborting"
        var errorKeywords = new[] { "error CS", "Aborting", "Scripts have compiler errors", "Error building Player" };
        int startPos = -1;

        foreach (var keyword in errorKeywords)
        {
            var idx = logContent.LastIndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                startPos = Math.Max(0, idx - 200);
                break;
            }
        }

        if (startPos < 0)
            startPos = Math.Max(0, logContent.Length - maxChars);

        var excerpt = logContent.Substring(startPos, Math.Min(maxChars, logContent.Length - startPos));
        return excerpt.Trim();
    }
}