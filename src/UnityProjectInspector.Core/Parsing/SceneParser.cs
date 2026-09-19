using System.Text;
using System.Text.RegularExpressions;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Core.Parsing;

/// <summary>
/// Parses Unity .unity scene files (YAML-based format) into structured models.
/// 
/// This parser reads the simplified YAML format used by Unity and extracts:
/// - GameObjects (name, fileId)
/// - Components (type, fileId, basic properties)
/// - GameObject-to-Component relationships
/// - Parent-child hierarchy (via Transform.m_Father)
///
/// It does NOT parse every Unity YAML feature. Unknown blocks are silently skipped.
/// </summary>
public partial class SceneParser
{
    // Regex: match "--- !u!<classId> &<fileId>"
    [GeneratedRegex(@"^---\s*!u!\d+\s+&(\d+)", RegexOptions.Compiled)]
    private static partial Regex BlockHeaderRegex();

    // Regex: match a property line like "  m_Name: Player"
    [GeneratedRegex(@"^\s{2}(\S[\w\.]*)\s*:\s*(.*)", RegexOptions.Compiled)]
    private static partial Regex PropertyLineRegex();

    // Regex: match "- component: {fileID: <id>}"
    [GeneratedRegex(@"-\s*component:\s*\{fileID:\s*(\d+)\}", RegexOptions.Compiled)]
    private static partial Regex ComponentRefRegex();

    // Regex: match "m_Father: {fileID: <id>}"
    [GeneratedRegex(@"m_Father:\s*\{fileID:\s*(\d+)\}", RegexOptions.Compiled)]
    private static partial Regex FatherRefRegex();

    // Known Unity component type names
    private static readonly HashSet<string> KnownComponentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transform", "RectTransform",
        "Camera", "Light",
        "MeshRenderer", "SkinnedMeshRenderer",
        "Rigidbody",
        "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider",
        "Canvas", "Button",
        "Animator",
        "AudioSource",
        "TextMeshProUGUI",
    };

    /// <summary>
    /// Parses a .unity scene file from disk.
    /// </summary>
    /// <param name="filePath">Path to the .unity file.</param>
    /// <returns>Parsed SceneInfo.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public SceneInfo Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Scene file not found: {filePath}", filePath);
        }

        var text = File.ReadAllText(filePath);
        return ParseText(text, filePath);
    }

    /// <summary>
    /// Parses scene text content. Extracted for testability without file I/O.
    /// </summary>
    public SceneInfo ParseText(string text, string filePath)
    {
        var sceneInfo = new SceneInfo
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return sceneInfo;
        }

        var lines = text.Split('\n');

        // Step 1: Parse all blocks into raw data
        var rawBlocks = ParseBlocks(lines);

        // Step 2: Categorize blocks into GameObjects and components
        var gameObjectBlocks = new List<ParsedBlock>();
        var componentBlocks = new Dictionary<long, ParsedBlock>();

        foreach (var block in rawBlocks)
        {
            if (block.TypeName == "GameObject")
            {
                gameObjectBlocks.Add(block);
            }
            else if (block.TypeName != null && KnownComponentTypes.Contains(block.TypeName))
            {
                componentBlocks[block.FileId] = block;
            }
        }

        // Step 3: Build Transform fileId -> GameObject fileId mapping
        var transformOfGameObject = new Dictionary<long, long>(); // Transform fileId -> GameObject fileId

        foreach (var goBlock in gameObjectBlocks)
        {
            var componentRefs = ParseComponentReferences(goBlock.RawPropertiesJoined);
            foreach (var compFileId in componentRefs)
            {
                if (componentBlocks.TryGetValue(compFileId, out var compBlock) &&
                    compBlock.TypeName is "Transform" or "RectTransform")
                {
                    transformOfGameObject[compFileId] = goBlock.FileId;
                    break;
                }
            }
        }

        // Step 4: Resolve parent relationships
        // Transform.m_Father stores the parent Transform's fileId.
        // Map it back to the parent GameObject's fileId via transformOfGameObject.
        var parentTransformMap = new Dictionary<long, long>(); // Transform fileId -> father Transform fileId

        foreach (var (tfFileId, tfBlock) in componentBlocks
                     .Where(kvp => kvp.Value.TypeName is "Transform" or "RectTransform"))
        {
            var fatherMatch = FatherRefRegex().Match(tfBlock.RawPropertiesJoined);
            if (fatherMatch.Success)
            {
                var fatherTfFileId = long.Parse(fatherMatch.Groups[1].Value);
                if (fatherTfFileId != 0)
                {
                    parentTransformMap[tfFileId] = fatherTfFileId;
                }
            }
        }

        // Step 5: Build the final model
        foreach (var goBlock in gameObjectBlocks)
        {
            var goName = ParseGameObjectName(goBlock.RawPropertiesJoined);

            // Resolve parent GameObject's fileId:
            //   1. Find this GO's Transform fileId
            //   2. Look up that Transform's father Transform fileId
            //   3. Map father Transform fileId back to its GameObject fileId
            long parentFileId = 0;
            var ourTfFileIds = transformOfGameObject
                .Where(kvp => kvp.Value == goBlock.FileId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var ourTfFileId in ourTfFileIds)
            {
                if (parentTransformMap.TryGetValue(ourTfFileId, out var fatherTfFileId) &&
                    transformOfGameObject.TryGetValue(fatherTfFileId, out var parentGoFileId))
                {
                    parentFileId = parentGoFileId;
                    break;
                }
            }

            var goInfo = new GameObjectInfo
            {
                Name = goName,
                FileId = goBlock.FileId,
                ParentFileId = parentFileId,
            };

            // Add linked components
            var componentRefs = ParseComponentReferences(goBlock.RawPropertiesJoined);
            foreach (var compFileId in componentRefs)
            {
                if (componentBlocks.TryGetValue(compFileId, out var compBlock))
                {
                    goInfo.Components.Add(new ComponentInfo
                    {
                        Type = compBlock.TypeName ?? "UnknownComponent",
                        FileId = compBlock.FileId,
                    });
                }
            }

            sceneInfo.GameObjects.Add(goInfo);
        }

        return sceneInfo;
    }

    /// <summary>
    /// Parses lines into blocks based on YAML "---" separators.
    /// Each block has a fileId, type name, and raw properties text.
    /// </summary>
    private static List<ParsedBlock> ParseBlocks(string[] lines)
    {
        var blocks = new List<ParsedBlock>();
        ParsedBlock? currentBlock = null;
        var propertyLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            var headerMatch = BlockHeaderRegex().Match(line);
            if (headerMatch.Success)
            {
                // Finalize previous block
                if (currentBlock != null)
                {
                    currentBlock.RawPropertiesJoined = string.Join("\n", propertyLines);
                    blocks.Add(currentBlock);
                }

                propertyLines.Clear();
                currentBlock = new ParsedBlock
                {
                    FileId = long.Parse(headerMatch.Groups[1].Value),
                };
                continue;
            }

            if (currentBlock == null)
            {
                continue;
            }

            // After the header, the first non-empty non-comment line is the type name
            if (currentBlock.TypeName == null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith('%') && !trimmed.StartsWith("---"))
                {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        currentBlock.TypeName = trimmed[..colonIndex].Trim();
                    }
                }
                continue;
            }

            // Accumulate property lines once type name is known
            propertyLines.Add(line);
        }

        // Add the last block
        if (currentBlock != null)
        {
            currentBlock.RawPropertiesJoined = string.Join("\n", propertyLines);
            blocks.Add(currentBlock);
        }

        return blocks;
    }

    private static string ParseGameObjectName(string rawProperties)
    {
        foreach (var line in rawProperties.Split('\n'))
        {
            var match = PropertyLineRegex().Match(line);
            if (match.Success && match.Groups[1].Value == "m_Name")
            {
                var value = match.Groups[2].Value;
                // Remove wrapping quotes if present
                if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                {
                    return value[1..^1];
                }
                return value;
            }
        }

        return "Unnamed GameObject";
    }

    private static List<long> ParseComponentReferences(string rawProperties)
    {
        var refs = new List<long>();

        foreach (Match match in ComponentRefRegex().Matches(rawProperties))
        {
            refs.Add(long.Parse(match.Groups[1].Value));
        }

        return refs;
    }

    /// <summary>
    /// Internal container for a parsed YAML block from a .unity file.
    /// </summary>
    private class ParsedBlock
    {
        public long FileId { get; init; }
        public string? TypeName { get; set; }
        public string RawPropertiesJoined { get; set; } = string.Empty;
    }
}