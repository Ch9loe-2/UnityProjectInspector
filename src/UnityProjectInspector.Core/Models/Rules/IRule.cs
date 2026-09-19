namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// Contract for a single inspection rule.
/// Each rule evaluates a specific aspect of a Unity project
/// and returns a single RuleResult.
///
/// Rules must be stateless — they receive all data through InspectionContext
/// and must not instantiate parsers or analyzers.
/// </summary>
public interface IRule
{
    /// <summary>
    /// Unique identifier for this rule (e.g. "SCENE_EXISTS").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable rule name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluates this rule against the given inspection context.
    /// </summary>
    RuleResult Evaluate(InspectionContext context);
}