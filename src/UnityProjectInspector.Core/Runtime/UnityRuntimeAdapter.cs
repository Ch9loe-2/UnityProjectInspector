using System.Diagnostics;
using System.Text.Json;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Adapter that launches the Unity Editor with the RuntimeTestHarness project,
/// waits for it to complete, and collects runtime evidence.
///
/// This adapter does NOT modify the target Unity project.
/// It uses a separate harness project and passes the target scene path
/// via a temporary config file.
/// </summary>
public class UnityRuntimeAdapter
{
    private readonly string _unityExecutablePath;
    private readonly string _harnessProjectPath;

    /// <summary>
    /// Creates a new adapter.
    /// </summary>
    /// <param name="unityExecutablePath">
    /// Full path to the Unity Editor executable.
    /// Example: /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity
    /// </param>
    /// <param name="harnessProjectPath">
    /// Full path to the RuntimeTestHarness Unity project directory.
    /// </param>
    public UnityRuntimeAdapter(string unityExecutablePath, string harnessProjectPath)
    {
        _unityExecutablePath = unityExecutablePath ?? throw new ArgumentNullException(nameof(unityExecutablePath));
        _harnessProjectPath = harnessProjectPath ?? throw new ArgumentNullException(nameof(harnessProjectPath));
    }

    /// <summary>
    /// Configuration for a single runtime test run.
    /// </summary>
    public record class RuntimeTestConfig
    {
        /// <summary>Full path to the target .unity scene file to load.</summary>
        public required string ScenePath { get; init; }

        /// <summary>Expected active scene name after entering Play Mode.</summary>
        public required string ExpectedScene { get; init; }

        /// <summary>
        /// Timeout in seconds for the entire Unity session.
        /// Default: 240 seconds (first cold start can take 90–180s).
        /// </summary>
        public int TimeoutSeconds { get; init; } = 240;
    }

    /// <summary>
    /// Runs a runtime test: launches Unity, waits for completion, collects evidence.
    /// </summary>
    public async Task<RuntimeSession> RunAsync(RuntimeTestConfig config, CancellationToken ct = default)
    {
        var session = new RuntimeSession
        {
            ProjectPath = _harnessProjectPath,
            UnityVersion = GetUnityVersion(),
            StartedAt = DateTime.UtcNow,
        };

        // 1. Create temp files for config and output
        var configDir = Path.Combine(Path.GetTempPath(), "UnityProjectInspector_RuntimeTest");
        Directory.CreateDirectory(configDir);
        var configFilePath = Path.Combine(configDir, $"config_{Guid.NewGuid():N}.json");
        var outputFilePath = Path.Combine(configDir, $"output_{Guid.NewGuid():N}.json");

        try
        {
            // 2. Write config file for the harness Editor script
            var configData = new HarnessConfig
            {
                scenePath = config.ScenePath,
                outputPath = outputFilePath,
            };
            var configJson = JsonSerializer.Serialize(configData);
            await File.WriteAllTextAsync(configFilePath, configJson, ct);

            // 3. Launch Unity Editor
            var startInfo = new ProcessStartInfo
            {
                FileName = _unityExecutablePath,
                Arguments = $"-projectPath \"{_harnessProjectPath}\" -batchmode -noGraphics -executeMethod RuntimeTestRunner.RunEntry -RuntimeConfig \"{configFilePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            // Log launch info
            var launchInfo = $"""
                Unity: {_unityExecutablePath}
                Project: {_harnessProjectPath}
                Target Scene: {config.ScenePath}
                Expected: {config.ExpectedScene}
                Config: {configFilePath}
                Output: {outputFilePath}
                """;

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // 4. Poll for output file OR process exit (Unity sometimes hangs on
            //    EditorApplication.Exit() in batchmode, but writes the result first)
            var deadline = DateTime.UtcNow.AddSeconds(config.TimeoutSeconds);
            RuntimeSession? pollResult = null;

            while (DateTime.UtcNow < deadline)
            {
                // Did the process exit?
                if (process.HasExited)
                    break;

                // Did the output file appear?
                if (File.Exists(outputFilePath))
                {
                    // Read result immediately before killing
                    var outputJson = await File.ReadAllTextAsync(outputFilePath, ct);
                    var result = JsonSerializer.Deserialize<HarnessResult>(outputJson);
                    if (result != null)
                    {
                        pollResult = ClassifyResult(session, config, result);
                    }

                    // Kill Unity (hung on Exit())
                    try { process.Kill(entireProcessTree: true); } catch { }
                    await process.WaitForExitAsync(CancellationToken.None);
                    break;
                }

                // Wait a bit before next poll
                await Task.Delay(200, ct);
            }

            // Process completed normally (not killed by us)
            if (pollResult == null)
            {
                // Wait for process to exit if it hasn't already
                if (!process.HasExited)
                {
                    var exited = process.WaitForExit(
                        (int)(deadline - DateTime.UtcNow).TotalMilliseconds);

                    if (!exited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        await process.WaitForExitAsync(CancellationToken.None);

                        session.Result = RuntimeResultStatus.NotEvaluated;
                        session.Message = $"Unity Editor did not complete within {config.TimeoutSeconds}s timeout.";
                        return session;
                    }
                }

                // 5. Read output file (process exited normally)
                if (File.Exists(outputFilePath))
                {
                    var outputJson = await File.ReadAllTextAsync(outputFilePath, ct);
                    var result = JsonSerializer.Deserialize<HarnessResult>(outputJson);
                    pollResult = ClassifyResult(session, config, result);

                    if (pollResult == null)
                    {
                        session.Result = RuntimeResultStatus.NotEvaluated;
                        session.Message = "Failed to parse Unity harness output.";
                    }
                }
                else
                {
                    session.Result = RuntimeResultStatus.NotEvaluated;
                    session.Message = "Unity harness did not produce output file.";
                }
            }

            // Merge pollResult into session
            if (pollResult != null)
            {
                session.Result = pollResult.Result;
                session.Message = pollResult.Message;
                session.Evidence.Clear();
                foreach (var e in pollResult.Evidence)
                    session.Evidence.Add(e);
            }

            // 6. Check exit code
            if (process.ExitCode != 0 && session.Result == RuntimeResultStatus.NotEvaluated)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                session.Message = $"Unity exited with code {process.ExitCode}: {stderr.Trim()}";
            }
        }
        catch (OperationCanceledException)
        {
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = "Runtime test was cancelled.";
        }
        catch (Exception ex)
        {
            session.Result = RuntimeResultStatus.NotEvaluated;
            session.Message = $"Runtime test failed with exception: {ex.Message}";
        }
        finally
        {
            // Clean up temp files
            try { if (File.Exists(configFilePath)) File.Delete(configFilePath); } catch { }
            try { if (File.Exists(outputFilePath)) File.Delete(outputFilePath); } catch { }
        }

        return session;
    }

    private string GetUnityVersion()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _unityExecutablePath,
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

    private static RuntimeSession? ClassifyResult(
        RuntimeSession session, RuntimeTestConfig config, HarnessResult? result)
    {
        if (result == null) return null;

        var observedScene = result.activeScene ?? string.Empty;

        var evidence = new RuntimeEvidence
        {
            Type = "ActiveScene",
            Expected = config.ExpectedScene,
            Observed = observedScene,
            Obtained = !observedScene.StartsWith("ERROR_") && !observedScene.StartsWith("TIMEOUT_"),
            Message = observedScene.StartsWith("ERROR_")
                ? $"Unity reported error: {observedScene}"
                : observedScene.StartsWith("TIMEOUT_")
                    ? $"Phase timed out: {observedScene}"
                    : $"Active scene is '{observedScene}'",
        };

        var resultSession = new RuntimeSession
        {
            ProjectPath = session.ProjectPath,
            UnityVersion = session.UnityVersion,
            StartedAt = session.StartedAt,
        };
        resultSession.Evidence.Add(evidence);

        if (!evidence.Obtained)
        {
            resultSession.Result = RuntimeResultStatus.NotEvaluated;
            resultSession.Message = evidence.Message;
        }
        else if (string.Equals(observedScene, config.ExpectedScene, StringComparison.Ordinal))
        {
            resultSession.Result = RuntimeResultStatus.Passed;
            resultSession.Message = $"Runtime POC passed: ActiveScene = '{observedScene}'";
        }
        else
        {
            resultSession.Result = RuntimeResultStatus.Failed;
            resultSession.Message = $"Runtime POC failed: expected '{config.ExpectedScene}', observed '{observedScene}'";
        }

        return resultSession;
    }
    /// <summary>
    /// Determines whether the Unity Editor executable exists and can be launched.
    /// </summary>
    public bool IsAvailable()
    {
        try
        {
            return File.Exists(_unityExecutablePath);
        }
        catch
        {
            return false;
        }
    }

    // Serialization types matching the harness script

    private class HarnessConfig
    {
        public string? scenePath { get; set; }
        public string? outputPath { get; set; }
    }

    private class HarnessResult
    {
        public string? activeScene { get; set; }
    }
}