using UnityProjectInspector.Core.Parsing;

namespace UnityProjectInspector.Tests;

public class ScriptResolverTests
{
    #region Test 1: Known GUID resolves correct .cs

    [Fact]
    public void Resolve_KnownScriptGuid_ReturnsScriptInfoWithCorrectPath()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act
        var result = resolver.ResolveScript("11111111111111111111111111111111");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsResolved);
        Assert.EndsWith("GameManager.cs", result.ScriptPath);
        Assert.Equal("GameManager", result.ScriptName);
    }

    #endregion

    #region Test 2: Second known GUID resolves correctly

    [Fact]
    public void Resolve_KnownScriptGuid_ReturnsPlayerController()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act
        var result = resolver.ResolveScript("22222222222222222222222222222222");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsResolved);
        Assert.EndsWith("PlayerController.cs", result.ScriptPath);
        Assert.Equal("PlayerController", result.ScriptName);
    }

    #endregion

    #region Test 3: Unknown GUID returns null

    [Fact]
    public void Resolve_UnknownGuid_ReturnsNull()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act
        var result = resolver.ResolveScript("99999999999999999999999999999999");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Test 4: Non-.cs GUID returns null from ResolveScript

    [Fact]
    public void Resolve_NonCsGuid_ReturnsNull()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act — GUID 333... points to a .prefab.meta, not a .cs
        var result = resolver.ResolveScript("33333333333333333333333333333333");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveRaw_NonCsGuid_ReturnsScriptInfo()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act — ResolveRaw should still return the info even for non-.cs assets
        var result = resolver.ResolveRaw("33333333333333333333333333333333");

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsResolved);
        Assert.EndsWith("SomePrefab.prefab.meta", result.FilePath);
    }

    #endregion

    #region Test 5: .meta exists but .cs is missing

    [Fact]
    public void Resolve_MetaExistsButCsMissing_ReturnsNull()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        // OrphanScript.cs.meta exists but no OrphanScript.cs in our setup
        var resolver = new ScriptResolver(projectDir.Path);

        // Act — GUID 444... has .meta but no .cs
        var result = resolver.ResolveScript("44444444444444444444444444444444");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ResolveRaw_MetaExistsButCsMissing_ReturnsUnresolvedInfo()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act
        var result = resolver.ResolveRaw("44444444444444444444444444444444");

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsResolved);
        Assert.Null(result.ScriptPath);
        Assert.EndsWith("OrphanScript.cs.meta", result.FilePath);
    }

    #endregion

    #region Test 6: Multiple .meta files — deterministic results

    [Fact]
    public void Resolve_MultipleMetaFiles_AllGuidsResolveCorrectly()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act — resolve all scripts
        var scripts = resolver.GetAllScripts();

        // Assert
        Assert.Equal(2, scripts.Count); // Only 2 valid .cs scripts
        var guids = scripts.Select(s => s.Guid).OrderBy(g => g).ToList();
        Assert.Contains("11111111111111111111111111111111", guids);
        Assert.Contains("22222222222222222222222222222222", guids);
    }

    [Fact]
    public void Resolve_ResolutionIsIdempotent()
    {
        // Arrange
        using var projectDir = CreateTestProject();
        var resolver = new ScriptResolver(projectDir.Path);

        // Act — resolve the same GUID twice
        var first = resolver.ResolveScript("11111111111111111111111111111111");
        var second = resolver.ResolveScript("11111111111111111111111111111111");

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.ScriptPath, second.ScriptPath);
    }

    #endregion

    #region Test 7: Edge cases

    [Fact]
    public void Resolve_EmptyAssetsDirectory_ReturnsNull()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets"));
        var resolver = new ScriptResolver(tempDir.Path);

        // Act
        var result = resolver.ResolveScript("anything");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_NonExistentPath_DoesNotThrow()
    {
        // Act (should not throw — resolution happens lazily)
        var resolver = new ScriptResolver("/nonexistent/path");

        // Assert — calling ResolveScript on non-existent path should return null
        var result = resolver.ResolveScript("anything");
        Assert.Null(result);
    }

    #endregion

    /// <summary>
    /// Creates a temporary Unity-like project with test scripts and meta files.
    /// </summary>
    private static TempDirectory CreateTestProject()
    {
        var tempDir = new TempDirectory();

        // Create Assets directory
        var assetsDir = Path.Combine(tempDir.Path, "Assets");
        Directory.CreateDirectory(assetsDir);

        // Copy test scripts and .meta files
        CopyToDir("TestData/Scripts/GameManager.cs", assetsDir);
        CopyToDir("TestData/Scripts/GameManager.cs.meta", assetsDir);
        CopyToDir("TestData/Scripts/PlayerController.cs", assetsDir);
        CopyToDir("TestData/Scripts/PlayerController.cs.meta", assetsDir);
        CopyToDir("TestData/Scripts/OrphanScript.cs.meta", assetsDir);
        CopyToDir("TestData/SomePrefab.prefab.meta", assetsDir);

        return tempDir;
    }

    private static void CopyToDir(string sourceRelative, string destDir)
    {
        var source = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            sourceRelative);
        var dest = Path.Combine(destDir, Path.GetFileName(sourceRelative));
        File.Copy(source, dest);
    }

    /// <summary>
    /// Helper to create and clean up temporary directories for testing.
    /// </summary>
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
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}