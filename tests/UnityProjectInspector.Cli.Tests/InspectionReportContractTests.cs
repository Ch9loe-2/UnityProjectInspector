using System.Text.Json;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M33 Report Contract Hardening — CLI-level write-path and failure-path tests.
///
/// These exercise the real <see cref="Program.Main"/> entry point (not just the
/// Builder/Writer unit tests) to confirm that <c>--report</c> actually writes a
/// file to disk in each supported format, that the schema version is present in the
/// on-disk output, and that a report write failure is caught and does NOT change the
/// inspection's exit code (the existing behavior is pinned, not altered).
///
/// Reuses the committed fixtures/MinimalUnityProject + examples/assignments/scene-check.json
/// (a static-only assignment that passes against the fixture, exit 0) — no new Unity
/// fixtures are created.
/// </summary>
public class InspectionReportContractTests
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

    private static string FixtureProject => Path.Combine(RepoRoot(), "tests", "fixtures", "MinimalUnityProject");

    private static string SceneCheckAssignment => Path.Combine(RepoRoot(), "examples", "assignments", "scene-check.json");

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

    // ─── 1. Write-path: .json ─────────────────────────────────

    [Fact]
    public async Task ReportWrite_Json_WritesFileWithSchemaVersion()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "upi-rpt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var reportPath = Path.Combine(outDir, "report.json");
        try
        {
            var (exit, stdout, _) = await RunCliAsync(
                "inspect", "--project", FixtureProject, "--assignment", SceneCheckAssignment,
                "--report", reportPath);

            Assert.Equal((int)CliExitCode.Passed, exit);
            Assert.True(File.Exists(reportPath), "JSON report file was not written");
            var bytes = new FileInfo(reportPath).Length;
            Assert.True(bytes > 0, "JSON report file is empty");

            var json = await File.ReadAllTextAsync(reportPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
            Assert.Equal("scene-check-example", root.GetProperty("assignmentId").GetString());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("requirements").ValueKind);
            Assert.Equal(2, root.GetProperty("requirements").GetArrayLength());
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── 2. Write-path: .md ───────────────────────────────────

    [Fact]
    public async Task ReportWrite_Markdown_WritesFileWithSchemaVersion()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "upi-rpt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var reportPath = Path.Combine(outDir, "report.md");
        try
        {
            var (exit, _, _) = await RunCliAsync(
                "inspect", "--project", FixtureProject, "--assignment", SceneCheckAssignment,
                "--report", reportPath);

            Assert.Equal((int)CliExitCode.Passed, exit);
            Assert.True(File.Exists(reportPath), "Markdown report file was not written");
            var md = await File.ReadAllTextAsync(reportPath);
            Assert.True(md.Length > 0, "Markdown report file is empty");

            Assert.Contains("Schema version:", md, StringComparison.Ordinal);
            Assert.Contains("1.0", md, StringComparison.Ordinal);
            // Core inspection information still present
            Assert.Contains("TrainingScene", md, StringComparison.Ordinal);
            Assert.Contains("Static rule results", md, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── 3. Write-path: .html ─────────────────────────────────

    [Fact]
    public async Task ReportWrite_Html_WritesFileWithSchemaVersion()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "upi-rpt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var reportPath = Path.Combine(outDir, "report.html");
        try
        {
            var (exit, _, _) = await RunCliAsync(
                "inspect", "--project", FixtureProject, "--assignment", SceneCheckAssignment,
                "--report", reportPath);

            Assert.Equal((int)CliExitCode.Passed, exit);
            Assert.True(File.Exists(reportPath), "HTML report file was not written");
            var html = await File.ReadAllTextAsync(reportPath);
            Assert.True(html.Length > 0, "HTML report file is empty");

            Assert.Contains("report-schema-version", html, StringComparison.Ordinal);
            Assert.Contains("report schema 1.0", html, StringComparison.Ordinal);
            // Core inspection information still present
            Assert.Contains("TrainingScene", html, StringComparison.Ordinal);
            Assert.Contains("Static Rule Results", html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── 4. Failure-path: report write fails, inspection exit code unchanged ──

    [Fact]
    public async Task ReportWrite_Failure_DoesNotChangeInspectionExitCode()
    {
        // Point --report at an existing directory. The CLI creates the parent (which is
        // the dir itself's parent), then StreamWriter throws because the path is a
        // directory. Program.cs catches this and emits a Warning; the inspection result
        // and its exit code must remain exactly as if --report had never been requested.
        var reportDir = Path.Combine(Path.GetTempPath(), "upi-rptfail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportDir);
        try
        {
            var (exit, stdout, stderr) = await RunCliAsync(
                "inspect", "--project", FixtureProject, "--assignment", SceneCheckAssignment,
                "--report", reportDir);

            // No unhandled exception → normal inspection exit code (static PASS = 0)
            Assert.Equal((int)CliExitCode.Passed, exit);
            Assert.Contains("PASSED", stdout, StringComparison.Ordinal);

            // The pre-existing failure handling behavior is preserved verbatim
            Assert.Contains("Warning: Could not write inspection report", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(reportDir, recursive: true);
        }
    }

    // ─── 5. Non-report CLI usage is unaffected ────────────────

    [Fact]
    public async Task WithoutReportFlag_CliStillPasses()
    {
        var (exit, stdout, _) = await RunCliAsync(
            "inspect", "--project", FixtureProject, "--assignment", SceneCheckAssignment);

        Assert.Equal((int)CliExitCode.Passed, exit);
        Assert.Contains("PASSED", stdout, StringComparison.Ordinal);
    }
}
