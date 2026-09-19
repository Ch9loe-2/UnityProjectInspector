using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Parsing;

namespace UnityProjectInspector.Tests;

public class UnityEventCodeLinkerTests
{
    private static string TestDataPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");

    private static string ScriptsPath =>
        Path.Combine(TestDataPath, "Scripts");

    #region Test 1 & 2: StartButton → Start2D → SceneManager.LoadScene

    [Fact]
    public void Link_StartButton_ResolvesStart2DMethod()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert
        var startButtonLink = FindLink(links, "StartButton");
        Assert.NotNull(startButtonLink);
        Assert.Equal("Start2D", startButtonLink.MethodName);
        Assert.Equal("LinkerGameManager", startButtonLink.ClassName);
        Assert.NotEqual(LinkStatus.Unresolved, startButtonLink.Status);
    }

    [Fact]
    public void Link_StartButton_Start2DContainsSceneManagerLoadScene()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert
        var startButtonLink = FindLink(links, "StartButton");
        Assert.NotNull(startButtonLink);
        var loadSceneCall = startButtonLink.Calls
            .FirstOrDefault(c => c.FullTarget == "SceneManager.LoadScene");
        Assert.NotNull(loadSceneCall);
    }

    [Fact]
    public void Link_StartButton_LoadSceneArgumentIsMaze2D()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert
        var startButtonLink = FindLink(links, "StartButton");
        Assert.NotNull(startButtonLink);
        var loadSceneCall = startButtonLink.Calls
            .FirstOrDefault(c => c.FullTarget == "SceneManager.LoadScene");
        Assert.NotNull(loadSceneCall);
        Assert.Single(loadSceneCall.Arguments);
        Assert.Equal("\"Maze2D\"", loadSceneCall.Arguments[0]);
        Assert.True(loadSceneCall.AreAllArgumentsStatic);
    }

    #endregion

    #region Test 4: Start3DButton → Start3D → LoadScene("Maze3D")

    [Fact]
    public void Link_Start3DButton_ResolvesStart3DMethod()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert
        var link = FindLink(links, "Start3DButton");
        Assert.NotNull(link);
        Assert.Equal("Start3D", link.MethodName);
        Assert.Equal(LinkStatus.Resolved, link.Status);

        var loadSceneCall = link.Calls
            .FirstOrDefault(c => c.FullTarget == "SceneManager.LoadScene");
        Assert.NotNull(loadSceneCall);
        Assert.Equal("\"Maze3D\"", loadSceneCall.Arguments[0]);
    }

    #endregion

    #region Test 5: Missing method (NotExist) → Unresolved

    [Fact]
    public void Link_MissingTargetButton_ReturnsUnresolved()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert — MissingTargetButton has fileID 0, can't resolve
        var missingLink = FindLink(links, "MissingTargetButton");
        Assert.NotNull(missingLink);
        Assert.Equal(LinkStatus.Unresolved, missingLink.Status);
    }

    #endregion

    #region Test 6: Method name doesn't exist in C# → Unresolved

    [Fact]
    public void Link_QuitGame_ResolvedButMethodFound()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert — MultiCallButton has QuitGame binding, which exists in the C#
        var multiCalls = FindLinks(links, "MultiCallButton");
        var quitGameLink = multiCalls.FirstOrDefault(l => l.MethodName == "QuitGame");
        Assert.NotNull(quitGameLink);
        Assert.Equal(LinkStatus.Resolved, quitGameLink.Status);
        Assert.Equal("LinkerGameManager", quitGameLink.ClassName);
    }

    #endregion

    #region Test 7: Method exists but no SceneManager.LoadScene

    [Fact]
    public void Link_MethodExists_ButNoLoadScene()
    {
        // Arrange — QuitGame exists but calls Debug.Log, not SceneManager.LoadScene
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);
        var multi = FindLinks(links, "MultiCallButton");
        var quitGameLink = multi.FirstOrDefault(l => l.MethodName == "QuitGame");

        // Assert — method exists, calls are present, but none is SceneManager.LoadScene
        Assert.NotNull(quitGameLink);
        Assert.Equal(LinkStatus.Resolved, quitGameLink.Status);
        Assert.NotEmpty(quitGameLink.Calls);
        Assert.DoesNotContain(quitGameLink.Calls, c => c.FullTarget == "SceneManager.LoadScene");
        Assert.Contains(quitGameLink.Calls, c => c.FullTarget == "Debug.Log");
    }

    #endregion

    #region Test 8: Variable argument → PartiallyResolved

    [Fact]
    public void Link_VariableArgument_IsPartiallyResolved()
    {
        // Arrange — We need a binding that points to LoadVariableScene
        // We'll test this via the CSharpAnalyzer directly since the scene doesn't have
        // such a binding. Instead we create a synthetic scenario using the existing setup.
        using var projectDir = CreateTestProject();
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Create a synthetic binding pointing to LoadVariableScene
        // First parse the scene normally
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var allBindings = eventParser.Parse(sceneText, scene);

        // Create a synthetic binding that targets LoadVariableScene
        // Find the target component (fileId 411) from existing scene
        var syntheticBindings = new List<UnityEventBindingInfo>
        {
            new()
            {
                SourceGameObjectFileId = 301,
                SourceComponentFileId = 321,
                TargetFileId = 411,
                TargetGameObjectFileId = 400,
                MethodName = "LoadVariableScene",
                TargetAssemblyTypeName = "LinkerGameManager, Assembly-CSharp",
            }
        };

        // Act
        var links = linker.Link(syntheticBindings, scene);

        // Assert
        var link = links[0];
        Assert.Equal("LoadVariableScene", link.MethodName);
        Assert.Equal(LinkStatus.PartiallyResolved, link.Status);

        var loadSceneCall = link.Calls
            .FirstOrDefault(c => c.FullTarget == "SceneManager.LoadScene");
        Assert.NotNull(loadSceneCall);
        Assert.Single(loadSceneCall.Arguments);
        Assert.Equal("sceneName", loadSceneCall.Arguments[0]);
        Assert.False(loadSceneCall.AreAllArgumentsStatic);
    }

    #endregion

    #region Test 9 + 10: Overall counts and NoEventObject

    [Fact]
    public void Link_TotalLinkCount()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert — 5 bindings: StartButton(1) + Start3DButton(1) + MultiCallButton(2) + MissingTargetButton(1)
        Assert.Equal(5, links.Count);
    }

    [Fact]
    public void Link_NoEventObject_NoLinks()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);
        var links = linker.Link(bindings, scene);

        // Assert — NoEventObject has no bindings, so no links from it
        var noEventLinks = links.Where(l => l.SourceGameObjectFileId == 500).ToList();
        Assert.Empty(noEventLinks);
    }

    #endregion

    #region MultiCallButton — both Start2D and QuitGame

    [Fact]
    public void Link_MultiCallButton_BothBindingsHaveCorrectCalls()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var sceneText = File.ReadAllText(Path.Combine(TestDataPath, "UnityEventScene.unity"));
        var sceneParser = new SceneParser();
        var scene = sceneParser.ParseText(sceneText, "UnityEventScene.unity");
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(bindings, scene);
        var multiLinks = FindLinks(links, "MultiCallButton");

        // Assert — 2 links: Start2D and QuitGame
        Assert.Equal(2, multiLinks.Count);

        var start2D = multiLinks.First(l => l.MethodName == "Start2D");
        Assert.Equal(LinkStatus.Resolved, start2D.Status);
        Assert.Contains(start2D.Calls, c => c.FullTarget == "SceneManager.LoadScene");

        var quitGame = multiLinks.First(l => l.MethodName == "QuitGame");
        Assert.Equal(LinkStatus.Resolved, quitGame.Status);
        Assert.Contains(quitGame.Calls, c => c.FullTarget == "Debug.Log");
    }

    #endregion

    #region Edge: Empty scene

    [Fact]
    public void Link_EmptyBindings_ReturnsEmpty()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var scene = new SceneInfo { Name = "empty", FilePath = "empty.unity" };
        var linker = new UnityEventCodeLinker(projectDir.Path);

        // Act
        var links = linker.Link(new List<UnityEventBindingInfo>(), scene);

        // Assert
        Assert.Empty(links);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a temporary Unity-like project with LinkerGameManager.cs.
    /// </summary>
    private static TempDirectory CreateTestProject()
    {
        var tempDir = new TempDirectory();

        var assetsDir = Path.Combine(tempDir.Path, "Assets");
        Directory.CreateDirectory(assetsDir);

        // Copy the linker game manager script
        CopyToDir("TestData/Scripts/LinkerGameManager.cs", assetsDir);
        CopyToDir("TestData/Scripts/LinkerGameManager.cs.meta", assetsDir);

        return tempDir;
    }

    /// <summary>
    /// Helper to find the first link originating from a given source GameObject name.
    /// Uses known test data fileId mapping.
    /// </summary>
    private static UnityEventCodeLink? FindLink(List<UnityEventCodeLink> links, string sourceGameObjectName)
    {
        var fileId = GetSourceFileId(sourceGameObjectName);
        return links.FirstOrDefault(l => l.SourceGameObjectFileId == fileId);
    }

    /// <summary>
    /// Helper to find all links originating from a given source GameObject name.
    /// </summary>
    private static List<UnityEventCodeLink> FindLinks(List<UnityEventCodeLink> links, string sourceGameObjectName)
    {
        var fileId = GetSourceFileId(sourceGameObjectName);
        return links.Where(l => l.SourceGameObjectFileId == fileId).ToList();
    }

    private static long GetSourceFileId(string gameObjectName)
    {
        return gameObjectName switch
        {
            "StartButton" => 301,
            "Start3DButton" => 302,
            "MultiCallButton" => 600,
            "MissingTargetButton" => 700,
            "NoCallsObject" => 800,
            _ => 0,
        };
    }

    private static void CopyToDir(string sourceRelative, string destDir)
    {
        var source = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            sourceRelative);
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

    #endregion
}