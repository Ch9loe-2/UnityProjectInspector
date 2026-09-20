using System.Text.Json;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Unit tests for M14 RuntimeAction and RuntimeAssertion models.
/// Verifies action/assertion serialization, defaults, and construction.
/// </summary>
[Trait("Category", "Unit")]
public class RuntimeActionTests
{
    [Fact]
    public void WaitAction_HasDefaults()
    {
        var wait = new WaitAction { ActionId = "wait1" };
        Assert.Equal("wait1", wait.ActionId);
        Assert.Equal(200, wait.Milliseconds);
    }

    [Fact]
    public void WaitAction_CustomDuration()
    {
        var wait = new WaitAction { ActionId = "w2", Milliseconds = 500 };
        Assert.Equal(500, wait.Milliseconds);
    }

    [Fact]
    public void ClickButtonAction_RequiresGameObjectName()
    {
        var click = new ClickButtonAction
        {
            ActionId = "click1",
            GameObjectName = "StartButton",
        };
        Assert.Equal("StartButton", click.GameObjectName);
    }

    [Fact]
    public void ObserveActiveSceneAction_Creates()
    {
        var obs = new ObserveActiveSceneAction { ActionId = "obs1" };
        Assert.Equal("obs1", obs.ActionId);
    }

    [Fact]
    public void ReadLogsAction_Creates()
    {
        var logs = new ReadLogsAction { ActionId = "logs1" };
        Assert.Equal("logs1", logs.ActionId);
    }
}

public class RuntimeAssertionTests
{
    [Fact]
    public void AssertActiveScene_RequiresSceneName()
    {
        var assertion = new AssertActiveScene
        {
            AssertionId = "assert1",
            ExpectedSceneName = "TargetScene",
        };
        Assert.Equal("TargetScene", assertion.ExpectedSceneName);
    }

    [Fact]
    public void AssertNoExceptions_HasDefaults()
    {
        var assertion = new AssertNoExceptions { AssertionId = "ne1" };
        Assert.Empty(assertion.IgnoredPatterns);
    }
}

public class RuntimeTestScriptTests
{
    [Fact]
    public void RuntimeTestScript_EmptyActionsAndAssertions()
    {
        var script = new RuntimeTestScript { Name = "Test1" };
        Assert.Empty(script.Actions);
        Assert.Empty(script.Assertions);
    }

    [Fact]
    public void RuntimeTestScript_WithActions()
    {
        var script = new RuntimeTestScript
        {
            Name = "Test1",
            Actions = new List<RuntimeAction>
            {
                new WaitAction { ActionId = "w1", Milliseconds = 500 },
                new ClickButtonAction { ActionId = "c1", GameObjectName = "Btn" },
                new ObserveActiveSceneAction { ActionId = "o1" },
                new ReadLogsAction { ActionId = "r1" },
            },
            Assertions = new List<RuntimeAssertion>
            {
                new AssertActiveScene { AssertionId = "a1", ExpectedSceneName = "TargetScene" },
                new AssertNoExceptions { AssertionId = "a2" },
            },
        };

        Assert.Equal(4, script.Actions.Count);
        Assert.Equal(2, script.Assertions.Count);
    }
}

public class RuntimeIpcProtocolTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void RuntimeCommand_SerializesAndDeserializes()
    {
        var cmd = new RuntimeCommand
        {
            Action = "ClickButton",
            Params = new Dictionary<string, object>
            {
                ["gameObjectName"] = "MainMenuButton",
            },
        };

        var json = JsonSerializer.Serialize(cmd, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeCommand>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("ClickButton", deserialized!.Action);
    }

    [Fact]
    public void RuntimeCommand_WaitAction()
    {
        var cmd = new RuntimeCommand
        {
            Action = "Wait",
            Params = new Dictionary<string, object> { ["milliseconds"] = 500 },
        };

        var json = JsonSerializer.Serialize(cmd, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RuntimeCommand>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("Wait", deserialized!.Action);
    }

    [Fact]
    public void CommandResult_Deserializes()
    {
        var json = """{"action":"result","success":true,"result":{"sceneName":"TargetScene"}}""";
        var result = JsonSerializer.Deserialize<CommandResult>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("result", result!.Action);
        Assert.True(result.Success);
    }

    [Fact]
    public void CommandResult_Failure()
    {
        var json = """{"action":"result","success":false,"error":"Button not found"}""";
        var result = JsonSerializer.Deserialize<CommandResult>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal("Button not found", result.Error);
    }
}