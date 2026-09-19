using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Executes a collection of IRule instances against an InspectionContext.
///
/// The engine is stateless — it only iterates over rules and collects results.
/// It does NOT implement any rule-specific logic.
/// A failure in one rule does NOT prevent other rules from executing.
/// </summary>
public class RuleEngine
{
    /// <summary>
    /// Evaluates all given rules against the context.
    /// Each rule produces exactly one RuleResult.
    /// Results are returned in the same order as the input rules.
    /// </summary>
    public List<RuleResult> Run(InspectionContext context, IReadOnlyList<IRule> rules)
    {
        var results = new List<RuleResult>(rules.Count);

        foreach (var rule in rules)
        {
            try
            {
                results.Add(rule.Evaluate(context));
            }
            catch (Exception ex)
            {
                results.Add(new RuleResult
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    Status = RuleStatus.NotEvaluated,
                    Severity = RuleSeverity.Error,
                    Message = $"Rule threw an exception: {ex.Message}",
                });
            }
        }

        return results;
    }
}