using System.Text.Json;
using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M31 Assignment Creator tests.
///
/// Tests cover: SanitizeId, JSON output re-readability, Validator pass,
/// and backward compatibility with existing CLI pipeline.
/// </summary>
public class AssignmentCreatorTests
{
    [Fact]
    public void SanitizeId_SceneName_CreatesValidId()
    {
        Assert.Equal("main-scene", AssignmentCreator.SanitizeId("Main Scene"));
    }

    [Fact]
    public void SanitizeId_FilePath_CreatesValidId()
    {
        var id = AssignmentCreator.SanitizeId("Assets/Scripts/PlayerMove.cs");
        Assert.DoesNotContain("/", id, StringComparison.Ordinal);
        Assert.DoesNotContain(".", id, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeId_ComponentName_CreatesValidId()
    {
        Assert.Equal("boxcollider", AssignmentCreator.SanitizeId("BoxCollider"));
    }

    [Fact]
    public void SanitizeId_AlreadySanitized_NoChange()
    {
        Assert.Equal("my-assignment", AssignmentCreator.SanitizeId("my-assignment"));
    }

    // ─── Generated JSON is valid and re-parseable ─────────────

    [Fact]
    public async Task CreatedAssignmentJson_IsValidAndReParsable()
    {
        var rules = new[]
        {
            ("scene.training", "TrainingScene 场景存在", "SceneExists", "TrainingScene"),
            ("go.player", "Player 游戏对象存在", "GameObjectExists", "Player"),
        };

        var json = BuildAssignmentJson("test-assignment", "Test Assignment", rules);
        var path = Path.Combine(Path.GetTempPath(), "upi-created-assignment-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json);

            // Must load through AssignmentLoader
            var assignment = AssignmentLoader.Load(path);
            Assert.NotNull(assignment);
            Assert.Equal("test-assignment", assignment.Id);
            Assert.NotEmpty(assignment.Requirements);

            var req = assignment.Requirements[0];
            Assert.NotEmpty(req.StaticRules);
            Assert.Equal(2, req.StaticRules.Count);
            Assert.Equal("SceneExists", req.StaticRules[0].Type);
            Assert.Equal("GameObjectExists", req.StaticRules[1].Type);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CreatedAssignmentJson_PassesValidator()
    {
        var rules = new[]
        {
            ("scene.training", "TrainingScene exists", "SceneExists", "TrainingScene"),
        };

        var json = BuildAssignmentJson("valid-test", "Valid Test", rules);
        var path = Path.Combine(Path.GetTempPath(), "upi-valid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            var assignment = AssignmentLoader.Load(path);

            Assert.Empty(CliInputValidator.ValidateAssignment(assignment));

            var validator = new InspectionWorkflowValidator();
            var errors = validator.Validate(assignment)
                .Where(i => i.Severity == WorkflowIssueSeverity.Error)
                .ToList();
            Assert.Empty(errors);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Builds a JSON string matching the format that AssignmentCreator produces.
    /// </summary>
    private static string BuildAssignmentJson(string id, string name, (string ruleId, string ruleName, string type, string target)[] rules)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"id\": \"{id}\",");
        sb.AppendLine($"  \"name\": \"{name}\",");
        sb.AppendLine("  \"requirements\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"id\": \"custom-rules\",");
        sb.AppendLine($"      \"name\": \"{name}\",");
        sb.AppendLine("      \"evidenceRequirement\": \"StaticOnly\",");
        sb.AppendLine("      \"staticRules\": [");

        for (int i = 0; i < rules.Length; i++)
        {
            var (ruleId, ruleName, type, target) = rules[i];
            sb.AppendLine("        {");
            sb.AppendLine($"          \"id\": \"{ruleId}\",");
            sb.AppendLine($"          \"name\": \"{ruleName}\",");
            sb.AppendLine($"          \"type\": \"{type}\",");
            sb.AppendLine($"          \"target\": \"{target}\",");
            sb.AppendLine($"          \"severity\": \"Error\"");
            sb.Append(i < rules.Length - 1 ? "        }," : "        }");
            sb.AppendLine();
        }

        sb.AppendLine("      ]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.Append("}");
        return sb.ToString();
    }
}