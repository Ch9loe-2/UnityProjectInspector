using System.Text.Json;
using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Loads an AssignmentDefinition from a JSON file.
///
/// CLI-side utility only — does not modify Core's AssignmentDefinition model.
/// Uses System.Text.Json directly (Core model has [JsonPropertyName] annotations).
/// </summary>
public static class AssignmentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads and validates an AssignmentDefinition from a JSON file path.
    /// </summary>
    /// <param name="path">Absolute or relative path to the JSON file.</param>
    /// <returns>The deserialized AssignmentDefinition.</returns>
    /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
    /// <exception cref="JsonException">If the JSON is invalid.</exception>
    /// <exception cref="InvalidOperationException">If deserialization returns null.</exception>
    public static AssignmentDefinition Load(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Assignment file not found: {fullPath}", fullPath);
        }

        var json = File.ReadAllText(fullPath);

        var assignment = JsonSerializer.Deserialize<AssignmentDefinition>(json, JsonOptions);

        if (assignment == null)
        {
            throw new InvalidOperationException(
                $"Failed to parse assignment from: {fullPath}");
        }

        return assignment;
    }
}