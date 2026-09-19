using UnityProjectInspector.Core.Parsing;

namespace UnityProjectInspector.Tests;

public class UnityProjectScannerTests
{
    private readonly UnityProjectScanner _scanner = new();

    [Fact]
    public void IsValidUnityProject_EmptyDirectory_ReturnsFalse()
    {
        // Arrange
        using var tempDir = new TempDirectory();

        // Act
        var result = UnityProjectScanner.IsValidUnityProject(tempDir.Path);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidUnityProject_CompleteProject_ReturnsTrue()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Packages"));

        // Act
        var result = UnityProjectScanner.IsValidUnityProject(tempDir.Path);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidUnityProject_MissingAssets_ReturnsFalse()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Packages"));

        // Act
        var result = UnityProjectScanner.IsValidUnityProject(tempDir.Path);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidUnityProject_MissingProjectSettings_ReturnsFalse()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Packages"));

        // Act
        var result = UnityProjectScanner.IsValidUnityProject(tempDir.Path);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidUnityProject_MissingPackages_ReturnsFalse()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "ProjectSettings"));

        // Act
        var result = UnityProjectScanner.IsValidUnityProject(tempDir.Path);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Scan_NonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => _scanner.Scan(nonExistentDir));
    }

    [Fact]
    public void Scan_ValidProjectWithScene_ParsesScene()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Packages"));

        // Copy the test scene into Assets
        var srcScene = Path.Combine("TestData", "SimpleScene.unity");
        var dstScene = Path.Combine(tempDir.Path, "Assets", "SampleScene.unity");
        File.Copy(srcScene, dstScene);

        // Act
        var project = _scanner.Scan(tempDir.Path);

        // Assert
        Assert.True(project.IsValid);
        Assert.Single(project.Scenes);
        Assert.Equal("SampleScene", project.Scenes[0].Name);
        Assert.NotEmpty(project.Scenes[0].GameObjects);
        Assert.Contains(project.Scenes[0].GameObjects, g => g.Name == "Main Camera");
    }

    [Fact]
    public void Scan_ValidProjectWithoutScenes_ReturnsEmptyScenes()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Packages"));

        // Act
        var project = _scanner.Scan(tempDir.Path);

        // Assert
        Assert.True(project.IsValid);
        Assert.Empty(project.Scenes);
    }

    [Fact]
    public void Scan_InvalidProject_ReturnsNotValidWithNoScenes()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(tempDir.Path);

        // Act
        var project = _scanner.Scan(tempDir.Path);

        // Assert
        Assert.False(project.IsValid);
        Assert.Empty(project.Scenes);
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