using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Tests;

public class CSharpAnalyzerTests
{
    private readonly CSharpAnalyzer _analyzer = new();

    private static string SampleFilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Scripts", "AnalysisSample.cs");

    #region Analysis Sample File Tests

    [Fact]
    public void AnalyzeSource_IdentifiesClassName()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);

        // Act
        var result = _analyzer.AnalyzeSource(source, filePath);

        // Assert
        Assert.Single(result.Classes);
        Assert.Equal("GameManager", result.Classes[0].Name);
    }

    [Fact]
    public void AnalyzeSource_IdentifiesNamespace()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);

        // Act
        var result = _analyzer.AnalyzeSource(source, filePath);

        // Assert
        Assert.Equal("TestProject", result.Classes[0].Namespace);
    }

    [Fact]
    public void AnalyzeSource_FindsAllMethods()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);

        // Act
        var result = _analyzer.AnalyzeSource(source, filePath);
        var methodNames = result.Classes[0].Methods.Select(m => m.Name).OrderBy(n => n).ToList();

        // Assert
        Assert.Contains("Start2D", methodNames);
        Assert.Contains("Start3D", methodNames);
        Assert.Contains("QuitGame", methodNames);
        Assert.Contains("LogMessage", methodNames);
        Assert.Contains("FakeCall", methodNames);
    }

    [Fact]
    public void AnalyzeSource_Start2D_ContainsSceneManagerLoadScene()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);
        var start2D = result.Classes[0].Methods.Single(m => m.Name == "Start2D");

        // Assert
        Assert.Single(start2D.Calls);
        Assert.Equal("SceneManager.LoadScene", start2D.Calls[0].Target);
    }

    [Fact]
    public void AnalyzeSource_Start2D_HasCorrectArgument()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);
        var start2D = result.Classes[0].Methods.Single(m => m.Name == "Start2D");

        // Assert
        Assert.Single(start2D.Calls[0].Arguments);
        Assert.Equal("\"Maze2D\"", start2D.Calls[0].Arguments[0]);
    }

    [Fact]
    public void AnalyzeSource_Start3D_HasCorrectArgument()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);
        var start3D = result.Classes[0].Methods.Single(m => m.Name == "Start3D");

        // Assert
        Assert.Single(start3D.Calls[0].Arguments);
        Assert.Equal("\"Maze3D\"", start3D.Calls[0].Arguments[0]);
    }

    [Fact]
    public void AnalyzeSource_QuitGame_HasApplicationQuit()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);
        var quitGame = result.Classes[0].Methods.Single(m => m.Name == "QuitGame");

        // Assert
        Assert.Single(quitGame.Calls);
        Assert.Equal("Application.Quit", quitGame.Calls[0].Target);
        Assert.Empty(quitGame.Calls[0].Arguments);
    }

    [Fact]
    public void AnalyzeSource_LogMessage_HasDebugLog()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);
        var logMessage = result.Classes[0].Methods.Single(m => m.Name == "LogMessage");

        // Assert
        Assert.Single(logMessage.Calls);
        Assert.Equal("Debug.Log", logMessage.Calls[0].Target);
        Assert.Single(logMessage.Calls[0].Arguments);
        Assert.Equal("\"Hello\"", logMessage.Calls[0].Arguments[0]);
    }

    [Fact]
    public void AnalyzeSource_CommentsAreNotIdentifiedAsCalls()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);

        // Assert — "FakeScene" only exists in comments, never as a real argument
        var allArgs = result.Classes[0].Methods
            .SelectMany(m => m.Calls)
            .SelectMany(c => c.Arguments)
            .ToList();

        Assert.DoesNotContain("FakeScene", allArgs);
    }

    [Fact]
    public void AnalyzeSource_StringIsNotIdentifiedAsCall()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);

        // FakeCall method should have ZERO calls (the string literal is not an invocation)
        var fakeCall = result.Classes[0].Methods.Single(m => m.Name == "FakeCall");

        // Assert
        Assert.Empty(fakeCall.Calls);
    }

    [Fact]
    public void AnalyzeSource_QuitGame_HasNoArguments()
    {
        // Arrange
        var filePath = SampleFilePath;
        var source = File.ReadAllText(filePath);
        var result = _analyzer.AnalyzeSource(source, filePath);
        var quitGame = result.Classes[0].Methods.Single(m => m.Name == "QuitGame");

        // Assert
        Assert.Empty(quitGame.Calls[0].Arguments);
    }

    #endregion

    #region File-based tests

    [Fact]
    public void AnalyzeFile_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistent = Path.Combine("TestData", "Scripts", "DoesNotExist.cs");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => _analyzer.AnalyzeFile(nonExistent));
    }

    [Fact]
    public void AnalyzeFile_ReadsFilePath()
    {
        // Arrange
        var filePath = SampleFilePath;

        // Act
        var result = _analyzer.AnalyzeFile(filePath);

        // Assert
        Assert.Equal(filePath, result.FilePath);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void AnalyzeSource_EmptySource_DoesNotCrash()
    {
        // Act
        var result = _analyzer.AnalyzeSource("", "empty.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Classes);
    }

    [Fact]
    public void AnalyzeSource_WhitespaceOnly_DoesNotCrash()
    {
        // Act
        var result = _analyzer.AnalyzeSource("   \n  \n  ", "whitespace.cs");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Classes);
    }

    [Fact]
    public void AnalyzeSource_SyntaxErrorSource_DoesNotCrash()
    {
        // Arrange — source with syntax error: missing closing brace
        const string brokenSource = @"
public class Broken
{
    public void DoSomething()
    {
        Debug.Log(""hello"");
    }
// Missing closing brace
";

        // Act — should not throw despite syntax errors
        var result = _analyzer.AnalyzeSource(brokenSource, "broken.cs");

        // Assert — Roslyn still produces a partial tree
        Assert.NotNull(result);
        Assert.NotEmpty(result.Classes);
    }

    [Fact]
    public void AnalyzeSource_SyntaxError_StillFindsAvailableMethods()
    {
        // Arrange — source with partial info
        const string brokenSource = @"
public class Partial
{
    public void ValidMethod()
    {
        Application.Quit();
    }

    public void InvalidMethod(
// Missing closing parens and brace
";

        // Act
        var result = _analyzer.AnalyzeSource(brokenSource, "partial.cs");

        // Assert — ValidMethod should still be fully parsed
        var validMethod = result.Classes
            .SelectMany(c => c.Methods)
            .FirstOrDefault(m => m.Name == "ValidMethod");

        Assert.NotNull(validMethod);
        Assert.Single(validMethod.Calls);
        Assert.Equal("Application.Quit", validMethod.Calls[0].Target);
    }

    [Fact]
    public void AnalyzeSource_NoNamespace_ClassNameIsFound()
    {
        // Arrange
        const string source = @"
public class NoNamespaceClass
{
    public void DoThing()
    {
        System.Console.WriteLine(""hi"");
    }
}
";

        // Act
        var result = _analyzer.AnalyzeSource(source, "nonamespace.cs");

        // Assert
        Assert.Single(result.Classes);
        Assert.Equal("NoNamespaceClass", result.Classes[0].Name);
        Assert.Null(result.Classes[0].Namespace);
        Assert.Single(result.Classes[0].Methods[0].Calls);
        Assert.Equal("System.Console.WriteLine", result.Classes[0].Methods[0].Calls[0].Target);
    }

    [Fact]
    public void AnalyzeSource_FileScopedNamespace_IsRecognized()
    {
        // Arrange
        const string source = @"
namespace MyGame;

public class Player
{
    public void Move()
    {
    }
}
";

        // Act
        var result = _analyzer.AnalyzeSource(source, "player.cs");

        // Assert
        Assert.Single(result.Classes);
        Assert.Equal("Player", result.Classes[0].Name);
        Assert.Equal("MyGame", result.Classes[0].Namespace);
    }

    [Fact]
    public void AnalyzeSource_NoMethods_HasClassOnly()
    {
        // Arrange
        const string source = @"
public class EmptyClass
{
}
";

        // Act
        var result = _analyzer.AnalyzeSource(source, "emptyclass.cs");

        // Assert
        Assert.Single(result.Classes);
        Assert.Equal("EmptyClass", result.Classes[0].Name);
        Assert.Empty(result.Classes[0].Methods);
    }

    [Fact]
    public void AnalyzeSource_SimpleMethodCall_NoArguments()
    {
        // Arrange
        const string source = @"
public class Simple
{
    public void Run()
    {
        SomeMethod();
    }
}
";

        // Act
        var result = _analyzer.AnalyzeSource(source, "simple.cs");

        // Assert
        Assert.Single(result.Classes[0].Methods[0].Calls);
        Assert.Equal("SomeMethod", result.Classes[0].Methods[0].Calls[0].Target);
        Assert.Empty(result.Classes[0].Methods[0].Calls[0].Arguments);
    }

    [Fact]
    public void AnalyzeSource_MultipleCallsInMethod_AllFound()
    {
        // Arrange
        const string source = @"
public class MultiCaller
{
    public void DoAll()
    {
        First.Call();
        Second.Call();
        Third.Call();
    }
}
";

        // Act
        var result = _analyzer.AnalyzeSource(source, "multi.cs");

        // Assert
        var calls = result.Classes[0].Methods[0].Calls;
        Assert.Equal(3, calls.Count);
        Assert.Equal("First.Call", calls[0].Target);
        Assert.Equal("Second.Call", calls[1].Target);
        Assert.Equal("Third.Call", calls[2].Target);
    }

    #endregion
}