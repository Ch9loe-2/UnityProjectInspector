using System.Diagnostics;
using System.Text.Json;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Orchestrates the end-to-end Runtime Inspection Pipeline:
///   1. Build the Unity standalone Player (via -executeMethod with a temp build script)
///   2. Launch the Player process
///   3. Poll for Runtime Evidence JSON
///   4. Classify the result (Passed / Failed / Timeout / ProcessExited / BuildFailed)
///   5. Return a structured RuntimeSession
///
/// Design based on M12 experimental evidence:
/// - Minimal Player Build: ~5s on macOS arm64
/// - Player Runtime evidence: ~0.2s after launch
/// - [RuntimeInitializeOnLoadMethod] + environment variable guard is the proven approach
///
/// All processes launched by this runner are tracked by PID and safely terminated
/// on timeout or error. No global kill commands are used.
/// </summary>
public class RuntimeRunner : IRuntimeRunner
{
    /// <summary>
    /// Runs the full runtime inspection pipeline.
    /// </summary>
    public async Task<RuntimeSession> RunAsync(
        RuntimeRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var session = new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            UnityVersion = GetUnityVersion(options.UnityExecutable),
            StartedAt = DateTime.UtcNow,
        };

        // Phase 1: Build
        var buildPhase = await BuildPlayerAsync(options, cancellationToken);
        session.Message = buildPhase.Message;
        if (buildPhase.Status != RuntimeResultStatus.Passed)
        {
            session.Result = buildPhase.Status;
            return session;
        }

        // Phase 2: Launch & Poll
        var playerExePath = buildPhase.PlayerExecutablePath!;
        return await LaunchAndPollAsync(options, playerExePath, session, cancellationToken);
    }

    // ══════════════════════════════════════════════════════════════
    // Build Phase
    // ══════════════════════════════════════════════════════════════

    private record BuildPhaseResult(RuntimeResultStatus Status, string? Message, string? PlayerExecutablePath);

    private async Task<BuildPhaseResult> BuildPlayerAsync(
        RuntimeRunOptions options, CancellationToken ct)
    {
        var projectPath = options.ProjectPath;
        var buildDir = string.IsNullOrEmpty(options.BuildDirectory)
            ? Path.Combine(projectPath, "Build")
            : options.BuildDirectory;

        var appName = options.ProductName + ".app";
        var appPath = Path.Combine(buildDir, appName);

        // Don't rebuild if the Player already exists (cached)
        if (Directory.Exists(appPath))
        {
            var exePath = Path.Combine(appPath, "Contents", "MacOS", options.ProductName);
            if (File.Exists(exePath))
            {
                return new BuildPhaseResult(RuntimeResultStatus.Passed,
                    $"Using cached Player at {exePath}", exePath);
            }
        }

        // The target project must have a build script in Assets/Editor/.
        // For M12 project, this is M12_PlayerBuild.Build.
        // The build script must use BuildPipeline.BuildPlayer() with StandaloneOSX target.
        //
        // The Runner does NOT inject temporary scripts into the target project.
        // The user must ensure their project has a compatible build method.
        // Default: "M12_PlayerBuild.Build" (from M12_MinimalUnityProject).
        var buildMethod = options.BuildMethod;

        var psi = new ProcessStartInfo
        {
            FileName = options.UnityExecutable,
            Arguments = $"-batchmode -noGraphics -quit " +
                $"-projectPath \"{projectPath}\" " +
                $"-executeMethod {buildMethod} " +
                $"-logFile \"{buildDir}/m13_build.log\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var runnerPid = process.Id;

        try
        {
            var exited = process.WaitForExit(options.BuildTimeoutSeconds * 1000);

            if (!exited)
            {
                KillProcess(runnerPid);
                await process.WaitForExitAsync(CancellationToken.None);
                return new BuildPhaseResult(RuntimeResultStatus.BuildFailed,
                    $"Build timed out after {options.BuildTimeoutSeconds}s. " +
                    $"Check {buildDir}/m13_build.log for details.", null);
            }

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                return new BuildPhaseResult(RuntimeResultStatus.BuildFailed,
                    $"Build failed with exit code {process.ExitCode}. {stderr.Trim()}", null);
            }

            // Find the Player executable
            var exePath = FindPlayerExecutable(buildDir, options.ProductName);
            if (exePath == null)
            {
                return new BuildPhaseResult(RuntimeResultStatus.BuildFailed,
                    $"Build appeared to succeed but Player executable not found at {buildDir}. " +
                    $"Check {buildDir}/m13_build.log.", null);
            }

            return new BuildPhaseResult(RuntimeResultStatus.Passed,
                $"Build succeeded. Player at {exePath}", exePath);
        }
        catch (OperationCanceledException)
        {
            KillProcess(runnerPid);
            return new BuildPhaseResult(RuntimeResultStatus.BuildFailed,
                "Build was cancelled.", null);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Launch & Poll Phase
    // ══════════════════════════════════════════════════════════════

    private async Task<RuntimeSession> LaunchAndPollAsync(
        RuntimeRunOptions options,
        string playerExePath,
        RuntimeSession session,
        CancellationToken ct)
    {
        var evidenceDir = string.IsNullOrEmpty(options.EvidenceDirectory)
            ? Path.Combine(Path.GetTempPath(), "M13_Results")
            : options.EvidenceDirectory;

        Directory.CreateDirectory(evidenceDir);

        var evidenceFile = Path.Combine(evidenceDir, options.EvidenceFileName);
        var markerFile = Path.Combine(evidenceDir, options.MarkerFileName);

        // Clean previous markers
        TryDeleteFile(markerFile);
        TryDeleteFile(evidenceFile);

        // Launch Player with the evidence directory env var
        var env = new Dictionary<string, string>
        {
            [options.ResultDirEnvVar] = evidenceDir,
        };

        using var playerProcess = StartProcess(playerExePath,
            "-batchmode -nographics", env);

        if (playerProcess == null)
        {
            session.Result = RuntimeResultStatus.ProcessExited;
            session.Message = $"Failed to start Player process at {playerExePath}";
            return session;
        }

        var playerPid = playerProcess.Id;
        var deadline = DateTime.UtcNow.AddSeconds(options.PlayerTimeoutSeconds);

        PlayerEvidence? evidence = null;
        bool processExited = false;

        // Poll for evidence or process exit
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (playerProcess.HasExited)
            {
                processExited = true;
                break;
            }

            // Check marker file (Player writes this when done)
            if (File.Exists(markerFile))
            {
                evidence = TryReadEvidence(evidenceFile);
                break;
            }

            // Also check evidence file directly
            if (File.Exists(evidenceFile))
            {
                evidence = TryReadEvidence(evidenceFile);
                break;
            }

            await Task.Delay(200, ct);
        }

        // ─── Classify Phase ───

        // Case 1: Cancelled
        if (ct.IsCancellationRequested)
        {
            KillProcess(playerPid);
            await SafeWaitExit(playerProcess, 5000);
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = "Runtime inspection was cancelled.";
            return session;
        }

        // Case 2: Timeout (still running)
        if (!processExited && evidence == null)
        {
            KillProcess(playerPid);
            await SafeWaitExit(playerProcess, 5000);
            session.Result = RuntimeResultStatus.Timeout;
            session.Message = $"Player did not produce evidence within {options.PlayerTimeoutSeconds}s timeout. Process was terminated.";
            return session;
        }

        // Case 3: Process exited before evidence
        if (processExited && evidence == null)
        {
            var exitCode = playerProcess.ExitCode;
            await SafeWaitExit(playerProcess, 2000);
            session.Result = RuntimeResultStatus.ProcessExited;
            session.Message = $"Player exited (code {exitCode}) before producing evidence.";
            return session;
        }

        // Case 4: Evidence received
        if (evidence != null)
        {
            // Let process finish naturally
            await SafeWaitExit(playerProcess, 5000);
            if (!playerProcess.HasExited)
            {
                KillProcess(playerPid);
            }

            return ClassifyEvidence(session, evidence);
        }

        // Fallback (shouldn't reach here)
        session.Result = RuntimeResultStatus.NotEvaluated;
        session.Message = "Unknown outcome: evidence was not collected.";
        return session;
    }

    // ══════════════════════════════════════════════════════════════
    // Classification
    // ══════════════════════════════════════════════════════════════

    private static RuntimeSession ClassifyEvidence(RuntimeSession session, PlayerEvidence evidence)
    {
        var evidenceModel = new RuntimeEvidence
        {
            Type = "RuntimeEvidence",
            Expected = "Runtime",
            Observed = evidence.ExecutionMode,
            Obtained = true,
            Message = $"executionMode={evidence.ExecutionMode}, " +
                      $"isEditor={evidence.IsEditor}, isPlaying={evidence.IsPlaying}, " +
                      $"activeScene={evidence.ActiveScene}, success={evidence.Success}",
        };
        session.Evidence.Add(evidenceModel);

        // Validate the evidence
        if (!evidence.IsValidRuntimeEvidence)
        {
            session.Result = RuntimeResultStatus.Failed;
            session.Message = evidence.ExecutionMode == "PlayMode (Editor)"
                ? "Evidence indicates Editor PlayMode, not true Runtime. " +
                  "isEditor=true — this is NOT a standalone Player."
                : $"Evidence is invalid for Runtime: " +
                  $"executionMode={evidence.ExecutionMode}, " +
                  $"isEditor={evidence.IsEditor}, isPlaying={evidence.IsPlaying}";
            return session;
        }

        if (!evidence.Success)
        {
            session.Result = RuntimeResultStatus.Failed;
            session.Message = $"Runtime evidence success=false: " +
                              $"activeScene={evidence.ActiveScene}";
            return session;
        }

        session.Result = RuntimeResultStatus.Passed;
        session.Message = $"Runtime inspection passed. " +
                          $"ActiveScene='{evidence.ActiveScene}', " +
                          $"isEditor={evidence.IsEditor}, isPlaying={evidence.IsPlaying}";
        return session;
    }

    // ══════════════════════════════════════════════════════════════
    // Process Management
    // ══════════════════════════════════════════════════════════════

    private static void KillProcess(int pid)
    {
        try
        {
            using var killer = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = pid.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            killer.Start();
            killer.WaitForExit(3000);
        }
        catch
        {
            // Best-effort
        }
    }

    private static Process? StartProcess(
        string exePath, string arguments, Dictionary<string, string> env)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            foreach (var kv in env)
                psi.EnvironmentVariables[kv.Key] = kv.Value;

            var process = new Process { StartInfo = psi };
            process.Start();
            return process;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SafeWaitExit(Process process, int timeoutMs)
    {
        try
        {
            if (!process.HasExited)
            {
                var exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    // Will be killed externally
                }
            }
        }
        catch
        {
            // Process may already have been reclaimed
        }
        await Task.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    private static string? FindPlayerExecutable(string buildDir, string productName)
    {
        var appBundle = Path.Combine(buildDir, productName + ".app");
        var exePath = Path.Combine(appBundle, "Contents", "MacOS", productName);
        return File.Exists(exePath) ? exePath : null;
    }

    private static PlayerEvidence? TryReadEvidence(string evidenceFile)
    {
        try
        {
            if (!File.Exists(evidenceFile))
                return null;
            var json = File.ReadAllText(evidenceFile);
            return JsonSerializer.Deserialize<PlayerEvidence>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string GetUnityVersion(string unityExecutable)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = unityExecutable,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output?.Trim() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}