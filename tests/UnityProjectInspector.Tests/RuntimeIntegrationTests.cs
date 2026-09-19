using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Integration tests for Milestone 13 RuntimeRunner that validate the
/// full end-to-end pipeline against the M12_MinimalUnityProject fixture.
///
/// These tests execute:
///   Real Unity Editor → Real Player Build → Real Standalone Player → Real Runtime Evidence
///
/// Requires: Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
/// and M12_MinimalUnityProject in the repo's experiments/ directory.
///
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[CollectionDefinition("RuntimeIntegration", DisableParallelization = true)]
public class RuntimeIntegrationCollection { }

/// <summary>
/// Integration tests for Milestone 13 RuntimeRunner that validate the
/// full end-to-end pipeline against the M12_MinimalUnityProject fixture.
///
/// These tests execute:
///   Real Unity Editor → Real Player Build → Real Standalone Player → Real Runtime Evidence
///
/// Requires: Unity Editor at /Applications/Unity/Hub/Editor/2022.3.62f3c1/
/// and M12_MinimalUnityProject in the repo's experiments/ directory.
///
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Collection("RuntimeIntegration")]
[Trait("Category", "Integration")]
public class RuntimeIntegrationTests
{
    private const string UnityExecutable =
        "/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity";

    private static readonly string? ProjectPath;

    static RuntimeIntegrationTests()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..", ".."));

        var candidate = Path.Combine(
            repoRoot, "experiments", "M12_MinimalUnityProject");

        // Debug: log the resolved paths
        System.Diagnostics.Debug.WriteLine(
            $"[RuntimeIntegrationTests] baseDir={baseDir}, repoRoot={repoRoot}");

        ProjectPath = Directory.Exists(candidate) ? candidate : null;

        // Test if the issue is wrong project path resolution
        if (ProjectPath == null)
            System.Diagnostics.Debug.WriteLine(
                $"[RuntimeIntegrationTests] M12 project NOT FOUND at {candidate}");
    }

    [Fact]
    public async Task RunAsync_M12Project_ReturnsPassed()
    {
        AssertPrerequisites();

        // Force fresh build — clean any cached Build directory
        var buildDir = Path.Combine(ProjectPath!, "Build");
        if (Directory.Exists(buildDir))
            Directory.Delete(buildDir, recursive: true);

        var options = new RuntimeRunOptions
        {
            UnityExecutable = UnityExecutable,
            ProjectPath = ProjectPath!,
            ProductName = "M12MinimalPlayer",
            BuildMethod = "M12_PlayerBuild.Build",
            ResultDirEnvVar = "M12_RESULT_DIR",
            EvidenceFileName = "runtime_evidence.json",
            MarkerFileName = "m12_done.marker",
            BuildTimeoutSeconds = 180,
            PlayerTimeoutSeconds = 60,
        };

        var runner = new RuntimeRunner();
        var session = await runner.RunAsync(options);

        Assert.Equal(RuntimeResultStatus.Passed, session.Result);
        var evidence = Assert.Single(session.Evidence);
        Assert.True(evidence.Obtained);
        Assert.Equal("Runtime", evidence.Observed);
        Assert.Contains("Runtime inspection passed", session.Message);
    }

    private static void AssertPrerequisites()
    {
        Assert.True(File.Exists(UnityExecutable),
            $"Unity Editor not found at {UnityExecutable}. " +
            $"This integration test requires Unity 2022.3.62f3c1 installed.");

        Assert.NotNull(ProjectPath);
        Assert.True(Directory.Exists(ProjectPath),
            $"M12 project not found. This test requires experiments/M12_MinimalUnityProject.");

        Assert.True(File.Exists(Path.Combine(ProjectPath!, "Assets", "Scenes", "MainScene.unity")),
            "M12 project missing MainScene.unity — run Unity import first.");
    }
}