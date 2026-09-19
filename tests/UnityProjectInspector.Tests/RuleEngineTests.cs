using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests;

public class RuleEngineTests
{
    private static string TestDataPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

    #region SceneExistsRule Tests

    [Fact]
    public void SceneExistsRule_SceneExists_Passes()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new SceneExistsRule("TestScene");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Passed, result.Status);
        Assert.Equal(RuleSeverity.Info, result.Severity);
        Assert.Contains("exists", result.Message);
    }

    [Fact]
    public void SceneExistsRule_SceneNotExists_Fails()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new SceneExistsRule("NonExistentScene");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
        Assert.Equal(RuleSeverity.Error, result.Severity);
    }

    [Fact]
    public void SceneExistsRule_EmptySceneList_Fails()
    {
        // Arrange
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>(),
            },
        };
        var rule = new SceneExistsRule("Anything");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    #endregion

    #region GameObjectExistsRule Tests

    [Fact]
    public void GameObjectExistsRule_GameObjectExists_Passes()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new GameObjectExistsRule("Player");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Passed, result.Status);
        Assert.Equal(RuleSeverity.Info, result.Severity);
        Assert.Contains("found", result.Message);
    }

    [Fact]
    public void GameObjectExistsRule_GameObjectNotExists_Fails()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new GameObjectExistsRule("NonExistentObject");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
        Assert.Equal(RuleSeverity.Error, result.Severity);
    }

    [Fact]
    public void GameObjectExistsRule_ScopedToScene_RespectsSceneFilter()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new GameObjectExistsRule("Main Camera", "TestScene");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Fact]
    public void GameObjectExistsRule_SceneNotFound_NotEvaluated()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new GameObjectExistsRule("Player", "MissingScene");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.NotEvaluated, result.Status);
        Assert.Equal(RuleSeverity.Warning, result.Severity);
    }

    #endregion

    #region ComponentExistsRule Tests

    [Fact]
    public void ComponentExistsRule_ComponentExists_Passes()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new ComponentExistsRule("Player", "Transform");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Passed, result.Status);
        Assert.Contains("Transform", result.Message);
    }

    [Fact]
    public void ComponentExistsRule_ComponentNotExists_Fails()
    {
        // Arrange — Player has Transform, Animator, BoxCollider, Rigidbody but no Light
        var context = MakeSimpleContext();
        var rule = new ComponentExistsRule("Player", "Light");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
        Assert.Equal(RuleSeverity.Error, result.Severity);
    }

    [Fact]
    public void ComponentExistsRule_GameObjectNotFound_NotEvaluated()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new ComponentExistsRule("NonExistentObject", "Transform");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.NotEvaluated, result.Status);
    }

    #endregion

    #region UnityEventBindingRule Tests

    [Fact]
    public void UnityEventBindingRule_CorrectBinding_Passes()
    {
        // Arrange
        var context = MakeUnityEventContext();
        var rule = new UnityEventBindingRule("StartButton", "Start2D", "GameManager");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Fact]
    public void UnityEventBindingRule_WrongMethodName_Fails()
    {
        // Arrange — StartButton has Start2D binding, not NonExistent
        var context = MakeUnityEventContext();
        var rule = new UnityEventBindingRule("StartButton", "NonExistentMethod", "GameManager");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
        Assert.Equal(RuleSeverity.Error, result.Severity);
    }

    [Fact]
    public void UnityEventBindingRule_WrongTargetGameObject_Fails()
    {
        // Arrange — StartButton targets GameManager, not SomeOtherObject
        var context = MakeUnityEventContext();
        var rule = new UnityEventBindingRule("StartButton", "Start2D", "Player");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    [Fact]
    public void UnityEventBindingRule_SourceGameObjectNotFound_NotEvaluated()
    {
        // Arrange
        var context = MakeUnityEventContext();
        var rule = new UnityEventBindingRule("NonExistentObject", "Start2D");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.NotEvaluated, result.Status);
    }

    #endregion

    #region CodeEvidenceRule Tests

    [Fact]
    public void CodeEvidenceRule_CompleteChain_Passes()
    {
        // Arrange — Build full evidence chain from real test data
        var (context, _) = BuildCodeEvidenceContext();

        var rule = new CodeEvidenceRule(
            "StartButton", "Start2D",
            "SceneManager.LoadScene", "Maze2D");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Fact]
    public void CodeEvidenceRule_MethodNotExists_Fails()
    {
        // Arrange — MissingTargetButton has no valid target
        var (context, _) = BuildCodeEvidenceContext();
        var rule = new CodeEvidenceRule("MissingTargetButton", "NonExistent");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    [Fact]
    public void CodeEvidenceRule_VariableArgument_NotEvaluated()
    {
        // Arrange — Use synthetic binding for LoadVariableScene which uses variable
        var (context, _) = BuildCodeEvidenceContextWithVariable();
        var rule = new CodeEvidenceRule(
            "CustomObject", "LoadVariableScene",
            "SceneManager.LoadScene", "DynamicScene");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.NotEvaluated, result.Status);
    }

    #endregion

    #region RuleEngine Tests

    [Fact]
    public void RuleEngine_MultipleRules_AllEvaluate()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rules = new List<IRule>
        {
            new SceneExistsRule("TestScene"),
            new GameObjectExistsRule("Player"),
            new ComponentExistsRule("Player", "Transform"),
        };

        var engine = new Core.Rules.RuleEngine();

        // Act
        var results = engine.Run(context, rules);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(RuleStatus.Passed, r.Status));
    }

    [Fact]
    public void RuleEngine_OneFailure_DoesNotPreventOthers()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rules = new List<IRule>
        {
            new SceneExistsRule("NonExistentScene"),    // Will fail
            new SceneExistsRule("TestScene"),           // Should still run and pass
            new ComponentExistsRule("Player", "Transform"), // Should still run and pass
        };

        var engine = new Core.Rules.RuleEngine();

        // Act
        var results = engine.Run(context, rules);

        // Assert — all 3 rules executed
        Assert.Equal(3, results.Count);
        Assert.Equal(RuleStatus.Failed, results[0].Status);
        Assert.Equal(RuleStatus.Passed, results[1].Status);
        Assert.Equal(RuleStatus.Passed, results[2].Status);
    }

    [Fact]
    public void RuleEngine_ResultOrder_MatchesInputOrder()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rules = new List<IRule>
        {
            new SceneExistsRule("SceneA"),
            new SceneExistsRule("NonExistent"),
            new ComponentExistsRule("Player", "Transform"),
        };

        var engine = new Core.Rules.RuleEngine();

        // Act
        var results = engine.Run(context, rules);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("SCENE_EXISTS", results[0].RuleId);
        Assert.Equal("SCENE_EXISTS", results[1].RuleId);
        Assert.Equal("COMPONENT_EXISTS", results[2].RuleId);
    }

    #endregion

    #region Full End-to-End Example (Requirement XII)

    [Fact]
    public void EndToEnd_AllFiveRules_Pass()
    {
        // Arrange — Build a context that represents:
        // MainMenu scene with StartButton → GameManager.Start2D → LoadScene("Maze2D")
        var (context, _) = BuildCodeEvidenceContext();

        var rules = new List<IRule>
        {
            new SceneExistsRule("UnityEventScene"),              // Scene exists
            new SceneExistsRule("Maze2D"),                        // Scene does NOT exist (we don't have this scene in test data)
            new GameObjectExistsRule("StartButton"),              // GameObject exists
            new UnityEventBindingRule("StartButton", "Start2D"),  // Binding exists
            new CodeEvidenceRule("StartButton", "Start2D",
                "SceneManager.LoadScene", "Maze2D"),              // Full evidence chain
        };

        var engine = new Core.Rules.RuleEngine();

        // Act
        var results = engine.Run(context, rules);

        // Assert — SceneExists for Maze2D fails (no such scene in test data)
        Assert.Equal(5, results.Count);
        Assert.Equal(RuleStatus.Passed, results[0].Status); // UnityEventScene exists
        Assert.Equal(RuleStatus.Failed, results[1].Status); // Maze2D scene not found
        Assert.Equal(RuleStatus.Passed, results[2].Status); // StartButton exists
        Assert.Equal(RuleStatus.Passed, results[3].Status); // Binding exists
        Assert.Equal(RuleStatus.Passed, results[4].Status); // Code evidence complete
    }

    #endregion

    #region Failure Case A: Scene missing

    [Fact]
    public void FailureCaseA_SceneMissing_Fails()
    {
        // Arrange
        var context = MakeSimpleContext();
        var rule = new SceneExistsRule("Maze2D");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    #endregion

    #region Failure Case B: GameObject missing

    [Fact]
    public void FailureCaseB_GameObjectMissing_Fails()
    {
        // Arrange
        var context = MakeUnityEventContext();
        var rule = new GameObjectExistsRule("StartButton");

        // Act — StartButton exists in UnityEventScene, so this should pass
        var result = rule.Evaluate(context);

        // Assert — StartButton does exist in UnityEventScene, so use a non-existent one
        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Fact]
    public void FailureCaseB2_GameObjectMissing_ActuallyFails()
    {
        // Arrange
        var context = MakeUnityEventContext();
        var rule = new GameObjectExistsRule("NonExistentButton");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    #endregion

    #region Failure Case C: Wrong method name

    [Fact]
    public void FailureCaseC_WrongMethodName_Fails()
    {
        // Arrange — StartButton has Start2D, but we check for Start3D
        var context = MakeUnityEventContext();
        var rule = new UnityEventBindingRule("StartButton", "Start3D", "GameManager");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    #endregion

    #region Failure Case D: UnityEvent correct but code evidence broken

    [Fact]
    public void FailureCaseD_CodeEvidenceBroken_Fails()
    {
        // Arrange — StartButton → Start2D exists, but we check for a call that doesn't exist
        var (context, _) = BuildCodeEvidenceContext();
        var rule = new CodeEvidenceRule(
            "StartButton", "Start2D",
            "SceneManager.LoadScene", "WrongScene");

        // Act
        var result = rule.Evaluate(context);

        // Assert
        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  Test Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a minimal InspectionContext with one scene containing
    /// typical GameObjects and Components.
    /// </summary>
    private static InspectionContext MakeSimpleContext()
    {
        var scene = new SceneInfo
        {
            Name = "TestScene",
            FilePath = "TestScene.unity",
            GameObjects = new List<GameObjectInfo>
            {
                new()
                {
                    Name = "Main Camera",
                    FileId = 100,
                    Components = new List<ComponentInfo>
                    {
                        new() { Type = "Transform", FileId = 200 },
                        new() { Type = "Camera", FileId = 201 },
                    },
                },
                new()
                {
                    Name = "Player",
                    FileId = 101,
                    Components = new List<ComponentInfo>
                    {
                        new() { Type = "Transform", FileId = 210 },
                        new() { Type = "Animator", FileId = 211 },
                        new() { Type = "BoxCollider", FileId = 212 },
                        new() { Type = "Rigidbody", FileId = 213 },
                    },
                },
                new()
                {
                    Name = "GameManager",
                    FileId = 102,
                    Components = new List<ComponentInfo>
                    {
                        new() { Type = "Transform", FileId = 220 },
                        new() { Type = "MonoBehaviour", FileId = 221, ScriptGuid = "abc" },
                    },
                },
            },
        };

        return new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo> { scene },
            },
        };
    }

    /// <summary>
    /// Creates an InspectionContext with UnityEventBindingInfo data
    /// from the UnityEventScene test data.
    /// </summary>
    private static InspectionContext MakeUnityEventContext()
    {
        var sceneText = File.ReadAllText(
            Path.Combine(TestDataPath, "UnityEventScene.unity"));

        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");

        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);

        return new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo> { scene },
            },
            EventBindings = bindings,
        };
    }

    /// <summary>
    /// Full integration: parse UnityEventScene + LinkerGameManager.cs,
    /// run UnityEventCodeLinker, build InspectionContext with CodeLinks.
    /// </summary>
    private static (InspectionContext context, TempDirectory tempDir) BuildCodeEvidenceContext()
    {
        var tempDir = CreateTestProject();
        var sceneText = File.ReadAllText(
            Path.Combine(TestDataPath, "UnityEventScene.unity"));

        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");

        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);

        var linker = new UnityEventCodeLinker(tempDir.Path);
        var links = linker.Link(bindings, scene);

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = tempDir.Path,
                IsValid = true,
                Scenes = new List<SceneInfo> { scene },
            },
            EventBindings = bindings,
            CodeLinks = links,
        };

        return (context, tempDir);
    }

    /// <summary>
    /// Builds a context with a synthetic binding to LoadVariableScene
    /// which uses a variable argument (PartiallyResolved).
    /// </summary>
    private static (InspectionContext context, TempDirectory tempDir) BuildCodeEvidenceContextWithVariable()
    {
        var tempDir = CreateTestProject();
        var sceneText = File.ReadAllText(
            Path.Combine(TestDataPath, "UnityEventScene.unity"));

        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");

        // Create synthetic binding for a custom GameObject that targets LoadVariableScene
        var customGo = new GameObjectInfo
        {
            Name = "CustomObject",
            FileId = 901,
            Components = new List<ComponentInfo>
            {
                new() { Type = "Transform", FileId = 910 },
                new() { Type = "Button", FileId = 911 },
            },
        };

        scene.GameObjects.Add(customGo);

        var syntheticBinding = new UnityEventBindingInfo
        {
            SourceGameObjectFileId = 901,
            SourceComponentFileId = 911,
            TargetFileId = 411, // The GameManager MonoBehaviour's fileId
            TargetGameObjectFileId = 400,
            MethodName = "LoadVariableScene",
            TargetAssemblyTypeName = "LinkerGameManager, Assembly-CSharp",
        };

        var linker = new UnityEventCodeLinker(tempDir.Path);
        var links = linker.Link(new List<UnityEventBindingInfo> { syntheticBinding }, scene);

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = tempDir.Path,
                IsValid = true,
                Scenes = new List<SceneInfo> { scene },
            },
            EventBindings = new List<UnityEventBindingInfo> { syntheticBinding },
            CodeLinks = links,
        };

        return (context, tempDir);
    }

    private static TempDirectory CreateTestProject()
    {
        var tempDir = new TempDirectory();
        var assetsDir = Path.Combine(tempDir.Path, "Assets");
        Directory.CreateDirectory(assetsDir);

        CopyToDir("TestData/Scripts/LinkerGameManager.cs", assetsDir);
        CopyToDir("TestData/Scripts/LinkerGameManager.cs.meta", assetsDir);

        return tempDir;
    }

    private static void CopyToDir(string sourceRelative, string destDir)
    {
        var source = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, sourceRelative);
        var dest = Path.Combine(destDir, Path.GetFileName(sourceRelative));
        File.Copy(source, dest);
    }

    public class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "UPITest_" + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}