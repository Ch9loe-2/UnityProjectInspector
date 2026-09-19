using System.Text.RegularExpressions;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Core.Parsing;

/// <summary>
/// Parses UnityEvent persistent call bindings from Unity .unity / .prefab YAML files.
///
/// Scans the YAML for m_PersistentCalls sections inside component blocks,
/// and extracts the binding info: target component, method name, arguments.
///
/// This is NOT limited to Button — it works for any UnityEvent (Button, Toggle, Slider, Dropdown,
/// custom UnityEvent, etc.) as long as the YAML has m_PersistentCalls structure.
/// </summary>
public class UnityEventParser
{
    private static readonly Regex BlockHeaderRegex = new(
        @"^---\s*!u!\d+\s+&(\d+)", RegexOptions.Compiled);

    private static readonly Regex PersistentCallsStartRegex = new(
        @"m_PersistentCalls:", RegexOptions.Compiled);

    // Regex to match a call entry start line like "      - m_Target: {fileID: 200}"
    private static readonly Regex CallEntryTargetRegex = new(
        @"^\s*-\s*m_Target:\s*\{fileID:\s*(\d+)\}", RegexOptions.Compiled);

    // Regex to match field lines within a call entry
    private static readonly Regex CallFieldRegex = new(
        @"^\s+m_(MethodName|TargetAssemblyTypeName|Mode|CallState):\s*(.*)", RegexOptions.Compiled);

    // Regex to match m_StringArgument in m_Arguments sub-block
    private static readonly Regex StringArgumentRegex = new(
        @"^\s+m_StringArgument:\s*(.*)", RegexOptions.Compiled);

    /// <summary>
    /// Parses UnityEvent persistent call bindings from raw scene text.
    /// </summary>
    /// <param name="sceneText">Raw content of a .unity file.</param>
    /// <param name="sceneInfo">Pre-parsed SceneInfo for cross-referencing FileIds.</param>
    /// <returns>List of parsed UnityEvent bindings.</returns>
    public List<UnityEventBindingInfo> Parse(string sceneText, SceneInfo sceneInfo)
    {
        var bindings = new List<UnityEventBindingInfo>();

        if (string.IsNullOrWhiteSpace(sceneText))
        {
            return bindings;
        }

        // Build reverse map: Component FileId → GameObject FileId from SceneInfo
        var componentToGameObject = BuildComponentToGameObjectMap(sceneInfo);

        var lines = sceneText.Split('\n');

        // Parse all blocks (lightweight — just fileId and raw text after the header)
        var blocks = ParseBlocks(lines);

        // For each block, check if it contains m_PersistentCalls
        foreach (var block in blocks)
        {
            if (!PersistentCallsStartRegex.IsMatch(block.RawPropertiesJoined))
            {
                continue;
            }

            // This block has UnityEvent bindings.
            // Find source GameObject. The block is a component; look up its GameObject.
            var sourceGameObjectFileId = componentToGameObject.TryGetValue(block.FileId, out var goId)
                ? goId
                : 0L;

            // Parse individual call entries
            var calls = ParseCallEntries(block.RawPropertiesJoined);

            foreach (var call in calls)
            {
                var binding = new UnityEventBindingInfo
                {
                    SourceGameObjectFileId = sourceGameObjectFileId,
                    SourceComponentFileId = block.FileId,
                    TargetFileId = call.TargetFileId,
                    MethodName = call.MethodName,
                    TargetAssemblyTypeName = call.TargetAssemblyTypeName,
                    Mode = call.Mode,
                    CallState = call.CallState,
                    ArgumentValues = call.ArgumentValues,
                };

                // Try to resolve target component → target GameObject
                if (componentToGameObject.TryGetValue(call.TargetFileId, out var targetGoId))
                {
                    binding.TargetGameObjectFileId = targetGoId;
                    binding.IsResolved = true;
                }

                bindings.Add(binding);
            }
        }

        return bindings;
    }

    /// <summary>
    /// Builds a reverse lookup: Component FileId → GameObject FileId from SceneInfo.
    /// </summary>
    private static Dictionary<long, long> BuildComponentToGameObjectMap(SceneInfo sceneInfo)
    {
        var map = new Dictionary<long, long>();

        foreach (var go in sceneInfo.GameObjects)
        {
            foreach (var comp in go.Components)
            {
                map[comp.FileId] = go.FileId;
            }
        }

        return map;
    }

    /// <summary>
    /// Lightweight block parser — splits scene text by "--- !u!N &fileId" headers.
    /// Returns only FileId and raw properties text for each block.
    /// </summary>
    private static List<RawBlock> ParseBlocks(string[] lines)
    {
        var blocks = new List<RawBlock>();
        RawBlock? currentBlock = null;
        var propertyLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var headerMatch = BlockHeaderRegex.Match(line);

            if (headerMatch.Success)
            {
                if (currentBlock != null)
                {
                    currentBlock.RawPropertiesJoined = string.Join("\n", propertyLines);
                    blocks.Add(currentBlock);
                }

                propertyLines.Clear();
                currentBlock = new RawBlock
                {
                    FileId = long.Parse(headerMatch.Groups[1].Value),
                };
                continue;
            }

            if (currentBlock == null) continue;

            // Skip the type name line (first line after header, has colon)
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

            propertyLines.Add(line);
        }

        if (currentBlock != null)
        {
            currentBlock.RawPropertiesJoined = string.Join("\n", propertyLines);
            blocks.Add(currentBlock);
        }

        return blocks;
    }

    /// <summary>
    /// Parses individual call entries from within a block that has m_PersistentCalls.
    /// </summary>
    private static List<ParsedCallEntry> ParseCallEntries(string rawProperties)
    {
        var entries = new List<ParsedCallEntry>();
        var lines = rawProperties.Split('\n');

        // Find the m_Calls: line
        int callsStartIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("m_Calls:"))
            {
                callsStartIndex = i;
                break;
            }
        }

        if (callsStartIndex < 0 || callsStartIndex >= lines.Length - 1)
        {
            return entries;
        }

        // Now scan from callsStartIndex + 1 for call entries
        ParsedCallEntry? currentEntry = null;

        for (int i = callsStartIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];

            // Stop if we hit a line that's dedented relative to m_Calls
            var indent = GetIndent(line);

            // Detect the end: a non-empty line with less indent than m_Calls?
            // m_Calls is typically at indent X, entries at indent X+2, fields at X+4
            // We need to detect when we exit the persistent calls section
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Check if this is a new call entry (starts with "- m_Target:")
            var targetMatch = CallEntryTargetRegex.Match(line);
            if (targetMatch.Success)
            {
                // Save previous entry
                if (currentEntry != null)
                {
                    entries.Add(currentEntry);
                }

                var targetFileId = long.Parse(targetMatch.Groups[1].Value);
                currentEntry = new ParsedCallEntry
                {
                    TargetFileId = targetFileId,
                };
                continue;
            }

            // If we don't have a current entry yet, skip
            if (currentEntry == null) continue;

            // Check for field lines within the current entry
            var fieldMatch = CallFieldRegex.Match(line);
            if (fieldMatch.Success)
            {
                switch (fieldMatch.Groups[1].Value)
                {
                    case "MethodName":
                        currentEntry.MethodName = fieldMatch.Groups[2].Value.Trim();
                        break;
                    case "TargetAssemblyTypeName":
                        currentEntry.TargetAssemblyTypeName = fieldMatch.Groups[2].Value.Trim();
                        break;
                    case "Mode":
                        int.TryParse(fieldMatch.Groups[2].Value.Trim(), out var mode);
                        currentEntry.Mode = mode;
                        break;
                    case "CallState":
                        int.TryParse(fieldMatch.Groups[2].Value.Trim(), out var callState);
                        currentEntry.CallState = callState;
                        break;
                }
                continue;
            }

            // Check for arguments section
            if (line.TrimStart().StartsWith("m_Arguments:"))
            {
                // Parse argument fields in the following lines
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var argLine = lines[j];
                    var argIndent = GetIndent(argLine);

                    // Stop if we've reached a dedent or new "- " entry
                    if (argLine.TrimStart().StartsWith('-'))
                    {
                        break;
                    }

                    // Check for string argument
                    var stringArgMatch = StringArgumentRegex.Match(argLine);
                    if (stringArgMatch.Success)
                    {
                        var argValue = stringArgMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(argValue))
                        {
                            currentEntry.ArgumentValues.Add(argValue);
                        }
                        break; // Only one m_StringArgument per m_Arguments block typically
                    }
                }
                // Don't advance i here since we already consumed the args lines
                continue;
            }
        }

        // Add the last entry
        if (currentEntry != null)
        {
            entries.Add(currentEntry);
        }

        return entries;
    }

    private static int GetIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }

    /// <summary>
    /// Lightweight internal block representation.
    /// </summary>
    private class RawBlock
    {
        public long FileId { get; init; }
        public string? TypeName { get; set; }
        public string RawPropertiesJoined { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parsed data for a single m_Calls entry.
    /// </summary>
    private class ParsedCallEntry
    {
        public long TargetFileId { get; init; }
        public string MethodName { get; set; } = string.Empty;
        public string? TargetAssemblyTypeName { get; set; }
        public int Mode { get; set; }
        public int CallState { get; set; }
        public List<string> ArgumentValues { get; init; } = new();
    }
}