using System.Reflection;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli.Tests;

public class CliArgumentParserTests
{
    // ─── 1. --help ─────────────────────────────────────────────

    [Fact]
    public void HelpFlag_ReturnsHelpRequested()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(["--help"]);

        Assert.True(help);
        Assert.Null(err);
        Assert.Null(options);
    }

    [Fact]
    public void HelpFlagWithArgs_ReturnsHelpRequested()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(["inspect", "--project", "x", "--help"]);

        Assert.True(help);
        Assert.Null(err);
    }

    // ─── 2. Missing arguments ─────────────────────────────────

    [Fact]
    public void NoArgs_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([]);

        Assert.False(help);
        Assert.NotNull(err);
        Assert.Contains("No arguments provided", err, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingProject_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(["inspect", "--assignment", "test.json"]);

        Assert.NotNull(err);
        Assert.Contains("--project", err, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAssignment_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(["inspect", "--project", "/path"]);

        Assert.NotNull(err);
        Assert.Contains("--assignment", err, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBoth_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(["inspect"]);

        Assert.NotNull(err);
        Assert.Contains("--project", err, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommand_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(["run", "--project", "/p", "--assignment", "/a"]);

        Assert.NotNull(err);
        Assert.Contains("Unknown command", err, StringComparison.Ordinal);
    }

    // ─── 3. Valid parsing ──────────────────────────────────────

    [Fact]
    public void MinimalArgs_ParsesCorrectly()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse(
            ["inspect", "--project", "/my/project", "--assignment", "assignment.json"]);

        Assert.Null(err);
        Assert.False(help);
        Assert.NotNull(options);
        Assert.Equal("/my/project", options.ProjectPath);
        Assert.Equal("assignment.json", options.AssignmentPath);
        Assert.Null(options.UnityExecutable);
        Assert.Equal(".", options.OutputDirectory);
        Assert.Equal("text", options.Format);
    }

    [Fact]
    public void AllArgs_ParsesCorrectly()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project", "/my/project",
            "--assignment", "assignment.json",
            "--unity", "/Applications/Unity/Unity.app/Contents/MacOS/Unity",
            "--output", "./results",
            "--format", "json",
        ]);

        Assert.Null(err);
        Assert.NotNull(options);
        Assert.Equal("/my/project", options.ProjectPath);
        Assert.Equal("assignment.json", options.AssignmentPath);
        Assert.Equal("/Applications/Unity/Unity.app/Contents/MacOS/Unity", options.UnityExecutable);
        Assert.Equal("./results", options.OutputDirectory);
        Assert.Equal("json", options.Format);
    }

    [Fact]
    public void EqualSignSyntax_ParsesCorrectly()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project=/my/project",
            "--assignment=assignment.json",
        ]);

        Assert.Null(err);
        Assert.NotNull(options);
        Assert.Equal("/my/project", options.ProjectPath);
        Assert.Equal("assignment.json", options.AssignmentPath);
    }

    [Fact]
    public void UnknownOption_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project", "/p",
            "--assignment", "/a",
            "--unknown", "value",
        ]);

        Assert.NotNull(err);
        Assert.Contains("--unknown", err, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidFormat_ReturnsError()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project", "/p",
            "--assignment", "/a",
            "--format", "xml",
        ]);

        Assert.NotNull(err);
        Assert.Contains("xml", err, StringComparison.Ordinal);
    }
}

public class AssignmentLoaderTests
{
    private readonly string _tempDir;

    public AssignmentLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    // ─── 4. Loading tests ──────────────────────────────────────

    [Fact]
    public void LoadValidAssignment_ReturnsDefinition()
    {
        var path = Path.Combine(_tempDir, "valid.json");
        File.WriteAllText(path, """
        {
            "id": "test-assignment",
            "name": "Test Assignment",
            "requirements": []
        }
        """);

        var result = AssignmentLoader.Load(path);

        Assert.NotNull(result);
        Assert.Equal("test-assignment", result.Id);
        Assert.Equal("Test Assignment", result.Name);
        Assert.Empty(result.Requirements);
    }

    [Fact]
    public void LoadAssignmentWithRequirement_ReturnsDefinition()
    {
        var path = Path.Combine(_tempDir, "with-req.json");
        File.WriteAllText(path, """
        {
            "id": "test",
            "name": "Test",
            "requirements": [
                {
                    "id": "R001",
                    "name": "Scene exists",
                    "staticRules": [
                        {
                            "id": "SR001",
                            "name": "SampleScene exists",
                            "type": "SceneExists",
                            "target": "SampleScene"
                        }
                    ]
                }
            ]
        }
        """);

        var result = AssignmentLoader.Load(path);

        Assert.Single(result.Requirements);
        Assert.Equal("R001", result.Requirements[0].Id);
        Assert.Single(result.Requirements[0].StaticRules);
        Assert.Equal("SampleScene", result.Requirements[0].StaticRules[0].Target);
    }

    [Fact]
    public void LoadAssignmentWithRuntime_ReturnsDefinition()
    {
        var path = Path.Combine(_tempDir, "with-runtime.json");
        File.WriteAllText(path, """
        {
            "id": "runtime-test",
            "name": "Runtime Test",
            "requirements": [
                {
                    "id": "RT001",
                    "name": "Runtime click test",
                    "evidenceRequirement": "RuntimeRequired",
                    "runtimeTest": {
                        "name": "Click button and verify scene",
                        "actions": [
                            {
                                "$type": "ClickButton",
                                "actionId": "click_1",
                                "gameObjectName": "StartButton"
                            },
                            {
                                "$type": "Wait",
                                "actionId": "wait_1",
                                "milliseconds": 500
                            },
                            {
                                "$type": "ObserveActiveScene",
                                "actionId": "observe_1"
                            }
                        ],
                        "assertions": [
                            {
                                "$type": "AssertActiveScene",
                                "assertionId": "assert_1",
                                "expectedSceneName": "GameScene"
                            }
                        ]
                    }
                }
            ]
        }
        """);

        var result = AssignmentLoader.Load(path);
        Assert.Single(result.Requirements);
        var req = result.Requirements[0];
        Assert.Equal("RuntimeRequired", req.EvidenceRequirement);
        Assert.NotNull(req.RuntimeTest);
        Assert.Equal("Click button and verify scene", req.RuntimeTest.Name);
        Assert.Equal(3, req.RuntimeTest.Actions.Count);
        Assert.Single(req.RuntimeTest.Assertions);
    }

    [Fact]
    public void LoadMissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");

        var ex = Assert.Throws<FileNotFoundException>(() => AssignmentLoader.Load(path));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadInvalidJson_ThrowsJsonException()
    {
        var path = Path.Combine(_tempDir, "invalid.json");
        File.WriteAllText(path, "this is not json");

        Assert.Throws<System.Text.Json.JsonException>(() => AssignmentLoader.Load(path));
    }
}

public class ResultFormatterTests
{
    private static AssignmentInspectionResult MakeResult(RuleStatus status, params (string id, string name, RuleStatus s)[] reqs)
    {
        var requirementResults = new List<RequirementInspectionResult>();
        foreach (var (id, name, s) in reqs)
        {
            requirementResults.Add(new RequirementInspectionResult
            {
                Requirement = new RequirementDefinition
                {
                    Id = id,
                    Name = name,
                },
                Status = s,
                Message = $"{id}: {s}",
                StaticResults = new List<RuleResult>
                {
                    new() { RuleId = id, RuleName = name, Status = s, Message = $"{id}: {s}" },
                },
            });
        }

        return new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition { Id = "test", Name = "Test" },
            FinalStatus = status,
            Message = $"Assignment {status}",
            RequirementResults = requirementResults,
        };
    }

    // ─── 5. Formatting tests ───────────────────────────────────

    [Fact]
    public void ToCliResult_PassedResult()
    {
        var coreResult = MakeResult(RuleStatus.Passed,
            ("R001", "Req 1", RuleStatus.Passed),
            ("R002", "Req 2", RuleStatus.Passed));

        var cli = ResultFormatter.ToCliResult(coreResult);

        Assert.Equal("PASSED", cli.Status);
        Assert.Equal(2, cli.Requirements.Count);
        Assert.All(cli.Requirements, r => Assert.Equal("PASSED", r.Status));
    }

    [Fact]
    public void ToCliResult_FailedResult()
    {
        var coreResult = MakeResult(RuleStatus.Failed,
            ("R001", "Req 1", RuleStatus.Passed),
            ("R002", "Req 2", RuleStatus.Failed));

        var cli = ResultFormatter.ToCliResult(coreResult);

        Assert.Equal("FAILED", cli.Status);
        Assert.Equal("FAILED", cli.Requirements[1].Status);
    }

    [Fact]
    public void ToCliResult_NotEvaluatedResult()
    {
        var coreResult = MakeResult(RuleStatus.NotEvaluated,
            ("R001", "Req 1", RuleStatus.Passed),
            ("R002", "Req 2", RuleStatus.NotEvaluated));

        var cli = ResultFormatter.ToCliResult(coreResult);

        Assert.Equal("NOT_EVALUATED", cli.Status);
    }

    [Fact]
    public void TextFormat_ContainsResultLine()
    {
        var coreResult = MakeResult(RuleStatus.Passed,
            ("R001", "Req 1", RuleStatus.Passed));

        var cli = ResultFormatter.ToCliResult(coreResult);
        using var writer = new StringWriter();
        ResultFormatter.WriteText(cli, writer);
        var output = writer.ToString();

        Assert.Contains("Result:", output, StringComparison.Ordinal);
        Assert.Contains("PASSED", output, StringComparison.Ordinal);
        Assert.Contains("R001", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TextFormat_FailedResult()
    {
        var coreResult = MakeResult(RuleStatus.Failed,
            ("R001", "Req 1", RuleStatus.Failed));

        var cli = ResultFormatter.ToCliResult(coreResult);
        using var writer = new StringWriter();
        ResultFormatter.WriteText(cli, writer);
        var output = writer.ToString();

        Assert.Contains("FAILED", output, StringComparison.Ordinal);
        Assert.Contains("✗", output);
    }

    [Fact]
    public void JsonFormat_ValidJsonOutput()
    {
        var coreResult = MakeResult(RuleStatus.Passed,
            ("R001", "Req 1", RuleStatus.Passed));

        var cli = ResultFormatter.ToCliResult(coreResult);
        using var writer = new StringWriter();
        ResultFormatter.WriteJson(cli, writer);
        var json = writer.ToString();

        Assert.NotNull(json);
        // Parse back to verify valid JSON
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        Assert.Equal("PASSED", parsed.GetProperty("status").GetString());
    }

    [Fact]
    public void JsonFormat_FailedResult()
    {
        var coreResult = MakeResult(RuleStatus.Failed,
            ("R001", "Req 1", RuleStatus.Failed));

        var cli = ResultFormatter.ToCliResult(coreResult);
        using var writer = new StringWriter();
        ResultFormatter.WriteJson(cli, writer);
        var json = writer.ToString();

        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        Assert.Equal("FAILED", parsed.GetProperty("status").GetString());
        Assert.Equal("FAILED", parsed.GetProperty("requirements")[0].GetProperty("status").GetString());
    }
}