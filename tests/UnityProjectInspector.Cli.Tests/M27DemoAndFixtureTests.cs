using System.Text.Json;
using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M27 reproducibility tests: the bundled minimal Unity fixture and the example
/// assignments must produce stable, environment-independent results so the README
/// Quick Start cannot silently drift. These tests use repo-relative paths only
/// (no /tmp, no user machine paths, no Unity Editor).
/// </summary>
public class M27DemoAndFixtureTests
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

    private static async Task<(int exit, string stdout, string stderr)> RunCliAsync(params string[] args)
    {
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exit = await Program.Main(args);
            return (exit, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    private static string FixtureDir() => Path.Combine(RepoRoot(), "tests", "fixtures", "MinimalUnityProject");
    private static string ExamplePath(string name) => Path.Combine(RepoRoot(), "examples", "assignments", name);

    // ─── Fixture integrity ─────────────────────────────────────

    [Fact]
    public void Fixture_HasMinimalUnityProjectStructure()
    {
        var root = FixtureDir();
        Assert.True(Directory.Exists(root), $"Fixture missing: {root}");
        Assert.True(Directory.Exists(Path.Combine(root, "Assets")), "Fixture missing Assets/");
        Assert.True(Directory.Exists(Path.Combine(root, "ProjectSettings")), "Fixture missing ProjectSettings/");
        Assert.True(Directory.Exists(Path.Combine(root, "Packages")), "Fixture missing Packages/");

        var scenes = Directory.GetFiles(
            Path.Combine(root, "Assets"), "*.unity", SearchOption.AllDirectories);
        Assert.Contains(scenes, s => Path.GetFileNameWithoutExtension(s) == "TrainingScene");
    }

    // ─── Demo success (exit 0) ─────────────────────────────────

    [Fact]
    public async Task DemoSuccess_FixturePlusStaticOnly_Passes()
    {
        var (exit, stdout, _) = await RunCliAsync(
            "inspect",
            "--project", FixtureDir(),
            "--assignment", ExamplePath("static-only.json"));

        Assert.Equal((int)CliExitCode.Passed, exit);
        Assert.Contains("PASSED", stdout, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Demo failure (exit 1) ─────────────────────────────────

    [Fact]
    public async Task DemoFailure_FixturePlusStaticFail_Fails()
    {
        var (exit, stdout, _) = await RunCliAsync(
            "inspect",
            "--project", FixtureDir(),
            "--assignment", ExamplePath("static-fail.json"));

        Assert.Equal((int)CliExitCode.Failed, exit);
        Assert.Contains("FAILED", stdout, StringComparison.OrdinalIgnoreCase);
    }

    // ─── JSON output is valid and re-parseable ─────────────────

    [Fact]
    public async Task DemoJson_FixturePlusStaticOnly_Parses()
    {
        var (exit, stdout, _) = await RunCliAsync(
            "inspect",
            "--project", FixtureDir(),
            "--assignment", ExamplePath("static-only.json"),
            "--format", "json");

        Assert.Equal((int)CliExitCode.Passed, exit);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("PASSED", root.GetProperty("status").GetString());
        var reqs = root.GetProperty("requirements");
        Assert.Equal(JsonValueKind.Array, reqs.ValueKind);
        Assert.Equal(1, reqs.GetArrayLength());
        Assert.False(reqs[0].GetProperty("hasRuntime").GetBoolean());
    }

    // ─── Example assignments are still structurally valid ──────

    [Theory]
    [InlineData("static-only.json")]
    [InlineData("static-fail.json")]
    [InlineData("runtime-required.json")]
    public void ExampleAssignment_LoadsAndValidates(string fileName)
    {
        var path = ExamplePath(fileName);
        Assert.True(File.Exists(path), $"Example file missing: {path}");

        var assignment = AssignmentLoader.Load(path);
        Assert.NotNull(assignment);
        Assert.NotEmpty(assignment.Requirements);

        Assert.Empty(CliInputValidator.ValidateAssignment(assignment));

        var validator = new InspectionWorkflowValidator();
        var errors = validator.Validate(assignment)
            .Where(i => i.Severity == WorkflowIssueSeverity.Error)
            .ToList();
        Assert.Empty(errors);
    }
}
