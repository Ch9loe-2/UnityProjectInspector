using System.Text.Json;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Loads a list of RuleDefinition objects from a JSON file.
///
/// This loader ONLY handles JSON deserialization and basic validation.
/// It does NOT:
///   - Access Unity projects
///   - Parse YAML
///   - Parse C#
///   - Execute rules
///
/// Error handling:
///   File not found  → FileNotFoundException
///   Invalid JSON    → JsonException (wraps the original)
///   Empty array     → returns empty list (valid, not an error)
///   Unknown fields  → silently ignored (lenient deserialization)
/// </summary>
public static class RuleDefinitionLoader
{
    /// <summary>
    /// Loads rule definitions from a JSON file.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the JSON file.</param>
    /// <returns>A list of parsed RuleDefinition objects. May be empty.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="JsonException">The file content is not valid JSON or does not match the expected schema.</exception>
    public static List<RuleDefinition> Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Rule definition file not found: {filePath}", filePath);
        }

        var json = File.ReadAllText(filePath);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("Rule definition file is empty.");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
        };

        List<RuleDefinition>? definitions;

        try
        {
            definitions = JsonSerializer.Deserialize<List<RuleDefinition>>(json, options);
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Failed to parse rule definition file '{filePath}': {ex.Message}", ex);
        }

        // Check for null (JSON "null" literal) vs empty array (valid)
        if (definitions == null)
        {
            throw new JsonException($"Rule definition file '{filePath}' contains null instead of a JSON array.");
        }

        return definitions;
    }
}