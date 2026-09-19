using System.Text.RegularExpressions;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Core.Parsing;

/// <summary>
/// Resolves Unity script GUID references to actual .cs source files
/// by scanning Assets/**/*.meta files.
///
/// Usage:
///   var resolver = new ScriptResolver(projectRootPath);
///   var scriptInfo = resolver.ResolveScript(guid);
/// </summary>
public partial class ScriptResolver
{
    private readonly Dictionary<string, ScriptInfo> _guidIndex = new();
    private readonly string _assetsPath;
    private bool _initialized;

    /// <summary>
    /// Creates a ScriptResolver for the given Unity project root.
    /// The index is built lazily on first resolve call, or you can call Initialize() explicitly.
    /// </summary>
    /// <param name="projectPath">Absolute or relative path to the Unity project root (containing Assets/).</param>
    /// <exception cref="DirectoryNotFoundException">Thrown when the project path does not exist.</exception>
    public ScriptResolver(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        _assetsPath = Path.Combine(fullPath, "Assets");
        _initialized = false;
    }

    /// <summary>
    /// Regex to extract the GUID from a Unity .meta file.
    /// Matches a line like "guid: abcdef1234567890abcdef1234567890"
    /// </summary>
    [GeneratedRegex(@"^guid:\s*([0-9a-fA-F]+)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex GuidLineRegex();

    /// <summary>
    /// Explicitly scans all .meta files under Assets/ to build the GUID index.
    /// Called automatically on first ResolveScript call if not already initialized.
    /// </summary>
    public void Initialize()
    {
        _guidIndex.Clear();

        if (!Directory.Exists(_assetsPath))
        {
            _initialized = true;
            return;
        }

        var metaFiles = Directory.GetFiles(_assetsPath, "*.meta", SearchOption.AllDirectories);

        foreach (var metaPath in metaFiles)
        {
            // Determine the corresponding asset path (strip .meta extension)
            // e.g. "Assets/Scripts/GameManager.cs.meta" → "Assets/Scripts/GameManager.cs"
            var assetPath = metaPath[..^5]; // remove ".meta"

            var guid = ExtractGuidFromMetaFile(metaPath);
            if (guid == null)
            {
                continue; // malformed .meta file, skip
            }

            var isCsScript = assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            var csFileExists = isCsScript && File.Exists(assetPath);

            _guidIndex[guid] = new ScriptInfo
            {
                Guid = guid,
                FilePath = metaPath,
                ScriptPath = csFileExists ? assetPath : null,
            };
        }

        _initialized = true;
    }

    /// <summary>
    /// Resolves a script GUID to its ScriptInfo.
    /// Returns null if the GUID is not found, or if the corresponding file is not a C# script.
    /// </summary>
    public ScriptInfo? ResolveScript(string guid)
    {
        if (!_initialized)
        {
            Initialize();
        }

        if (_guidIndex.TryGetValue(guid, out var info))
        {
            // Only return resolved script info if it's an actual .cs file
            if (info.IsResolved)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves any GUID to its raw ScriptInfo (including non-script assets, unresolved files, etc.).
    /// Returns null only if the GUID is not found in any .meta file.
    /// Use this when you need to distinguish "GUID not found" from "GUID found but not a C# script".
    /// </summary>
    public ScriptInfo? ResolveRaw(string guid)
    {
        if (!_initialized)
        {
            Initialize();
        }

        _guidIndex.TryGetValue(guid, out var info);
        return info;
    }

    /// <summary>
    /// Returns all resolved C# scripts in the project.
    /// </summary>
    public IReadOnlyCollection<ScriptInfo> GetAllScripts()
    {
        if (!_initialized)
        {
            Initialize();
        }

        return _guidIndex.Values
            .Where(s => s.IsResolved)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Reads a .meta file and extracts the GUID.
    /// Returns null if the file cannot be read or no GUID is found.
    /// </summary>
    private static string? ExtractGuidFromMetaFile(string metaPath)
    {
        try
        {
            // Read only the first few lines — GUID is almost always within the first 10 lines
            using var reader = new StreamReader(metaPath);
            for (var i = 0; i < 20; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;

                var match = GuidLineRegex().Match(line);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch
        {
            // If a .meta file can't be read (permissions, locked, etc.), skip it
        }

        return null;
    }
}