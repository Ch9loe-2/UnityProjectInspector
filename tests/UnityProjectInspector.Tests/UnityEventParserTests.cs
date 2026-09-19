using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Tests;

public class UnityEventParserTests
{
    private readonly SceneParser _sceneParser = new();
    private readonly UnityEventParser _eventParser = new();

    private static string TestDataPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

    private string UnityEventScenePath =>
        Path.Combine(TestDataPath, "UnityEventScene.unity");

    [Fact]
    public void Parse_StartButton_HasCorrectMethodName()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert
        var startButton = FindBindings(bindings, "StartButton").FirstOrDefault();
        Assert.NotNull(startButton);
        Assert.Equal("Start2D", startButton.MethodName);
    }

    [Fact]
    public void Parse_StartButton_TargetFileIdIsCorrect()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — m_Target: {fileID: 411} which is GameManager's MonoBehaviour
        var startButton = FindBindings(bindings, "StartButton").FirstOrDefault();
        Assert.NotNull(startButton);
        Assert.Equal(411, startButton.TargetFileId);
    }

    [Fact]
    public void Parse_StartButton_TargetGameObjectIsResolved()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — fileID 411 belongs to GameManager GameObject (fileID 400)
        var startButton = FindBindings(bindings, "StartButton").FirstOrDefault();
        Assert.NotNull(startButton);
        Assert.Equal(400, startButton.TargetGameObjectFileId);
        Assert.True(startButton.IsResolved);
    }

    [Fact]
    public void Parse_StartButton_TargetAssemblyTypeNameIsCaptured()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        var startButton = FindBindings(bindings, "StartButton").FirstOrDefault();
        Assert.NotNull(startButton);
        Assert.Equal("GameManager, Assembly-CSharp", startButton.TargetAssemblyTypeName);
    }

    [Fact]
    public void Parse_MultipleButtons_HaveDifferentMethods()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);
        var startButtonBinding = FindBindings(bindings, "StartButton").FirstOrDefault();
        var start3DButtonBinding = FindBindings(bindings, "Start3DButton").FirstOrDefault();

        // Assert
        Assert.NotNull(startButtonBinding);
        Assert.NotNull(start3DButtonBinding);
        Assert.Equal("Start2D", startButtonBinding.MethodName);
        Assert.Equal("Start3D", start3DButtonBinding.MethodName);
        // Both point to GameManager
        Assert.Equal(400, startButtonBinding.TargetGameObjectFileId);
        Assert.Equal(400, start3DButtonBinding.TargetGameObjectFileId);
    }

    [Fact]
    public void Parse_MultiCallButton_AllCallsAreFound()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — MultiCallButton has 2 calls
        var multiCalls = FindBindings(bindings, "MultiCallButton");
        Assert.Equal(2, multiCalls.Count);

        var methodNames = multiCalls.Select(b => b.MethodName).OrderBy(n => n).ToList();
        Assert.Equal("QuitGame", methodNames[0]);
        Assert.Equal("Start2D", methodNames[1]);
    }

    [Fact]
    public void Parse_MultiCallButton_AllTargetToSameGameObject()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);
        var multiCalls = FindBindings(bindings, "MultiCallButton");

        // Assert
        foreach (var binding in multiCalls)
        {
            Assert.True(binding.IsResolved);
            Assert.Equal(400, binding.TargetGameObjectFileId);
        }
    }

    [Fact]
    public void Parse_NoCallsObject_ReturnsEmpty()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — NoCallsObject has m_PersistentCalls: {} (no m_Calls)
        var noCalls = FindBindings(bindings, "NoCallsObject");
        Assert.Empty(noCalls);
    }

    [Fact]
    public void Parse_MissingTargetButton_DoesNotCrash()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — MissingTargetButton has fileID 0
        var missing = FindBindings(bindings, "MissingTargetButton").FirstOrDefault();
        Assert.NotNull(missing);
        Assert.Equal(0, missing.TargetFileId);
        Assert.False(missing.IsResolved);
        Assert.Equal("NonExistent", missing.MethodName);
    }

    [Fact]
    public void Parse_NoEventObject_ProducesNoBindings()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — NoEventObject has no m_OnClick at all
        var noEventBindings = FindBindings(bindings, "NoEventObject");
        Assert.Empty(noEventBindings);
    }

    [Fact]
    public void Parse_TotalBindingCount()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert
        // StartButton: 1 call
        // Start3DButton: 1 call
        // MultiCallButton: 2 calls
        // MissingTargetButton: 1 call
        // NoCallsObject: 0 calls
        // NoEventObject: 0 calls
        // = 5 total
        Assert.Equal(5, bindings.Count);
    }

    [Fact]
    public void Parse_EmptyScene_DoesNotCrash()
    {
        // Arrange
        var scene = new SceneInfo
        {
            Name = "empty",
            FilePath = "empty.unity"
        };

        // Act
        var bindings = _eventParser.Parse("", scene);

        // Assert
        Assert.Empty(bindings);
    }

    [Fact]
    public void Parse_NonExistentComponentTarget_DoesNotCrash()
    {
        // Arrange — Start3DButton + MultiCallButton both have valid resets, but
        // the parse should handle target fileId 0 gracefully
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — MissingTargetButton has unresolved target
        var missing = FindBindings(bindings, "MissingTargetButton").FirstOrDefault();
        Assert.NotNull(missing);
        Assert.False(missing.IsResolved);
        Assert.Equal(0, missing.TargetGameObjectFileId);
    }

    [Fact]
    public void Parse_SourceComponentAndGameObjectAreCorrect()
    {
        // Arrange
        var text = File.ReadAllText(UnityEventScenePath);
        var scene = _sceneParser.Parse(UnityEventScenePath);

        // Act
        var bindings = _eventParser.Parse(text, scene);

        // Assert — StartButton's source component is fileID 321
        var startButtonBinding = FindBindings(bindings, "StartButton").First();
        Assert.Equal(321, startButtonBinding.SourceComponentFileId);
        Assert.Equal(301, startButtonBinding.SourceGameObjectFileId);
    }

    /// <summary>
    /// Helper to find bindings originating from a given GameObject name.
    /// </summary>
    private static List<UnityEventBindingInfo> FindBindings(
        List<UnityEventBindingInfo> bindings, string gameObjectName)
    {
        // We don't have a direct reference to the SceneInfo here for name lookup.
        // SourceGameObjectFileId is known — we need to compute from the test data.
        // Use a helper mapping for the test data:
        var nameToFileId = new Dictionary<string, long>
        {
            ["Canvas"] = 300,
            ["StartButton"] = 301,
            ["Start3DButton"] = 302,
            ["GameManager"] = 400,
            ["NoEventObject"] = 500,
            ["MultiCallButton"] = 600,
            ["MissingTargetButton"] = 700,
            ["NoCallsObject"] = 800,
        };

        if (!nameToFileId.TryGetValue(gameObjectName, out var fileId))
        {
            return new List<UnityEventBindingInfo>();
        }

        return bindings
            .Where(b => b.SourceGameObjectFileId == fileId)
            .ToList();
    }
}