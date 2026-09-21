using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Runtime;

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
    public void RuntimeAction_PolymorphicRoundtrip_WaitAction()
    {
        var action = new WaitAction { ActionId = "w1", Milliseconds = 500 };
        var json = JsonSerializer.Serialize<RuntimeAction>(action, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeAction>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.IsType<WaitAction>(deserialized);
        var wait = (WaitAction)deserialized!;
        Assert.Equal("w1", wait.ActionId);
        Assert.Equal(500, wait.Milliseconds);
    }

    [Fact]
    public void RuntimeAction_PolymorphicRoundtrip_ClickButtonAction()
    {
        var action = new ClickButtonAction { ActionId = "click_btn", GameObjectName = "MainMenuButton" };
        var json = JsonSerializer.Serialize<RuntimeAction>(action, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeAction>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.IsType<ClickButtonAction>(deserialized);
        var click = (ClickButtonAction)deserialized!;
        Assert.Equal("click_btn", click.ActionId);
        Assert.Equal("MainMenuButton", click.GameObjectName);
    }

    [Fact]
    public void RuntimeAction_PolymorphicRoundtrip_ObserveActiveSceneAction()
    {
        var action = new ObserveActiveSceneAction { ActionId = "obs_scene" };
        var json = JsonSerializer.Serialize<RuntimeAction>(action, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeAction>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.IsType<ObserveActiveSceneAction>(deserialized);
        var obs = (ObserveActiveSceneAction)deserialized!;
        Assert.Equal("obs_scene", obs.ActionId);
    }

    [Fact]
    public void RuntimeAction_PolymorphicRoundtrip_ReadLogsAction()
    {
        var action = new ReadLogsAction { ActionId = "read_logs" };
        var json = JsonSerializer.Serialize<RuntimeAction>(action, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeAction>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.IsType<ReadLogsAction>(deserialized);
    }

    [Fact]
    public void RuntimeAssertion_PolymorphicRoundtrip_AssertActiveScene()
    {
        var assertion = new AssertActiveScene { AssertionId = "a1", ExpectedSceneName = "TargetScene" };
        var json = JsonSerializer.Serialize<RuntimeAssertion>(assertion, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeAssertion>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.IsType<AssertActiveScene>(deserialized);
        var aas = (AssertActiveScene)deserialized!;
        Assert.Equal("TargetScene", aas.ExpectedSceneName);
    }

    [Fact]
    public void RuntimeAssertion_PolymorphicRoundtrip_AssertNoExceptions()
    {
        var assertion = new AssertNoExceptions { AssertionId = "a2" };
        var json = JsonSerializer.Serialize<RuntimeAssertion>(assertion, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeAssertion>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.IsType<AssertNoExceptions>(deserialized);
    }

    [Fact]
    public void RuntimeTestScript_RoundTrip_WithPolymorphicActionsAndAssertions()
    {
        var script = new RuntimeTestScript
        {
            Name = "Test Script",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "wait1", Milliseconds = 200 },
                new ClickButtonAction { ActionId = "click1", GameObjectName = "Btn" },
                new ObserveActiveSceneAction { ActionId = "obs1" },
                new ReadLogsAction { ActionId = "log1" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                new AssertActiveScene { AssertionId = "assert1", ExpectedSceneName = "TargetScene" },
                new AssertNoExceptions { AssertionId = "assert2" },
            },
        };

        var json = JsonSerializer.Serialize(script, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeTestScript>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("Test Script", deserialized!.Name);
        Assert.Equal(4, deserialized.Actions.Count);
        Assert.Equal(2, deserialized.Assertions.Count);
        Assert.IsType<WaitAction>(deserialized.Actions[0]);
        Assert.IsType<ClickButtonAction>(deserialized.Actions[1]);
        Assert.IsType<ObserveActiveSceneAction>(deserialized.Actions[2]);
        Assert.IsType<ReadLogsAction>(deserialized.Actions[3]);
        Assert.IsType<AssertActiveScene>(deserialized.Assertions[0]);
        Assert.IsType<AssertNoExceptions>(deserialized.Assertions[1]);
    }

    private static readonly JsonSerializerOptions PolymorphicOptions = new()
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