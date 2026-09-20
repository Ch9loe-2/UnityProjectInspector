namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// Describes what kind of evidence is required to satisfy an inspection rule.
///
/// StaticOnly: Static analysis (scene YAML parsing, C# AST) is sufficient.
///   Examples: scene file exists, GameObject exists, script is attached,
///   UnityEvent binding exists, hierarchy is correct.
///
/// RuntimeRequired: Static analysis can only show intent, not actual behavior.
///   Actual runtime verification (Player execution) is required.
///   Examples: clicking a button actually navigates to the correct scene,
///   no runtime exceptions occur during the flow.
/// </summary>
public enum EvidenceRequirement
{
    /// <summary>
    /// Static analysis evidence is sufficient to pass this rule.
    /// No runtime verification needed.
    /// </summary>
    StaticOnly,

    /// <summary>
    /// Runtime verification (actual standalone Player execution) is required.
    /// Static analysis alone cannot satisfy this rule.
    /// </summary>
    RuntimeRequired,
}