using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Lightweight, CLI-side structural validation performed BEFORE invoking the Core
/// inspection pipeline.
///
/// This is format/shape validation only. It does NOT replicate Core business rules
/// (those are validated by <see cref="InspectionWorkflowValidator"/> at Phase 4 of
/// the CLI pipeline, and by the Core rule engine during inspection).
///
/// The single genuine gap this class closes: <see cref="InspectionWorkflowValidator"/>
/// only rejects a null <c>Requirements</c> list, not an *empty* one. An assignment
/// with zero requirements would otherwise pass validation and reach the inspection
/// pipeline, producing an empty result. That is obviously illegal configuration from
/// a user perspective, so we reject it up front with a clear message.
/// </summary>
public static class CliInputValidator
{
    /// <summary>
    /// Returns a list of human-readable errors for obviously-illegal assignment
    /// configurations. An empty list means the assignment is structurally acceptable
    /// for the next phase.
    /// </summary>
    public static IReadOnlyList<string> ValidateAssignment(AssignmentDefinition assignment)
    {
        var errors = new List<string>();

        if (assignment == null)
        {
            errors.Add("Assignment is null and could not be parsed.");
            return errors;
        }

        if (assignment.Requirements == null || assignment.Requirements.Count == 0)
        {
            errors.Add(
                "Assignment must contain at least one requirement. " +
                "An assignment without requirements cannot verify anything.");
        }

        return errors;
    }
}
