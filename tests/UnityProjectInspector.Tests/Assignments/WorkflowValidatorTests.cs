using UnityProjectInspector.Core.Assignments;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// Tests for InspectionWorkflowValidator.
///
/// Validates the assignment configuration checks:
///   - valid assignment
///   - duplicate requirement ID
///   - missing requirement ID
///   - RuntimeRequired + no RuntimeTest
///   - StaticOnly + RuntimeTest → Warning
///   - invalid static RuleDefinition
/// </summary>
[Trait("Category", "Unit")]
public class WorkflowValidatorTests
{
    private readonly InspectionWorkflowValidator _validator = new();

    [Fact]
    public void Validate_ValidAssignment_ReturnsNoErrors()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "maze-2d",
            Name = "2D Maze",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "req-1",
                    Name = "MainMenu exists",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.exists",
                            Name = "Scene exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                },
            },
        };

        var issues = _validator.Validate(assignment);

        Assert.DoesNotContain(issues, i => i.Severity == WorkflowIssueSeverity.Error);
    }

    [Fact]
    public void Validate_DuplicateRequirementId_ReturnsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new() { Id = "dup", Name = "Req 1" },
                new() { Id = "dup", Name = "Req 2" },
            },
        };

        var issues = _validator.Validate(assignment);

        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Error &&
            i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_MissingRequirementId_ReturnsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new() { Id = "", Name = "Req 1" },
            },
        };

        var issues = _validator.Validate(assignment);

        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Error &&
            i.Path.Contains("id"));
    }

    [Fact]
    public void Validate_RuntimeRequiredWithoutRuntimeTest_ReturnsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    // No RuntimeTest
                },
            },
        };

        var issues = _validator.Validate(assignment);

        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Error &&
            i.Message.Contains("RuntimeRequired") &&
            i.Message.Contains("no RuntimeTest"));
    }

    [Fact]
    public void Validate_StaticOnlyWithRuntimeTest_ReturnsWarning()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "static-req",
                    Name = "Static Req with Runtime",
                    EvidenceRequirement = "StaticOnly",
                    RuntimeTest = new Core.Runtime.RuntimeTestScript
                    {
                        Name = "Test script",
                    },
                },
            },
        };

        var issues = _validator.Validate(assignment);

        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Warning &&
            i.Message.Contains("StaticOnly") &&
            i.Message.Contains("RuntimeTest"));
    }

    [Fact]
    public void Validate_UnknownRuleType_ReturnsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "bad-rule",
                    Name = "Bad rule",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "rule-invalid",
                            Name = "Invalid",
                            Type = "DoesNotExistType",
                        },
                    },
                },
            },
        };

        var issues = _validator.Validate(assignment);

        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Error &&
            i.Message.Contains("DoesNotExistType"));
    }

    [Fact]
    public void Validate_EmptyAssignmentId_ReturnsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "",
            Name = "Test",
        };

        var issues = _validator.Validate(assignment);
        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Error &&
            i.Path.Contains("assignment.id"));
    }

    [Fact]
    public void Validate_EmptyAssignmentName_ReturnsError()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "",
        };

        var issues = _validator.Validate(assignment);
        Assert.Contains(issues, i =>
            i.Severity == WorkflowIssueSeverity.Error &&
            i.Path.Contains("assignment.name"));
    }
}