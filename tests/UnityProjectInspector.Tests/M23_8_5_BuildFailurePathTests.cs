using System.Diagnostics;
using System.Text;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// M23.8.5 — Build failure path tests for RuntimeRunner BuildPlayerAsync.
///
/// Verifies that after StartPipeDrain with captureStderr StringBuilder:
///   - Non-zero exit codes can retrieve stderr content from the captured buffer
///     WITHOUT calling StandardError.ReadToEndAsync (which throws
///     InvalidOperationException when mixed with BeginErrorReadLine).
///   - Zero exit codes work normally (no false failures).
///   - Large stderr does NOT deadlock the process.
///   - Timeout path is clean (no pipe read involved).
///
/// These tests use a real subprocess (python3 or /bin/bash) to generate
/// controlled output with specific exit codes.
///
/// The test pattern directly mirrors BuildPlayerAsync's Process lifecycle:
///   process.Start() → StartPipeDrain(process, captureStderr) → WaitForExit()
///   → if exitCode != 0: read stderr from capture.ToString()
/// </summary>
[Trait("Category", "Unit")]
public class M23_8_5_BuildFailurePathTests
{
    private const string Python3 = "/usr/bin/python3";

    /// <summary>
    /// Test 1: Stdout-only, non-zero exit.
    ///
    /// Process writes to stdout only and exits with code 1.
    /// StartPipeDrain(captureStderr) should capture nothing from stdout,
    /// and the captured stderr buffer should be empty.
    /// No InvalidOperationException should occur.
    /// </summary>
    [Fact]
    public void BuildFailurePath_StdoutOnly_NonZeroExit_StderrCaptureEmpty()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; print('hello stdout'); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(1, process.ExitCode);

        // Build failure path: read from captured buffer, NOT StandardError.ReadToEndAsync
        var stderr = capture.ToString();
        Assert.Equal("", stderr.Trim());
    }

    /// <summary>
    /// Test 2: Stderr-only, non-zero exit.
    ///
    /// Process writes to stderr and exits with code 1.
    /// The captured stderr buffer should contain the error message.
    /// </summary>
    [Fact]
    public void BuildFailurePath_StderrOnly_NonZeroExit_StderrCaptured()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; print('error in build', file=sys.stderr); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(1, process.ExitCode);

        var stderr = capture.ToString();
        Assert.Contains("error in build", stderr);
    }

    /// <summary>
    /// Test 3: Stdout and stderr simultaneously, non-zero exit.
    ///
    /// Process writes to both pipes and exits with code 1.
    /// The captured stderr buffer should contain only stderr content,
    /// not stdout content.
    /// </summary>
    [Fact]
    public void BuildFailurePath_StdoutAndStderr_NonZeroExit_OnlyStderrCaptured()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; print('stdout line'); print('stderr line', file=sys.stderr); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(1, process.ExitCode);

        var stderr = capture.ToString();
        Assert.Contains("stderr line", stderr);
        Assert.DoesNotContain("stdout line", stderr);
    }

    /// <summary>
    /// Test 4: Zero exit code — stderr capture should be empty.
    ///
    /// This mirrors the success path: BuildPlayerAsync returns BuildPhaseResult.Passed
    /// and never reads the captured buffer.
    /// </summary>
    [Fact]
    public void BuildFailurePath_ZeroExit_Success_CaptureBufferEmpty()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"print('success')\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(0, process.ExitCode);

        // Success path: we never read the buffer
        var stderr = capture.ToString();
        Assert.Equal("", stderr.Trim());
    }

    /// <summary>
    /// Test 5: Large stderr volume with non-zero exit.
    ///
    /// 50,000 lines (~2MB) of stderr, exit code 1.
    /// No deadlock, captured buffer contains all stderr.
    /// </summary>
    [Fact]
    public void BuildFailurePath_LargeStderr_NonZeroExit_NoDeadlock()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; [print(f'error {i} ' * 20, file=sys.stderr) for i in range(50000)]; sys.stderr.flush(); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(60000);
        Assert.True(exited, "Process with large stderr must exit within 60s (no deadlock)");
        Assert.Equal(1, process.ExitCode);

        var stderr = capture.ToString();
        Assert.Contains("error 0", stderr);
        Assert.Contains("error 49999", stderr);
    }

    /// <summary>
    /// Test 6: Large stdout volume with non-zero exit, no stderr.
    ///
    /// 50,000 lines (~2MB) of stdout, exit code 1.
    /// Stdout is drained (discarded by StartPipeDrain), stderr buffer empty.
    /// No deadlock.
    /// </summary>
    [Fact]
    public void BuildFailurePath_LargeStdout_NonZeroExit_NoDeadlock()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; [print(f'line {i} ' * 20) for i in range(50000)]; sys.stdout.flush(); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(60000);
        Assert.True(exited, "Process with large stdout must exit within 60s (no deadlock)");
        Assert.Equal(1, process.ExitCode);

        var stderr = capture.ToString();
        Assert.Equal("", stderr.Trim());
    }

    /// <summary>
    /// Test 7: Large stdout AND stderr simultaneously with non-zero exit.
    ///
    /// Interleaved large output to both pipes. Most stressful scenario.
    /// Only stderr captured, stdout drained.
    /// No deadlock.
    /// </summary>
    [Fact]
    public void BuildFailurePath_LargeBoth_NonZeroExit_NoDeadlock()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; [print(f'out {i}') or print(f'err {i}', file=sys.stderr) for i in range(25000)]; sys.stdout.flush(); sys.stderr.flush(); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(60000);
        Assert.True(exited, "Process with both large stdout+stderr must exit within 60s");
        Assert.Equal(1, process.ExitCode);

        var stderr = capture.ToString();
        Assert.Contains("err 0", stderr);
        Assert.Contains("err 24999", stderr);
        Assert.DoesNotContain("out ", stderr);
    }

    /// <summary>
    /// Test 8: Timeout — process does NOT exit.
    ///
    /// Verifies that StartPipeDrain + captureStderr doesn't interfere with
    /// the timeout path (which calls KillProcess + WaitForExitAsync, no pipe reads).
    /// </summary>
    [Fact]
    public void BuildFailurePath_Timeout_ProcessKilled_NoDeadlock()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import time; time.sleep(30)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        // Short timeout to force timeout
        var exited = process.WaitForExit(1000);
        Assert.False(exited, "Process should not exit within 1s (timeout expected)");

        // Kill process (simulating BuildPlayerAsync timeout path)
        KillProcess(process);
        process.WaitForExit(5000);
        Assert.True(process.HasExited);

        // Timeout path: we normally DON'T read the buffer
        // (BuildPlayerAsync returns timeout error message referencing build log file)
        var stderr = capture.ToString();
        Assert.Equal("", stderr.Trim());
    }

    /// <summary>
    /// Test 9: Negative test — mixing StartPipeDrain with StandardError.ReadToEndAsync
    ///          MUST throw InvalidOperationException.
    ///
    /// This documents that the old pattern (M23.7/M23.8 bug) is broken,
    /// and M23.8.5 fix correctly avoids it.
    /// </summary>
    [Fact]
    public void BuildFailurePath_Negative_MixingBeginErrorReadLineAndReadToEndAsync_Throws()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; print('error msg', file=sys.stderr); sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        RuntimeRunner.StartPipeDrain(process); // starts BeginErrorReadLine

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");

        // This is the OLD bug pattern: ReadToEndAsync after BeginErrorReadLine
        var ex = Record.Exception(() =>
        {
            var task = process.StandardError.ReadToEndAsync();
            task.Wait(5000);
        });

        Assert.NotNull(ex);
        Assert.Contains("Cannot mix synchronous and asynchronous operation on process stream",
            ex.ToString());
    }

    /// <summary>
    /// Test 10: Non-zero exit with minimal output — no empty string issue.
    ///
    /// Process exits with code 1 but writes nothing to stderr.
    /// Captured buffer should be empty; Trim() on empty is safe.
    /// </summary>
    [Fact]
    public void BuildFailurePath_NonZeroExitEmptyStderr_CaptureIsEmpty()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; sys.exit(1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(1, process.ExitCode);

        var stderr = capture.ToString();
        // BuildPlayerAsync does: $"... {stderr.Trim()}"
        // Empty trim is safe; produces "Build failed with exit code 1. "
        Assert.Equal("", stderr.Trim());
    }

    /// <summary>
    /// Test 11: StartPipeDrain without captureStderr parameter still works (regression).
    ///
    /// This is the mode used by StartProcess for Player launch.
    /// Must not break existing behavior.
    /// </summary>
    [Fact]
    public void BuildFailurePath_NoCaptureParam_ExistingBehaviorPreserved()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; print('hello'); print('error', file=sys.stderr); sys.exit(0)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        // Call without captureStderr (existing Player launch pattern)
        RuntimeRunner.StartPipeDrain(process); // No capture param

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Test 12: Non-zero exit reads stderr correctly after pipe drain completes.
    ///
    /// Verifies that WaitForExit followed by reading the captured StringBuilder
    /// is safe — all ErrorDataReceived events have been dispatched before
    /// WaitForExit returns (the event pump drains on process exit).
    /// </summary>
    [Fact]
    public void BuildFailurePath_WaitForExitThenRead_StderrComplete()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; print('err msg', file=sys.stderr); print('err2', file=sys.stderr); sys.exit(2)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var capture = new StringBuilder();
        RuntimeRunner.StartPipeDrain(process, capture);

        var exited = process.WaitForExit(15000);
        Assert.True(exited, "Process must exit within 15s");
        Assert.Equal(2, process.ExitCode);

        var stderr = capture.ToString();
        Assert.Contains("err msg", stderr);
        Assert.Contains("err2", stderr);
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}