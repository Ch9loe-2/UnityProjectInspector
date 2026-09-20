using System.Text.Json;
using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// Tests for AssignmentDefinition JSON serialization and deserialization.
/// </summary>
[Trait("Category", "Unit")]
public class AssignmentModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void AssignmentDefinition_SerializesAndDeserializes()
    {
        var original = new AssignmentDefinition
        {
            Id = "test-assignment",
            Name = "Test Assignment",
            Description = "A test",
            Requirements = new List<RequirementDefinition>(),
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<AssignmentDefinition>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("test-assignment", deserialized!.Id);
        Assert.Equal("Test Assignment", deserialized.Name);
        Assert.Empty(deserialized.Requirements);
    }

    [Fact]
    public void AssignmentDefinition_DeserializesFromJsonString()
    {
        var json = """
        {
            "id": "maze-2d",
            "name": "2D Maze",
            "description": "Test",
            "requirements": []
        }
        """;

        var assignment = JsonSerializer.Deserialize<AssignmentDefinition>(json, JsonOptions);

        Assert.NotNull(assignment);
        Assert.Equal("maze-2d", assignment!.Id);
        Assert.Equal("2D Maze", assignment.Name);
        Assert.Empty(assignment.Requirements);
    }

    [Fact]
    public void AssignmentDefinition_InvalidJson_Throws()
    {
        // Omit "id" entirely — the required modifier on AssignmentDefinition.Id
        // will cause System.Text.Json to throw JsonException.
        var json = """{ "name": "Test" }""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AssignmentDefinition>(json, JsonOptions));
    }

    [Fact]
    public void RequirementDefinition_SerializesAndDeserializes()
    {
        var req = new RequirementDefinition
        {
            Id = "req-1",
            Name = "Requirement 1",
            Description = "A requirement",
            EvidenceRequirement = "StaticOnly",
        };

        var json = JsonSerializer.Serialize(req, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RequirementDefinition>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("req-1", deserialized!.Id);
        Assert.Equal("StaticOnly", deserialized.EvidenceRequirement);
        Assert.Null(deserialized.RuntimeTest);
    }

    [Fact]
    public void RequirementDefinition_WithRuntimeRequired()
    {
        var req = new RequirementDefinition
        {
            Id = "req-runtime",
            Name = "Runtime Req",
            EvidenceRequirement = "RuntimeRequired",
        };

        var json = JsonSerializer.Serialize(req, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RequirementDefinition>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("RuntimeRequired", deserialized!.EvidenceRequirement);
    }

    [Fact]
    public void RequirementDefinition_WithStaticRules()
    {
        var req = new RequirementDefinition
        {
            Id = "req-1",
            Name = "Req 1",
            EvidenceRequirement = "StaticOnly",
            StaticRules = new List<Core.Models.Rules.RuleDefinition>
            {
                new()
                {
                    Id = "scene.exists",
                    Name = "Scene exists",
                    Type = "SceneExists",
                    Target = "MainMenu",
                },
            },
        };

        var json = JsonSerializer.Serialize(req, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RequirementDefinition>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.StaticRules);
        Assert.Equal("SceneExists", deserialized.StaticRules[0].Type);
        Assert.Equal("MainMenu", deserialized.StaticRules[0].Target);
    }

    [Fact]
    public void AssignmentResult_AllPassed()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
        };

        var result = new AssignmentInspectionResult
        {
            Assignment = assignment,
            FinalStatus = Core.Models.Rules.RuleStatus.Passed,
            Message = "All passed.",
        };

        Assert.Equal(Core.Models.Rules.RuleStatus.Passed, result.FinalStatus);
        Assert.Single(new[] { result });
    }
}