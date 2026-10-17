using System.Text;
using System.Text.Json;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Auto-generates a static-only assignment JSON for a Unity project:
/// project-structure checks (ProjectVersion.txt, manifest.json) + one
/// SceneExists/FileExists pair per scene found under Assets/.
///
/// This replaces the former python gen-assignment step so the one-click
/// tools no longer depend on python3 on any platform.
/// </summary>
public static class AssignmentGenerator
{
    /// <summary>
    /// Scans <paramref name="projectRoot"/> for .unity scenes and writes an
    /// assignment JSON to <paramref name="outPath"/>. Returns the scene count.
    /// </summary>
    public static int Generate(string projectRoot, string outPath)
    {
        var scenes = FindScenes(projectRoot);
        var structureRules = new[]
        {
            new { id = "file.projectversion", name = "ProjectSettings/ProjectVersion.txt 存在", type = "FileExists", target = "ProjectSettings/ProjectVersion.txt", severity = "Error" },
            new { id = "file.manifest", name = "Packages/manifest.json 存在", type = "FileExists", target = "Packages/manifest.json", severity = "Error" },
        };

        var requirements = new List<object>();
        requirements.Add(new
        {
            id = "project-structure",
            name = "Unity 工程结构完整",
            evidenceRequirement = "StaticOnly",
            staticRules = structureRules,
        });

        if (scenes.Count > 0)
        {
            var sceneRules = new List<object>();
            foreach (var (name, rel) in scenes)
            {
                sceneRules.Add(new { id = $"scene.{name}.exists", name = $"场景「{name}」存在", type = "SceneExists", target = name, severity = "Error" });
                sceneRules.Add(new { id = $"file.{name}.exists", name = $"场景文件 {rel} 存在", type = "FileExists", target = rel, severity = "Error" });
            }
            requirements.Add(new
            {
                id = "scenes",
                name = $"场景文件（共 {scenes.Count} 个）",
                evidenceRequirement = "StaticOnly",
                staticRules = sceneRules,
            });
        }

        var assignment = new
        {
            id = "auto-check",
            name = "自动生成的一键检查方案",
            description = "由一键检查工具自动生成：校验工程结构与场景完整性",
            requirements,
        };

        var json = JsonSerializer.Serialize(assignment, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        var parentDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }
        File.WriteAllText(outPath, json, new UTF8Encoding(false));
        return scenes.Count;
    }

    private static List<(string name, string rel)> FindScenes(string projectRoot)
    {
        var scenes = new List<(string, string)>();
        var assetsDir = Path.Combine(projectRoot, "Assets");
        if (!Directory.Exists(assetsDir))
        {
            return scenes;
        }

        foreach (var file in Directory.EnumerateFiles(assetsDir, "*.unity", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(projectRoot, file);
            var name = Path.GetFileNameWithoutExtension(file);
            scenes.Add((name, rel));
        }

        return scenes.OrderBy(s => s.Item1, StringComparer.Ordinal).ToList();
    }
}
