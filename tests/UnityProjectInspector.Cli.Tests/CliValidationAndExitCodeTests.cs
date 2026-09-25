using System.Text;
using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M26 productization tests: input validation, error handling, exit codes,
/// and example-assignment loadability. These tests do NOT require a Unity Editor
/// — static inspection is exercised against a minimal fake Unity project, and
/// runtime-dependent paths (exit code 3) are covered at the mapping level instead.
/// </summary>
public class CliValidationAndExitCodeTests
{
    // ─── helpers ──────────────────────────────────────────────

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

    private static string MakeFakeUnityProject(string sceneName)
    {
        var root = Path.Combine(Path.GetTempPath(), "upi-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        File.WriteAllText(
            Path.Combine(root, "Assets", "Scenes", sceneName + ".unity"),
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" +
            "--- !u!1 &1112762170\nGameObject:\n  m_ObjectHideFlags: 0\n  m_Name: Root\n");
        return root;
    }

    private static string WriteAssignment(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "upi-assign-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string StaticAssignment(string sceneName) =>
        "{" +
        "\"id\":\"example-static\"," +
        "\"name\":\"Static Example\"," +
        "\"requirements\":[" +
        "{" +
        "\"id\":\"r1\"," +
        "\"name\":\"Scene exists\"," +
        "\"evidenceRequirement\":\"StaticOnly\"," +
        "\"staticRules\":[" +
        "{\"id\":\"s1\",\"name\":\"scene\",\"type\":\"SceneExists\"," +
        $"\"target\":\"{sceneName}\",\"severity\":\"Error\"}}" +
        "]" +
        "}" +
        "]}";

    // ─── 1. Argument validation (exit 2) ──────────────────────

    [Fact]
    public async Task MissingProject_ExitsInvalidInput()
    {
        var (exit, _, err) = await RunCliAsync("inspect", "--assignment", "x.json");
        Assert.Equal((int)CliExitCode.InvalidInput, exit);
        Assert.Contains("project", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingAssignment_ExitsInvalidInput()
    {
        var (exit, _, err) = await RunCliAsync("inspect", "--project", "x");
        Assert.Equal((int)CliExitCode.InvalidInput, exit);
        Assert.Contains("assignment", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownOption_ExitsInvalidInput()
    {
        var (exit, _, err) = await RunCliAsync(
            "inspect", "--project", "x", "--assignment", "y", "--bogus");
        Assert.Equal((int)CliExitCode.InvalidInput, exit);
        Assert.Contains("Unknown option", err);
    }

    [Fact]
    public async Task UnknownCommand_ExitsInvalidInput()
    {
        var (exit, _, err) = await RunCliAsync("frobnicate");
        Assert.Equal((int)CliExitCode.InvalidInput, exit);
        Assert.Contains("Unknown command", err);
    }

    [Fact]
    public async Task InvalidFormat_ExitsInvalidInput()
    {
        var (exit, _, err) = await RunCliAsync(
            "inspect", "--project", "x", "--assignment", "y", "--format", "xml");
        Assert.Equal((int)CliExitCode.InvalidInput, exit);
        Assert.Contains("Invalid format", err);
    }

    // ─── 2. File / path validation (exit 2) ──────────────────

    [Fact]
    public async Task AssignmentFileNotFound_ExitsInvalidInput()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var missing = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json");
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", project, "--assignment", missing);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
            Assert.Contains("not found", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectPathNotFound_ExitsInvalidInput()
    {
        var assign = WriteAssignment(StaticAssignment("TrainingScene"));
        var missing = Path.Combine(Path.GetTempPath(), "no-such-project-" + Guid.NewGuid());
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", missing, "--assignment", assign);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
            Assert.Contains("not found", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(assign);
        }
    }

    // ─── 3. JSON / assignment format validation (exit 2) ──────

    [Fact]
    public async Task MalformedJson_ExitsInvalidInput()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var assign = WriteAssignment("{\"id\":\"x\",");
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
            Assert.Contains("not valid", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    [Fact]
    public async Task IllegalRootObject_ExitsInvalidInput()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var assign = WriteAssignment("[1, 2, 3]");
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
            Assert.Contains("not valid", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    [Fact]
    public async Task MissingRequiredField_ExitsInvalidInput()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        // Missing "id" (required on AssignmentDefinition)
        var assign = WriteAssignment("{\"name\":\"NoId\",\"requirements\":[]}");
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
            Assert.Contains("not valid", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    [Fact]
    public async Task UnknownPolymorphicType_ExitsInvalidInput()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var assign = WriteAssignment(
            "{" +
            "\"id\":\"x\",\"name\":\"x\"," +
            "\"requirements\":[{" +
            "\"id\":\"r1\",\"name\":\"r1\"," +
            "\"evidenceRequirement\":\"StaticOnly\"," +
            "\"staticRules\":[{\"id\":\"s1\",\"name\":\"s\",\"type\":\"SceneExists\",\"target\":\"TrainingScene\",\"severity\":\"Error\"}]," +
            "\"runtimeTest\":{\"name\":\"t\",\"actions\":[{\"$type\":\"NoSuchAction\",\"actionId\":\"a1\"}],\"assertions\":[]}" +
            "}]}");
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    [Fact]
    public async Task EmptyRequirements_ExitsInvalidInput()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        // Valid JSON + passes Core validator, but has zero requirements.
        var assign = WriteAssignment("{\"id\":\"x\",\"name\":\"y\",\"requirements\":[]}");
        try
        {
            var (exit, _, err) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.InvalidInput, exit);
            Assert.Contains("at least one requirement", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    // ─── 4. Successful static inspection (exit 0) ─────────────

    [Fact]
    public async Task StaticPass_ExitsPassed()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var assign = WriteAssignment(StaticAssignment("TrainingScene"));
        try
        {
            var (exit, stdout, _) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.Passed, exit);
            Assert.Contains("PASSED", stdout);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    [Fact]
    public async Task StaticFail_ExitsFailed()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var assign = WriteAssignment(StaticAssignment("NonExistentScene"));
        try
        {
            var (exit, stdout, _) = await RunCliAsync("inspect", "--project", project, "--assignment", assign);
            Assert.Equal((int)CliExitCode.Failed, exit);
            Assert.Contains("FAILED", stdout);
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    // ─── 5. JSON output is valid and re-parseable ──────────────

    [Fact]
    public async Task JsonOutput_IsValidAndReparseable()
    {
        var project = MakeFakeUnityProject("TrainingScene");
        var assign = WriteAssignment(StaticAssignment("TrainingScene"));
        try
        {
            var (exit, stdout, _) = await RunCliAsync(
                "inspect", "--project", project, "--assignment", assign, "--format", "json");
            Assert.Equal((int)CliExitCode.Passed, exit);

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.Equal("PASSED", root.GetProperty("status").GetString());
            var reqs = root.GetProperty("requirements");
            Assert.Equal(JsonValueKind.Array, reqs.ValueKind);
            Assert.Equal(1, reqs.GetArrayLength());
            Assert.False(reqs[0].GetProperty("hasRuntime").GetBoolean());
        }
        finally
        {
            Directory.Delete(project, recursive: true);
            File.Delete(assign);
        }
    }

    // ─── 6. CliInputValidator unit tests ──────────────────────

    [Fact]
    public void CliInputValidator_EmptyRequirements_FlagsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "a",
            Name = "b",
            Requirements = new List<RequirementDefinition>(),
        };

        var errors = CliInputValidator.ValidateAssignment(assignment);
        Assert.Single(errors);
        Assert.Contains("at least one requirement", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CliInputValidator_ValidAssignment_NoErrors()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "a",
            Name = "b",
            Requirements = new List<RequirementDefinition>
            {
                new() { Id = "r1", Name = "r1" },
            },
        };

        var errors = CliInputValidator.ValidateAssignment(assignment);
        Assert.Empty(errors);
    }

    // ─── 7. Exit-code mapping (covers 0/1/3/4) ────────────────

    [Fact]
    public void ExitCodeMapping_Passed_Is0()
        => Assert.Equal(CliExitCode.Passed, CliResultMapping.FromFinalStatus(RuleStatus.Passed));

    [Fact]
    public void ExitCodeMapping_Failed_Is1()
        => Assert.Equal(CliExitCode.Failed, CliResultMapping.FromFinalStatus(RuleStatus.Failed));

    [Fact]
    public void ExitCodeMapping_NotEvaluated_Is3()
        => Assert.Equal(CliExitCode.RuntimeError, CliResultMapping.FromFinalStatus(RuleStatus.NotEvaluated));

    [Fact]
    public void ExitCodeMapping_Unexpected_Is4()
        => Assert.Equal(CliExitCode.InternalError, CliResultMapping.FromFinalStatus((RuleStatus)999));

    // ─── 8. Example assignments are loadable & valid ──────────

    [Theory]
    [InlineData("static-only.json")]
    [InlineData("runtime-required.json")]
    public void ExampleAssignment_LoadsAndValidates(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "examples", "assignments", fileName);
        Assert.True(File.Exists(path), $"Example file missing: {path}");

        var assignment = AssignmentLoader.Load(path);
        Assert.NotNull(assignment);
        Assert.NotEmpty(assignment.Requirements);

        // CLI front-door validation must pass
        var cliIssues = CliInputValidator.ValidateAssignment(assignment);
        Assert.Empty(cliIssues);

        // Core business-rule validation must pass
        var validator = new InspectionWorkflowValidator();
        var errors = validator.Validate(assignment)
            .Where(i => i.Severity == WorkflowIssueSeverity.Error)
            .ToList();
        Assert.Empty(errors);
    }
}
