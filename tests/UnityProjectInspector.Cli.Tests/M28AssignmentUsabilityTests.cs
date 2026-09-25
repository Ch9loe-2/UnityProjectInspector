using System.Text.Json;
using UnityProjectInspector.Core;
using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M28 assignment-authoring usability tests. These protect the documentation
/// surface: the template and the scene-check example must remain loadable,
/// valid, and (for scene-check) actually pass against the bundled fixture.
/// Invalid examples must be rejected with exit code 2 (InvalidInput) so a
/// beginner gets a clear error instead of a crash.
///
/// All paths are repo-relative or generated under the system temp directory at
/// test time (never committed). No Unity Editor is required.
/// </summary>
public class M28AssignmentUsabilityTests
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

    private static string WriteTempAssignment(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "upi-m28-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "assignment.json");
        File.WriteAllText(path, json);
        return path;
    }

    // ─── Template loads and validates (no Unity needed) ──────────

    [Fact]
    public void Template_LoadsAndValidates()
    {
        var path = ExamplePath("template.json");
        Assert.True(File.Exists(path), $"Template missing: {path}");

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

    [Fact]
    public void Template_IsValidJson()
    {
        var json = File.ReadAllText(ExamplePath("template.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ─── scene-check example runs against the fixture (exit 0) ──

    [Fact]
    public async Task SceneCheckExample_FixturePlusSceneCheck_Passes()
    {
        var (exit, stdout, _) = await RunCliAsync(
            "inspect",
            "--project", FixtureDir(),
            "--assignment", ExamplePath("scene-check.json"));

        Assert.Equal((int)CliExitCode.Passed, exit);
        Assert.Contains("PASSED", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneCheckExample_LoadsAndValidates()
    {
        var path = ExamplePath("scene-check.json");
        Assert.True(File.Exists(path), $"Example missing: {path}");

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

    // ─── Invalid assignments are rejected with exit 2 ───────────

    [Fact]
    public async Task Invalid_UnknownRuntimeType_ReturnsExit2()
    {
        var json = """
        {
          "id": "bad",
          "name": "Bad",
          "requirements": [
            {
              "id": "r",
              "name": "Runtime with bogus $type",
              "evidenceRequirement": "RuntimeRequired",
              "runtimeTest": {
                "name": "t",
                "actions": [
                  { "$type": "BogusAction", "actionId": "a1" }
                ],
                "assertions": [
                  { "$type": "AssertActiveScene", "assertionId": "x", "expectedSceneName": "S" }
                ]
              }
            }
          ]
        }
        """;
        var path = WriteTempAssignment(json);
        var (exit, _, stderr) = await RunCliAsync(
            "inspect", "--project", FixtureDir(), "--assignment", path);

        Assert.Equal((int)CliExitCode.InvalidInput, exit);
    }

    [Fact]
    public async Task Invalid_EmptyRequirements_ReturnsExit2()
    {
        var json = """
        {
          "id": "empty",
          "name": "Empty",
          "requirements": []
        }
        """;
        var path = WriteTempAssignment(json);
        var (exit, _, _) = await RunCliAsync(
            "inspect", "--project", FixtureDir(), "--assignment", path);

        Assert.Equal((int)CliExitCode.InvalidInput, exit);
    }

    [Fact]
    public async Task Invalid_MissingRequiredProperty_ReturnsExit2()
    {
        // requirement missing the required "name" field
        var json = """
        {
          "id": "missing",
          "name": "Missing prop",
          "requirements": [
            {
              "id": "r1",
              "staticRules": [
                { "id": "s1", "name": "Scene", "type": "SceneExists", "target": "S" }
              ]
            }
          ]
        }
        """;
        var path = WriteTempAssignment(json);
        var (exit, _, _) = await RunCliAsync(
            "inspect", "--project", FixtureDir(), "--assignment", path);

        Assert.Equal((int)CliExitCode.InvalidInput, exit);
    }
}
