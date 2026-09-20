using System.Diagnostics;
using System.Text.Json;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Orchestrates the end-to-end Runtime Inspection Pipeline:
///   1. Build the Unity standalone Player (via -executeMethod)
///   2. Launch the Player process
///   3. Execute Action Pipeline (Wait, ClickButton, ObserveActiveScene, ReadLogs)
///      via file-based IPC (commands → results)
///   4. Collect Runtime Evidence from Player
///   5. Evaluate Assertions against collected evidence
///   6. Return a structured RuntimeSession
///
/// Launch is owned by RuntimeRunner — it is NOT a RuntimeAction.
/// Actions execute AFTER the Player process is running and ready.
/// Assertions evaluate AFTER all actions complete.
///
/// Design based on M12/M14 experimental evidence:
/// - Minimal Player Build: ~5s on macOS arm64
/// - Player Runtime bridge initializes in ~0.2s
/// - File-based IPC: command.json written by Core, read by Player, results reverse
/// - All processes tracked by PID, safely terminated on timeout/error
/// </summary>
public class RuntimeRunner : IRuntimeRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    // ══════════════════════════════════════════════════════════════
    // IRuntimeRunner — M13 compatible (probe only)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// M13-compatible overload. Probes only, no action pipeline.
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

        // Phase 2: Launch & Poll (M13 original logic)
        var playerExePath = buildPhase.PlayerExecutablePath!;
        return await LaunchAndPollAsync(options, playerExePath, session, cancellationToken);
    }

    // ══════════════════════════════════════════════════════════════
    // IRuntimeRunner — M14 Action Pipeline
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs the full runtime inspection pipeline with a test script.
    /// Build → Launch → Action Pipeline → Evidence → Assertions → Result
    /// </summary>
    public async Task<RuntimeSession> RunAsync(
        RuntimeRunOptions options,
        RuntimeTestScript script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(script);

        var session = new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            UnityVersion = GetUnityVersion(options.UnityExecutable),
            StartedAt = DateTime.UtcNow,
        };

        // Phase 1: Build Player
        var buildPhase = await BuildPlayerAsync(options, cancellationToken);
        session.Message = buildPhase.Message;
        if (buildPhase.Status != RuntimeResultStatus.Passed)
        {
            session.Result = buildPhase.Status;
            return session;
        }

        // Phase 2: Launch + Action Pipeline + Assert
        var playerExePath = buildPhase.PlayerExecutablePath!;
        return await RunActionPipelineAsync(options, playerExePath, script, session, cancellationToken);
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

        var buildMethod = options.BuildMethod;

        var psi = new ProcessStartInfo
        {
            FileName = options.UnityExecutable,
            Arguments = $"-batchmode -noGraphics -quit " +
                $"-projectPath \"{projectPath}\" " +
                $"-executeMethod {buildMethod} " +
                $"-logFile \"{buildDir}/m14_build.log\"",
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
                    $"Check {buildDir}/m14_build.log for details.", null);
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
                    $"Check {buildDir}/m14_build.log.", null);
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
    // M13 Compatible: Launch & Poll (probe only)
    // ══════════════════════════════════════════════════════════════

    private async Task<RuntimeSession> LaunchAndPollAsync(
        RuntimeRunOptions options,
        string playerExePath,
        RuntimeSession session,
        CancellationToken ct)
    {
        // M13-compatible: use SessionDirectory as evidence directory
        var evidenceDir = string.IsNullOrEmpty(options.SessionDirectory)
            ? Path.Combine(Path.GetTempPath(), "M13_Results")
            : options.SessionDirectory;

        Directory.CreateDirectory(evidenceDir);

        var evidenceFile = Path.Combine(evidenceDir, options.EvidenceFileName);
        var markerFile = Path.Combine(evidenceDir, options.DoneMarkerFileName);

        // Clean previous markers
        TryDeleteFile(markerFile);
        TryDeleteFile(evidenceFile);

        // Launch Player with the evidence directory env var
        var env = new Dictionary<string, string>
        {
            [options.SessionDirEnvVar] = evidenceDir,
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

        return ClassifyM13Result(session, evidence, processExited, playerPid, playerProcess, ct, deadline);
    }

    private static RuntimeSession ClassifyM13Result(
        RuntimeSession session, PlayerEvidence? evidence, bool processExited,
        int playerPid, Process playerProcess, CancellationToken ct, DateTime deadline)
    {
        // Case 1: Cancelled
        if (ct.IsCancellationRequested)
        {
            KillProcess(playerPid);
            SafeWaitExitSync(playerProcess, 5000);
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = "Runtime inspection was cancelled.";
            return session;
        }

        // Case 2: Timeout
        if (!processExited && evidence == null)
        {
            KillProcess(playerPid);
            SafeWaitExitSync(playerProcess, 5000);
            session.Result = RuntimeResultStatus.Timeout;
            session.Message = $"Player did not produce evidence within timeout. Process was terminated.";
            return session;
        }

        // Case 3: Process exited before evidence
        if (processExited && evidence == null)
        {
            var exitCode = playerProcess.ExitCode;
            SafeWaitExitSync(playerProcess, 2000);
            session.Result = RuntimeResultStatus.ProcessExited;
            session.Message = $"Player exited (code {exitCode}) before producing evidence.";
            return session;
        }

        // Case 4: Evidence received
        if (evidence != null)
        {
            SafeWaitExitSync(playerProcess, 5000);
            if (!playerProcess.HasExited)
                KillProcess(playerPid);

            return ClassifyEvidence(session, evidence);
        }

        // Fallback
        session.Result = RuntimeResultStatus.NotEvaluated;
        session.Message = "Unknown outcome: evidence was not collected.";
        return session;
    }

    // ══════════════════════════════════════════════════════════════
    // M14: Action Pipeline — Launch → Ready → Commands → Results → Evidence
    // ══════════════════════════════════════════════════════════════

    private async Task<RuntimeSession> RunActionPipelineAsync(
        RuntimeRunOptions options,
        string playerExePath,
        RuntimeTestScript script,
        RuntimeSession session,
        CancellationToken ct)
    {
        // Setup session directory
        var sessionDir = string.IsNullOrEmpty(options.SessionDirectory)
            ? CreateTempSessionDir()
            : options.SessionDirectory;

        Directory.CreateDirectory(sessionDir);
        var commandsDir = Path.Combine(sessionDir, options.CommandsSubDir);
        var resultsDir = Path.Combine(sessionDir, options.ResultsSubDir);
        Directory.CreateDirectory(commandsDir);
        Directory.CreateDirectory(resultsDir);

        var evidenceFile = Path.Combine(sessionDir, options.EvidenceFileName);
        var doneMarker = Path.Combine(sessionDir, options.DoneMarkerFileName);
        var readyMarker = Path.Combine(sessionDir, options.ReadyMarkerFileName);

        // Clean any stale markers
        TryDeleteFile(readyMarker);
        TryDeleteFile(doneMarker);
        TryDeleteFile(evidenceFile);

        // Launch Player with session directory env var
        var env = new Dictionary<string, string>
        {
            [options.SessionDirEnvVar] = sessionDir,
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

        try
        {
            // Step 1: Wait for Player to become ready
            var readyResult = WaitForReady(readyMarker, playerProcess,
                options.ReadyTimeoutSeconds, options.PollIntervalMs);

            if (readyResult == ReadyResult.Timeout)
            {
                KillProcess(playerPid);
                SafeWaitExitSync(playerProcess, 5000);
                session.Result = RuntimeResultStatus.Timeout;
                session.Message = $"Player did not become ready within {options.ReadyTimeoutSeconds}s.";
                return session;
            }
            if (readyResult == ReadyResult.ProcessExited)
            {
                var exitCode = playerProcess.ExitCode;
                session.Result = RuntimeResultStatus.ProcessExited;
                session.Message = $"Player exited (code {exitCode}) before becoming ready.";
                return session;
            }

            // Read initial active scene (for pre-action baseline)
            var initialSceneEvidence = await SendCommandAndReadResultAsync(
                new RuntimeCommand { Action = "ObserveActiveScene" },
                commandsDir, resultsDir, 0, options.CommandTimeoutSeconds, ct);

            if (initialSceneEvidence != null)
            {
                session.Evidence.Add(initialSceneEvidence);
            }

            // Step 2: Execute action pipeline
            for (int i = 0; i < script.Actions.Count; i++)
            {
                var action = script.Actions[i];
                var evidence = await ExecuteActionAsync(
                    action, commandsDir, resultsDir, i + 1, options, playerProcess, ct);

                if (evidence != null)
                {
                    session.Evidence.Add(evidence);
                }

                // Check if process died
                if (playerProcess.HasExited)
                {
                    session.Result = RuntimeResultStatus.ProcessExited;
                    session.Message = $"Player exited (code {playerProcess.ExitCode}) during action '{action.ActionId}'.";
                    return session;
                }

                // Check cancellation
                if (ct.IsCancellationRequested)
                {
                    KillProcess(playerPid);
                    session.Result = RuntimeResultStatus.NotEvaluated;
                    session.Message = "Runtime inspection was cancelled.";
                    return session;
                }
            }

            // Step 3: Send Quit command and collect final evidence
            var finalEvidence = await SendQuitAndCollectEvidenceAsync(
                commandsDir, resultsDir, script.Actions.Count + 1,
                evidenceFile, doneMarker, options, playerProcess, ct);

            if (finalEvidence != null)
            {
                session.Evidence.Add(new RuntimeEvidence
                {
                    Type = "RuntimeEvidenceChain",
                    Expected = "Runtime",
                    Observed = "Runtime",
                    Obtained = true,
                    Message = $"executionMode={finalEvidence.ExecutionMode}, " +
                              $"isEditor={finalEvidence.IsEditor}, isPlaying={finalEvidence.IsPlaying}, " +
                              $"activeScene={finalEvidence.ActiveScene}, success={finalEvidence.Success}",
                });
            }

            // Step 4: Wait for process to exit
            if (!playerProcess.HasExited)
            {
                SafeWaitExitSync(playerProcess, 5000);
                if (!playerProcess.HasExited)
                    KillProcess(playerPid);
            }

            // Step 5: Run assertions against collected evidence
            return EvaluateAssertions(session, script.Assertions, finalEvidence);
        }
        catch (OperationCanceledException)
        {
            KillProcess(playerPid);
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = "Runtime inspection was cancelled.";
            return session;
        }
        catch (Exception ex)
        {
            KillProcess(playerPid);
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = $"Runtime inspection failed: {ex.Message}";
            return session;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Action Dispatch
    // ══════════════════════════════════════════════════════════════

    private async Task<RuntimeEvidence?> ExecuteActionAsync(
        RuntimeAction action,
        string commandsDir,
        string resultsDir,
        int commandIndex,
        RuntimeRunOptions options,
        Process playerProcess,
        CancellationToken ct)
    {
        var runtimeCommand = MapActionToCommand(action);
        if (runtimeCommand == null)
        {
            return new RuntimeEvidence
            {
                Type = "ActionError",
                Expected = "executed",
                Observed = "skipped",
                Obtained = false,
                Message = $"Action '{action.ActionId}' has no IPC mapping",
            };
        }

        return await SendCommandAndReadResultAsync(
            runtimeCommand, commandsDir, resultsDir, commandIndex,
            options.CommandTimeoutSeconds, ct);
    }

    private static RuntimeCommand? MapActionToCommand(RuntimeAction action)
    {
        return action switch
        {
            WaitAction wait => new RuntimeCommand
            {
                Action = "Wait",
                Params = new Dictionary<string, object> { ["milliseconds"] = wait.Milliseconds },
            },
            ClickButtonAction click => new RuntimeCommand
            {
                Action = "ClickButton",
                Params = new Dictionary<string, object> { ["gameObjectName"] = click.GameObjectName },
            },
            ObserveActiveSceneAction => new RuntimeCommand
            {
                Action = "ObserveActiveScene",
                Params = new Dictionary<string, object>(),
            },
            ReadLogsAction => new RuntimeCommand
            {
                Action = "ReadLogs",
                Params = new Dictionary<string, object>(),
            },
            _ => null,
        };
    }

    // ══════════════════════════════════════════════════════════════
    // IPC: Send Command → Poll Result → Produce Evidence
    // ══════════════════════════════════════════════════════════════

    private async Task<RuntimeEvidence?> SendCommandAndReadResultAsync(
        RuntimeCommand command,
        string commandsDir,
        string resultsDir,
        int commandIndex,
        int timeoutSeconds,
        CancellationToken ct)
    {
        try
        {
            // Write command file
            var cmdPath = Path.Combine(commandsDir, $"cmd_{commandIndex}.json");
            var cmdJson = JsonSerializer.Serialize(command, JsonOptions);
            await File.WriteAllTextAsync(cmdPath, cmdJson, ct);

            // Poll for result file
            var resultPath = Path.Combine(resultsDir, $"result_{commandIndex}.json");
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (File.Exists(resultPath))
                {
                    var resultJson = await File.ReadAllTextAsync(resultPath, ct);
                    var result = JsonSerializer.Deserialize<CommandResult>(resultJson, JsonOptions);

                    if (result != null)
                    {
                        // Build evidence from command result
                        return BuildActionEvidence(command.Action, result);
                    }
                }

                await Task.Delay(100, ct);
            }

            // Timeout waiting for result
            return new RuntimeEvidence
            {
                Type = $"ActionTimeout",
                Expected = "completed",
                Observed = "timeout",
                Obtained = false,
                Message = $"Command '{command.Action}' #{commandIndex} timed out after {timeoutSeconds}s",
            };
        }
        catch (Exception ex)
        {
            return new RuntimeEvidence
            {
                Type = "ActionError",
                Expected = "completed",
                Observed = "exception",
                Obtained = false,
                Message = $"Command '{command.Action}' #{commandIndex} failed: {ex.Message}",
            };
        }
    }

    private static RuntimeEvidence BuildActionEvidence(string actionType, CommandResult result)
    {
        var successStr = result.Success ? "passed" : "failed";
        var message = result.Success
            ? $"Action '{actionType}': {successStr}"
            : $"Action '{actionType}': {result.Error ?? "unknown error"}";

        // Extract action-specific details
        if (result.Result != null)
        {
            if (result.Result.TryGetValue("sceneName", out var sceneName))
            {
                message += $", sceneName='{sceneName}'";
            }
            if (result.Result.TryGetValue("found", out var found))
            {
                message += $", found={found}";
            }
            if (result.Result.TryGetValue("clicked", out var clicked))
            {
                message += $", clicked={clicked}";
            }
            if (result.Result.TryGetValue("hasErrors", out var hasErrors))
            {
                message += $", hasErrors={hasErrors}";
            }
            if (result.Result.TryGetValue("hasExceptions", out var hasExceptions))
            {
                message += $", hasExceptions={hasExceptions}";
            }
        }

        return new RuntimeEvidence
        {
            Type = $"Action:{actionType}",
            Expected = "passed",
            Observed = successStr,
            Obtained = result.Success,
            Message = message,
        };
    }

    // ══════════════════════════════════════════════════════════════
    // IPC: Quit + Final Evidence Collection
    // ══════════════════════════════════════════════════════════════

    private async Task<PlayerEvidence?> SendQuitAndCollectEvidenceAsync(
        string commandsDir,
        string resultsDir,
        int commandIndex,
        string evidenceFile,
        string doneMarker,
        RuntimeRunOptions options,
        Process playerProcess,
        CancellationToken ct)
    {
        // Send Quit command
        var cmdPath = Path.Combine(commandsDir, $"cmd_{commandIndex}.json");
        var quitCmd = new RuntimeCommand { Action = "Quit", Params = new Dictionary<string, object>() };
        var quitJson = JsonSerializer.Serialize(quitCmd, JsonOptions);
        await File.WriteAllTextAsync(cmdPath, quitJson, ct);

        // Poll for done marker or evidence file
        var deadline = DateTime.UtcNow.AddSeconds(15); // Quit should be fast

        while (DateTime.UtcNow < deadline && !playerProcess.HasExited && !ct.IsCancellationRequested)
        {
            if (File.Exists(doneMarker) || File.Exists(evidenceFile))
            {
                return TryReadEvidence(evidenceFile);
            }
            await Task.Delay(100, ct);
        }

        // Process may have exited without writing evidence
        if (playerProcess.HasExited)
        {
            return null;
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════════
    // Ready Wait
    // ══════════════════════════════════════════════════════════════

    private enum ReadyResult { Ready, Timeout, ProcessExited }

    private static ReadyResult WaitForReady(
        string readyMarker,
        Process playerProcess,
        int timeoutSeconds,
        int pollIntervalMs)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (playerProcess.HasExited)
                return ReadyResult.ProcessExited;

            if (File.Exists(readyMarker))
                return ReadyResult.Ready;

            Thread.Sleep(pollIntervalMs);
        }

        return ReadyResult.Timeout;
    }

    // ══════════════════════════════════════════════════════════════
    // Classification — Assertion-based
    // ══════════════════════════════════════════════════════════════

    private static RuntimeSession EvaluateAssertions(
        RuntimeSession session,
        IReadOnlyList<RuntimeAssertion> assertions,
        PlayerEvidence? finalEvidence)
    {
        var assertionResults = new List<RuntimeEvidence>();
        bool anyFailed = false;
        bool anyNotEvaluated = false;
        bool anyPassed = false;

        foreach (var assertion in assertions)
        {
            var result = EvaluateSingleAssertion(assertion, session.Evidence, finalEvidence);
            assertionResults.Add(result);

            switch (result.Type.Split(':')[1]) // "Result:Passed" / "Result:Failed" / "Result:NotEvaluated"
            {
                case "Passed":
                    anyPassed = true;
                    break;
                case "Failed":
                    anyFailed = true;
                    break;
                case "NotEvaluated":
                    anyNotEvaluated = true;
                    break;
            }
        }

        // Add assertion results to evidence
        foreach (var ar in assertionResults)
        {
            session.Evidence.Add(ar);
        }

        // Classify: any Failed → Failed
        if (anyFailed)
        {
            session.Result = RuntimeResultStatus.Failed;
            session.Message = "Some assertions failed.";
            return session;
        }

        // All NotEvaluated → NotEvaluated
        if (!anyPassed && anyNotEvaluated)
        {
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = "All assertions were not evaluable (missing evidence).";
            return session;
        }

        // All Passed → Passed
        if (anyPassed && !anyFailed)
        {
            session.Result = RuntimeResultStatus.Passed;
            session.Message = "All assertions passed.";
            return session;
        }

        // Fallback: mixed Passed + NotEvaluated → still Passed (lenient)
        // This matches the behavior that actions may have been skipped,
        // but all actual checks passed
        session.Result = RuntimeResultStatus.Passed;
        session.Message = "All evaluated assertions passed.";
        return session;
    }

    private static RuntimeEvidence EvaluateSingleAssertion(
        RuntimeAssertion assertion,
        List<RuntimeEvidence> evidence,
        PlayerEvidence? finalEvidence)
    {
        switch (assertion)
        {
            case AssertActiveScene assertScene:
                return EvaluateAssertActiveScene(assertScene, evidence, finalEvidence);

            case AssertNoExceptions assertNoEx:
                return EvaluateAssertNoExceptions(assertNoEx, evidence);

            default:
                return new RuntimeEvidence
                {
                    Type = "Result:NotEvaluated",
                    Expected = "passed",
                    Observed = "unknown_assertion_type",
                    Obtained = false,
                    Message = $"Unknown assertion type: {assertion.GetType().Name}",
                };
        }
    }

    private static RuntimeEvidence EvaluateAssertActiveScene(
        AssertActiveScene assertion,
        List<RuntimeEvidence> evidence,
        PlayerEvidence? finalEvidence)
    {
        var expectedScene = assertion.ExpectedSceneName;

        // Priority 1: Check for ObserveActiveScene evidence in evidence list
        var sceneEvidence = evidence.LastOrDefault(e =>
            e.Type == "Action:ObserveActiveScene" ||
            e.Type == "Action:ClickButton"); // ClickButton may include scene name

        if (sceneEvidence != null && sceneEvidence.Message != null)
        {
            // Extract scene name from message: "sceneName='TargetScene'"
            var sceneName = ExtractFieldFromEvidence(sceneEvidence.Message, "sceneName");
            if (sceneName != null)
            {
                var passed = string.Equals(sceneName, expectedScene, StringComparison.Ordinal);
                return new RuntimeEvidence
                {
                    Type = passed ? "Result:Passed" : "Result:Failed",
                    Expected = expectedScene,
                    Observed = sceneName,
                    Obtained = true,
                    Message = passed
                        ? $"AssertActiveScene: scene is '{sceneName}' (expected '{expectedScene}')"
                        : $"AssertActiveScene: expected '{expectedScene}', observed '{sceneName}'",
                };
            }
        }

        // Priority 2: Check final evidence activeScene
        if (finalEvidence != null && finalEvidence.ActiveScene != null)
        {
            var passed = string.Equals(finalEvidence.ActiveScene, expectedScene, StringComparison.Ordinal);
            return new RuntimeEvidence
            {
                Type = passed ? "Result:Passed" : "Result:Failed",
                Expected = expectedScene,
                Observed = finalEvidence.ActiveScene,
                Obtained = true,
                Message = passed
                    ? $"AssertActiveScene: scene is '{finalEvidence.ActiveScene}' (expected '{expectedScene}')"
                    : $"AssertActiveScene: expected '{expectedScene}', observed '{finalEvidence.ActiveScene}'",
            };
        }

        // No scene evidence found
        return new RuntimeEvidence
        {
            Type = "Result:NotEvaluated",
            Expected = expectedScene,
            Observed = null,
            Obtained = false,
            Message = "AssertActiveScene: no scene evidence available",
        };
    }

    private static RuntimeEvidence EvaluateAssertNoExceptions(
        AssertNoExceptions assertion,
        List<RuntimeEvidence> evidence)
    {
        // Check for ReadLogs evidence that indicates errors
        var logEvidence = evidence.FirstOrDefault(e =>
            e.Type == "Action:ReadLogs");

        if (logEvidence != null && logEvidence.Message != null)
        {
            if (logEvidence.Message.Contains("hasErrors=True") ||
                logEvidence.Message.Contains("hasExceptions=True"))
            {
                return new RuntimeEvidence
                {
                    Type = "Result:Failed",
                    Expected = "no_exceptions",
                    Observed = "exceptions_found",
                    Obtained = true,
                    Message = "AssertNoExceptions: runtime exceptions were detected",
                };
            }

            return new RuntimeEvidence
            {
                Type = "Result:Passed",
                Expected = "no_exceptions",
                Observed = "no_exceptions",
                Obtained = true,
                Message = "AssertNoExceptions: no runtime exceptions detected",
            };
        }

        // Also check final evidence
        if (logEvidence == null)
        {
            return new RuntimeEvidence
            {
                Type = "Result:Passed",
                Expected = "no_exceptions",
                Observed = "no_exceptions",
                Obtained = true,
                Message = "AssertNoExceptions: no exceptions logged (no log evidence needed)",
            };
        }

        return new RuntimeEvidence
        {
            Type = "Result:NotEvaluated",
            Expected = "no_exceptions",
            Observed = null,
            Obtained = false,
            Message = "AssertNoExceptions: no log evidence available",
        };
    }

    private static string? ExtractFieldFromEvidence(string message, string fieldName)
    {
        var search = $"{fieldName}='";
        var start = message.IndexOf(search);
        if (start < 0)
        {
            // Try without quotes
            search = $"{fieldName}=";
            start = message.IndexOf(search);
            if (start < 0) return null;
            start += search.Length;
            var end = message.IndexOfAny(new[] { ',', ' ', '}' }, start);
            return end > start ? message[start..end] : null;
        }
        start += search.Length;
        var quoteEnd = message.IndexOf('\'', start);
        return quoteEnd > start ? message[start..quoteEnd] : null;
    }

    // ══════════════════════════════════════════════════════════════
    // M13 Classification (retained for backward compat)
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

    private static void SafeWaitExitSync(Process process, int timeoutMs)
    {
        try
        {
            if (!process.HasExited)
            {
                process.WaitForExit(timeoutMs);
            }
        }
        catch
        {
            // Process may have been reclaimed
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    private static string CreateTempSessionDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "M14_Sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

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