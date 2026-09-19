using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Core.Parsing;

/// <summary>
/// Scans a directory to determine if it is a valid Unity project,
/// and discovers .unity scene files.
/// </summary>
public class UnityProjectScanner
{
    private readonly SceneParser _sceneParser;

    public UnityProjectScanner()
    {
        _sceneParser = new SceneParser();
    }

    /// <summary>
    /// Scans the given directory and returns a UnityProjectInfo.
    /// </summary>
    /// <param name="projectPath">Absolute or relative path to the Unity project directory.</param>
    /// <returns>A populated UnityProjectInfo.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the path does not exist.</exception>
    public UnityProjectInfo Scan(string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory not found: {projectPath}");
        }

        var projectDir = Path.GetFullPath(projectPath);

        var isValid = IsValidUnityProject(projectDir);

        var result = new UnityProjectInfo
        {
            RootPath = projectDir,
            IsValid = isValid,
        };

        if (!isValid)
        {
            return result;
        }

        // Find all .unity files under Assets/
        var assetsDir = Path.Combine(projectDir, "Assets");
        if (Directory.Exists(assetsDir))
        {
            var sceneFiles = Directory.GetFiles(assetsDir, "*.unity", SearchOption.AllDirectories);

            foreach (var sceneFile in sceneFiles)
            {
                var sceneInfo = _sceneParser.Parse(sceneFile);
                result.Scenes.Add(sceneInfo);
            }
        }

        return result;
    }

    /// <summary>
    /// Checks whether the given directory contains the expected Unity project subdirectories.
    /// </summary>
    public static bool IsValidUnityProject(string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            return false;
        }

        var hasAssets = Directory.Exists(Path.Combine(projectPath, "Assets"));
        var hasProjectSettings = Directory.Exists(Path.Combine(projectPath, "ProjectSettings"));
        var hasPackages = Directory.Exists(Path.Combine(projectPath, "Packages"));

        return hasAssets && hasProjectSettings && hasPackages;
    }
}