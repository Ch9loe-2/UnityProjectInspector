using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// Describes a single inspection rule in a portable, JSON-serializable format.
///
/// This is the configuration-side counterpart of IRule.
/// RuleDefinition → RuleFactory → IRule → RuleEngine → RuleResult.
///
/// Different rule types use different fields:
///   SceneExists:        Target → scene name
///   GameObjectExists:   Target → GameObject name
///   ComponentExists:    Target → GameObject name, ExpectedClass → component type
///   UnityEventBinding:  Target → source GameObject, ExpectedClass → target class, ExpectedMethod → method name
///   CodeEvidence:       Target → source GameObject, ExpectedMethod → method name
///
/// All fields beyond Id/Name/Type are nullable — not all rules need them.
/// </summary>
public class RuleDefinition
{
    /// <summary>
    /// Unique identifier for this rule (e.g. "scene.sample.exists").
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable rule name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Rule type discriminator (e.g. "SceneExists", "GameObjectExists").
    /// Must match one of the supported types in RuleFactory.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Primary target name. Interpretation depends on rule type:
    ///   SceneExists     → scene name
    ///   GameObjectExists  → GameObject name
    ///   ComponentExists   → GameObject name
    ///   UnityEventBinding → source GameObject name
    ///   CodeEvidence      → source GameObject name
    /// </summary>
    [JsonPropertyName("target")]
    public string? Target { get; init; }

    /// <summary>
    /// Expected class/component type name.
    ///   ComponentExists   → component type (e.g. "Canvas")
    ///   UnityEventBinding → target script class (e.g. "PanelSwitcher")
    /// </summary>
    [JsonPropertyName("expectedClass")]
    public string? ExpectedClass { get; init; }

    /// <summary>
    /// Expected method name for UnityEvent/CodeEvidence rules.
    /// </summary>
    [JsonPropertyName("expectedMethod")]
    public string? ExpectedMethod { get; init; }

    /// <summary>
    /// Severity level ("Info", "Warning", "Error").
    /// Defaults to "Info" when null.
    /// </summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; init; }

    /// <summary>
    /// Optional custom message override.
    /// When provided, this message is passed to the rule constructor
    /// or used as the result message template.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}