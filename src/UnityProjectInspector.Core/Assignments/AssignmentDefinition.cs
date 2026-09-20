using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// Defines a Unity project inspection assignment, typically created by a teacher
/// to verify that a student's Unity project meets specific requirements.
///
/// An assignment consists of multiple RequirementDefinitions, each of which
/// defines static rules and/or a runtime test script.
///
/// JSON-serializable. All fields use camelCase JSON names.
/// </summary>
public class AssignmentDefinition
{
    /// <summary>
    /// Unique identifier for this assignment (e.g. "maze-2d-basic").
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable assignment name (e.g. "2D迷宫基础作业").
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Optional description explaining the assignment goal.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Ordered list of requirements that this assignment must satisfy.
    /// Validated at workflow load time for duplicate IDs and missing fields.
    /// </summary>
    [JsonPropertyName("requirements")]
    public List<RequirementDefinition> Requirements { get; init; } = new();
}