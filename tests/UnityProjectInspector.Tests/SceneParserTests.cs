using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Tests;

public class SceneParserTests
{
    private readonly SceneParser _parser = new();

    #region Basic GameObject Parsing

    [Fact]
    public void Parse_SimpleScene_IdentifiesMultipleGameObjects()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");

        // Act
        var scene = _parser.Parse(testFile);

        // Assert
        Assert.NotNull(scene);
        Assert.Equal(8, scene.GameObjects.Count);
    }

    [Fact]
    public void Parse_SimpleScene_ReadsGameObjectNames()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");

        // Act
        var scene = _parser.Parse(testFile);

        // Assert
        var names = scene.GameObjects.Select(g => g.Name).OrderBy(n => n).ToList();
        Assert.Contains("Main Camera", names);
        Assert.Contains("Directional Light", names);
        Assert.Contains("Player", names);
        Assert.Contains("Weapon", names);
        Assert.Contains("Ground", names);
        Assert.Contains("UI Canvas", names);
        Assert.Contains("PlayButton", names);
        Assert.Contains("GameManager", names);
    }

    [Fact]
    public void Parse_SimpleScene_ReadsGameObjectFileIds()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");

        // Act
        var scene = _parser.Parse(testFile);

        // Assert
        var ids = scene.GameObjects.Select(g => g.FileId).OrderBy(id => id).ToList();
        Assert.Equal([100, 101, 102, 103, 104, 105, 106, 107], ids);
    }

    #endregion

    #region Component Parsing

    [Fact]
    public void Parse_SimpleScene_IdentifiesBasicComponents()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");

        // Act
        var scene = _parser.Parse(testFile);
        var player = scene.GameObjects.Single(g => g.Name == "Player");

        // Assert - Player has known components: Transform, Animator, BoxCollider, Rigidbody
        var componentTypes = player.Components.Select(c => c.Type).OrderBy(t => t).ToList();
        Assert.Contains("Animator", componentTypes);
        Assert.Contains("BoxCollider", componentTypes);
        Assert.Contains("Rigidbody", componentTypes);
        Assert.Contains("Transform", componentTypes);
    }

    [Fact]
    public void Parse_SimpleScene_ComponentFileIdsAreCorrect()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var mainCamera = scene.GameObjects.Single(g => g.Name == "Main Camera");

        // Act
        var cameraComp = mainCamera.Components.Single(c => c.Type == "Camera");

        // Assert
        Assert.Equal(200, cameraComp.FileId);
    }

    [Fact]
    public void Parse_SimpleScene_MainCamera_HasCameraComponent()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var mainCamera = scene.GameObjects.Single(g => g.Name == "Main Camera");

        // Assert
        Assert.Contains(mainCamera.Components, c => c.Type == "Camera");
        Assert.Contains(mainCamera.Components, c => c.Type == "Transform");
    }

    [Fact]
    public void Parse_SimpleScene_Light_HasLightComponent()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var light = scene.GameObjects.Single(g => g.Name == "Directional Light");

        // Assert
        Assert.Contains(light.Components, c => c.Type == "Light");
        Assert.Contains(light.Components, c => c.Type == "Transform");
    }

    [Fact]
    public void Parse_SimpleScene_Ground_HasMeshRenderer()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var ground = scene.GameObjects.Single(g => g.Name == "Ground");

        // Assert
        Assert.Contains(ground.Components, c => c.Type == "MeshRenderer");
    }

    [Fact]
    public void Parse_SimpleScene_Canvas_HasCanvasComponent()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var uiCanvas = scene.GameObjects.Single(g => g.Name == "UI Canvas");

        // Assert
        Assert.Contains(uiCanvas.Components, c => c.Type == "Canvas");
        Assert.Contains(uiCanvas.Components, c => c.Type == "RectTransform");
    }

    [Fact]
    public void Parse_SimpleScene_PlayButton_HasButton()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var playButton = scene.GameObjects.Single(g => g.Name == "PlayButton");

        // Assert
        Assert.Contains(playButton.Components, c => c.Type == "Button");
        Assert.Contains(playButton.Components, c => c.Type == "RectTransform");
    }

    #endregion

    #region MonoBehaviour Parsing

    [Fact]
    public void Parse_SimpleScene_IdentifiesMonoBehaviour()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var player = scene.GameObjects.Single(g => g.Name == "Player");

        // Assert
        Assert.Contains(player.Components, c => c.Type == "MonoBehaviour");
    }

    [Fact]
    public void Parse_SimpleScene_MonoBehaviourHasCorrectFileId()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var player = scene.GameObjects.Single(g => g.Name == "Player");
        var mono = player.Components.Single(c => c.Type == "MonoBehaviour");

        // Assert
        Assert.Equal(221, mono.FileId);
    }

    [Fact]
    public void Parse_SimpleScene_MonoBehaviourHasScriptGuid()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var player = scene.GameObjects.Single(g => g.Name == "Player");
        var mono = player.Components.Single(c => c.Type == "MonoBehaviour");

        // Assert
        Assert.Equal("abcdef1234567890abcdef1234567890", mono.ScriptGuid);
    }

    [Fact]
    public void Parse_SimpleScene_NonMonoBehaviourHasNoScriptGuid()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var camera = scene.GameObjects.Single(g => g.Name == "Main Camera");
        var cameraComp = camera.Components.Single(c => c.Type == "Camera");

        // Assert
        Assert.Null(cameraComp.ScriptGuid);
    }

    [Fact]
    public void Parse_SimpleScene_MultipleMonoBehaviours_HaveDifferentGuids()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var player = scene.GameObjects.Single(g => g.Name == "Player");
        var gameManager = scene.GameObjects.Single(g => g.Name == "GameManager");

        var playerMono = player.Components.Single(c => c.Type == "MonoBehaviour");
        var gmMono = gameManager.Components.Single(c => c.Type == "MonoBehaviour");

        // Assert
        Assert.Equal("abcdef1234567890abcdef1234567890", playerMono.ScriptGuid);
        Assert.Equal("fedcba0987654321fedcba0987654321", gmMono.ScriptGuid);
    }

    [Fact]
    public void Parse_SimpleScene_GameManager_HasOnlyMonoBehaviour()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var gameManager = scene.GameObjects.Single(g => g.Name == "GameManager");

        // Assert — GameManager has only a MonoBehaviour (no Transform known, no other component)
        Assert.Single(gameManager.Components);
        Assert.Equal("MonoBehaviour", gameManager.Components[0].Type);
        Assert.Equal(270, gameManager.Components[0].FileId);
        Assert.Equal("fedcba0987654321fedcba0987654321", gameManager.Components[0].ScriptGuid);
    }

    #endregion

    #region Parent-Child Hierarchy

    [Fact]
    public void Parse_SimpleScene_RootObjectsHaveNoParent()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);

        // Most objects in this test scene are root (m_Father: {fileID: 0})
        var rootObjects = scene.GameObjects.Where(g => g.ParentFileId == 0).ToList();
        Assert.Contains(rootObjects, g => g.Name == "Main Camera");
        Assert.Contains(rootObjects, g => g.Name == "Player");
        Assert.Contains(rootObjects, g => g.Name == "Ground");
    }

    [Fact]
    public void Parse_SimpleScene_WeaponIsChildOfPlayer()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var player = scene.GameObjects.Single(g => g.Name == "Player");
        var weapon = scene.GameObjects.Single(g => g.Name == "Weapon");

        // Assert - Weapon's Transform has m_Father: {fileID: 220} which is Player's Transform
        Assert.Equal(player.FileId, weapon.ParentFileId);
    }

    [Fact]
    public void Parse_SimpleScene_PlayButtonIsChildOfUICanvas()
    {
        // Arrange
        var testFile = Path.Combine("TestData", "SimpleScene.unity");
        var scene = _parser.Parse(testFile);
        var uiCanvas = scene.GameObjects.Single(g => g.Name == "UI Canvas");
        var playButton = scene.GameObjects.Single(g => g.Name == "PlayButton");

        // Assert - PlayButton's RectTransform m_Father: {fileID: 250} = UI Canvas's RectTransform
        Assert.Equal(uiCanvas.FileId, playButton.ParentFileId);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_EmptyScene_DoesNotCrash()
    {
        // Arrange
        var scene = _parser.ParseText("", "empty.unity");

        // Assert
        Assert.NotNull(scene);
        Assert.Empty(scene.GameObjects);
    }

    [Fact]
    public void Parse_WhitespaceOnlyScene_DoesNotCrash()
    {
        // Arrange
        var scene = _parser.ParseText("   \n  \n  ", "whitespace.unity");

        // Assert
        Assert.NotNull(scene);
        Assert.Empty(scene.GameObjects);
    }

    [Fact]
    public void Parse_SceneWithOnlyMetadata_DoesNotCrash()
    {
        // Arrange - scene with header lines but no GameObjects
        const string sceneText = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
SceneSettings:
  m_ObjectHideFlags: 0
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
";

        // Act
        var scene = _parser.ParseText(sceneText, "empty_scene.unity");

        // Assert
        Assert.NotNull(scene);
        Assert.Empty(scene.GameObjects);
    }

    [Fact]
    public void Parse_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine("TestData", "DoesNotExist.unity");

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() => _parser.Parse(nonExistentFile));
        Assert.Contains("DoesNotExist.unity", exception.FileName);
    }

    [Fact]
    public void Parse_SceneWithUnnamedGameObject_DefaultsName()
    {
        // Arrange
        const string sceneText = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &999
GameObject:
  m_ObjectHideFlags: 0
  m_Component:
  - component: {fileID: 998}
--- !u!4 &998
Transform:
  m_ObjectHideFlags: 0
  m_Father: {fileID: 0}
";

        // Act
        var scene = _parser.ParseText(sceneText, "unnamed.unity");

        // Assert
        Assert.Single(scene.GameObjects);
        Assert.Equal("Unnamed GameObject", scene.GameObjects[0].Name);
    }

    #endregion
}