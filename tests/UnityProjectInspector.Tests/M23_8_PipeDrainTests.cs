using System.Diagnostics;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// M23.8 — Pipe drain tests for RuntimeRunner stdout/stderr redirection.
///
/// Verifies that processes with RedirectStandardOutput=true and
/// RedirectStandardError=true do NOT deadlock when producing large
/// volumes of output, provided the pipes are drained asynchronously.
///
/// These tests use a real subprocess (python3 or /bin/bash) to generate
/// controlled output volumes. They do NOT launch Unity.
///
/// The test pattern directly mirrors RuntimeRunner.StartPipeDrain:
///   process.BeginOutputReadLine() + process.BeginErrorReadLine()
/// with no-op event handlers.
/// </summary>
[Trait("Category", "Unit")]
public class M23_8_PipeDrainTests
{
    private const string Python3 = "/usr/bin/python3";

    /// <summary>
    /// Test 1: Large stdout volume does NOT deadlock.
    ///
    /// Child process writes 50,000 lines (~2MB) to stdout.
    /// With unbuffered pipe, this simulates a Unity Player producing
    /// continuous Debug.Log output (the M23.7 root cause scenario).
    /// </summary>
    [Fact]
    public void PipeDrain_LargeStdout_ProcessCompletes()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; [print(f'line {i} ' * 20) for i in range(50000)]; sys.stdout.flush()\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Drain stdout/stderr with no-op event handlers (exactly like StartPipeDrain)
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Process must exit without deadlock
        var exited = process.WaitForExit(30000);
        Assert.True(exited, "Process with large stdout must exit within 30s (no deadlock)");
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Test 2: Large stderr volume does NOT deadlock.
    ///
    /// Child process writes 50,000 lines (~2MB) to stderr.
    /// </summary>
    [Fact]
    public void PipeDrain_LargeStderr_ProcessCompletes()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; [print(f'error {i} ' * 20, file=sys.stderr) for i in range(50000)]; sys.stderr.flush()\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(30000);
        Assert.True(exited, "Process with large stderr must exit within 30s (no deadlock)");
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Test 3: stdout AND stderr simultaneously does NOT deadlock.
    ///
    /// Child process interleaves 25,000 lines to both stdout and stderr.
    /// This is the most stressful scenario for pipe drain.
    /// </summary>
    [Fact]
    public void PipeDrain_StdoutAndStderr_ProcessCompletes()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"import sys; [print(f'out {i}') or print(f'err {i}', file=sys.stderr) for i in range(25000)]; sys.stdout.flush(); sys.stderr.flush()\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(30000);
        Assert.True(exited, "Process with both stdout+stderr must exit within 30s (no deadlock)");
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Test 4: Small output also works (regression — verify no negative impact).
    /// </summary>
    [Fact]
    public void PipeDrain_SmallOutput_ProcessCompletes()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-c \"print('hello world')\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(10000);
        Assert.True(exited, "Small output process must exit normally");
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Test 5: Without pipe drain, large stdout blocks (negative test).
    ///
    /// Verifies the bug condition exists: a stdout-spamming process
    /// WITHOUT BeginOutputReadLine will deadlock when output exceeds
    /// the OS pipe buffer (~8MB on macOS).
    ///
    /// Uses Python -u (unbuffered) and 300,000 lines (~10.5MB)
    /// to reliably exceed the pipe buffer.
    ///
    /// NOTE: This test is system-dependent. On systems with very large
    /// pipe buffers (or if Python manages to flush through), it may
    /// be flaky. It exists to document the root cause, not as a
    /// gate for CI.
    /// </summary>
    [Fact]
    public void PipeDrain_Negative_WithoutDrain_BlocksOnStdout()
    {
        var psi = new ProcessStartInfo
        {
            FileName = Python3,
            Arguments = "-u -c \"import sys; [print('x' * 35) for _ in range(300000)]; sys.stdout.flush()\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Do NOT call BeginOutputReadLine — simulate M23.7 bug
        // Do NOT call BeginErrorReadLine

        // Process should NOT exit within 5s (pipe fills, process blocks)
        var exited = process.WaitForExit(5000);
        Assert.False(exited, "Without pipe drain, process should block (pipe full)");

        // Cleanup
        KillProcess(process);
        process.WaitForExit(3000);
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