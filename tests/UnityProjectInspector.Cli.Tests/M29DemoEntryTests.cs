using System.Runtime.Versioning;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M29 one-click demo entry-point tests. The Demo.command is a macOS
/// presentation script that orchestrates the CLI against the bundled
/// fixture and example assignments.
///
/// These tests verify the demo shell script is present, well-formed,
/// and references correct existing assets.
/// </summary>
public class M29DemoEntryTests
{
    private static string RepoRoot()
    {
        var dir = Path.GetDirectoryName(AppContext.BaseDirectory)!;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "UnityProjectInspector.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Repo root (UnityProjectInspector.slnx) not found.");
    }

    private static string DemoScriptPath => Path.Combine(RepoRoot(), "Demo.command");

    // ─── File exists and is well-formed ────────────────────────

    [Fact]
    public void DemoCommand_Exists()
    {
        Assert.True(File.Exists(DemoScriptPath), "Demo.command must exist at repo root");
    }

    [Fact]
    public void DemoCommand_HasCorrectShebang()
    {
        var firstLine = File.ReadLines(DemoScriptPath).First();
        Assert.StartsWith("#!/bin/bash", firstLine.Trim());
    }

    [Fact]
    [SupportedOSPlatform("macOS")]
    public void DemoCommand_IsExecutable()
    {
        // On macOS, .command files need +x for double-click to work.
        var perms = File.GetUnixFileMode(DemoScriptPath);
        Assert.True(
            (perms & UnixFileMode.UserExecute) != 0,
            "Demo.command must have the executable bit set (chmod +x).");
    }

    // ─── No real machine paths ─────────────────────────────────

    [Fact]
    public void DemoCommand_NoAbsoluteUserPaths()
    {
        var content = File.ReadAllText(DemoScriptPath);

        var forbidden = new[]
        {
            "/Users/tangluyi",
            "/Users/Chloe",
            "3DIndustrialMonitor",
            "VRDeviceTraining",
        };
        foreach (var pattern in forbidden)
        {
            if (content.Contains(pattern, StringComparison.Ordinal))
            {
                Assert.Fail($"Demo.command must not contain hardcoded path: '{pattern}'");
            }
        }
    }

    // ─── References correct existing assets ────────────────────

    [Fact]
    public void DemoCommand_ReferencesCorrectFixture()
    {
        var content = File.ReadAllText(DemoScriptPath);
        Assert.Contains("MinimalUnityProject", content, StringComparison.Ordinal);
        Assert.Contains("FIXTURE_DIR", content, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoCommand_ReferencesCorrectAssignments()
    {
        var content = File.ReadAllText(DemoScriptPath);
        Assert.Contains("scene-check.json", content, StringComparison.Ordinal);
        Assert.Contains("static-fail.json", content, StringComparison.Ordinal);
    }

    [Fact]
    public void DemoCommand_ReferencesCorrectCliProject()
    {
        var content = File.ReadAllText(DemoScriptPath);
        Assert.Contains("UnityProjectInspector.Cli", content, StringComparison.Ordinal);
    }

    // ─── Structural integrity ──────────────────────────────────

    [Fact]
    public void DemoCommand_HasExpectedStructure()
    {
        var content = File.ReadAllText(DemoScriptPath);

        // Must have build phase
        Assert.Contains("dotnet build", content, StringComparison.Ordinal);

        // Must have success case (scene-check → exit 0)
        Assert.Contains("scene-check.json", content, StringComparison.Ordinal);
        Assert.Contains("Static Inspection", content, StringComparison.Ordinal);

        // Must have failure case (static-fail → exit 1)
        Assert.Contains("static-fail.json", content, StringComparison.Ordinal);
        Assert.Contains("Failure Detection", content, StringComparison.Ordinal);

        // Must have summary and wait for user
        Assert.Contains("Demo Complete", content, StringComparison.Ordinal);
        Assert.Contains("close this window", content, StringComparison.Ordinal);

        // Must exit 0 on success
        Assert.Contains("FINAL_EXIT", content, StringComparison.Ordinal);

        // Must not hardcode real user paths
        Assert.DoesNotContain("tangluyi", content, StringComparison.Ordinal);
    }
}