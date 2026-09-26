using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Extracts evidence from Core's <see cref="RuleDefinition"/> + <see cref="RuleResult"/>
/// at the CLI layer. No Core DTOs are modified — all evidence is derived from existing
/// data by positional index matching between a requirement's static rule definitions
/// and their corresponding results.
///
/// Evidence fields are only populated when the source data is reliably available.
/// No information is ever fabricated; when a source/location cannot be determined,
/// the field is simply left null.
/// </summary>
public static class RuleEvidenceBuilder
{
    /// <summary>
    /// Builds per-rule evidence for a single requirement by matching its
    /// <see cref="RequirementDefinition.StaticRules"/> (position i) with
    /// <see cref="RequirementInspectionResult.StaticResults"/> (position i).
    /// Returns null when no static rules were evaluated.
    /// </summary>
    public static List<CliRuleEvidence>? BuildEvidence(
        RequirementDefinition requirement,
        List<RuleResult>? staticResults)
    {
        if (requirement == null || staticResults == null || staticResults.Count == 0)
            return null;

        var evidence = new List<CliRuleEvidence>(staticResults.Count);

        for (int i = 0; i < staticResults.Count; i++)
        {
            var ruleDef = i < requirement.StaticRules.Count ? requirement.StaticRules[i] : null;
            var ruleResult = staticResults[i];

            evidence.Add(BuildSingleEvidence(ruleDef, ruleResult));
        }

        return evidence;
    }

    private static CliRuleEvidence BuildSingleEvidence(RuleDefinition? ruleDef, RuleResult result)
    {
        var statusStr = CliStatusFormatter.StatusToString(result.Status);
        var ruleType = ruleDef?.Type ?? result.RuleName;

        // Extract structured fields from the combination of RuleDefinition + RuleResult
        var (actual, reason, source) = ExtractFromMessage(result, ruleDef);

        return new CliRuleEvidence
        {
            RuleId = result.RuleId,
            RuleType = ruleType,
            Status = statusStr,
            Target = ruleDef?.Target,
            Expected = DetermineExpected(ruleDef),
            Actual = actual,
            Reason = reason ?? result.Message,
            Source = source,
        };
    }

    /// <summary>
    /// Determines the "expected" field based on rule type.
    /// For ComponentExists/ScriptAttached: the expected component/script class.
    /// For GameObjectHierarchy: the expected parent name.
    /// For UnityEventBinding/CodeEvidence: the expected method name.
    /// Otherwise: null.
    /// </summary>
    private static string? DetermineExpected(RuleDefinition? ruleDef)
    {
        if (ruleDef == null) return null;

        return ruleDef.Type switch
        {
            "ComponentExists" or "ScriptAttached" => ruleDef.ExpectedClass,
            "GameObjectHierarchy" => ruleDef.ExpectedParent,
            "UnityEventBinding" or "CodeEvidence" => ruleDef.ExpectedMethod,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts actual/reason/source from the existing RuleResult message string
    /// and RuleDefinition. Parsing is done by rule type and is intentionally
    /// conservative — unknown message patterns return null for the field.
    /// </summary>
    private static (string? actual, string? reason, string? source) ExtractFromMessage(
        RuleResult result, RuleDefinition? ruleDef)
    {
        var msg = result.Message ?? string.Empty;
        var ruleType = ruleDef?.Type ?? result.RuleName;

        // --- PASS ---
        if (result.Status == RuleStatus.Passed)
        {
            switch (ruleType)
            {
                case "SceneExists":
                    return ExtractSceneExistsPass(msg, ruleDef);

                case "GameObjectExists":
                    return ExtractGameObjectExistsPass(msg, ruleDef);

                case "ComponentExists":
                    return ExtractComponentExistsPass(msg, ruleDef);

                case "FileExists":
                    return ExtractFileExistsPass(msg, ruleDef);

                case "ScriptAttached":
                    return ExtractScriptAttachedPass(msg, ruleDef);

                case "GameObjectHierarchy":
                    return ExtractGameObjectHierarchyPass(msg, ruleDef);

                case "UnityEventBinding":
                    return ExtractBindingPass(msg, ruleDef);

                case "CodeEvidence":
                    return ExtractCodeEvidencePass(msg, ruleDef);

                default:
                    return ("exists", null, null);
            }
        }

        // --- FAIL ---
        if (result.Status == RuleStatus.Failed)
        {
            switch (ruleType)
            {
                case "SceneExists":
                    return ExtractSceneExistsFail(msg, ruleDef);

                case "GameObjectExists":
                    return ExtractGameObjectExistsFail(msg, ruleDef);

                case "ComponentExists":
                    return ExtractComponentExistsFail(msg, ruleDef);

                case "FileExists":
                    return ExtractFileExistsFail(msg, ruleDef);

                case "ScriptAttached":
                    return ExtractScriptAttachedFail(msg, ruleDef);

                case "GameObjectHierarchy":
                    return ExtractGameObjectHierarchyFail(msg, ruleDef);

                case "UnityEventBinding":
                    return ExtractBindingFail(msg, ruleDef);

                case "CodeEvidence":
                    return ExtractCodeEvidenceFail(msg, ruleDef);

                default:
                    return ("not found", msg, null);
            }
        }

        // --- NOT EVALUATED ---
        return ("not evaluated", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // SceneExists evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractSceneExistsPass(
        string msg, RuleDefinition? ruleDef)
    {
        // "Scene 'Main' exists"
        var sceneName = ruleDef?.Target;
        return ("exists", null, sceneName != null ? $"Scenes/{sceneName}.unity" : null);
    }

    private static (string? actual, string? reason, string? source) ExtractSceneExistsFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "Scene 'BossLevel' not found in project"
        // "Scene 'BossLevel' not found — no scenes in project"
        if (msg.Contains("not found"))
        {
            if (msg.Contains("no scenes"))
                return ("not found", "Project contains no scenes at all", null);
            return ("not found", "Scene not found in project", null);
        }
        return ("not found", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // GameObjectExists evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractGameObjectExistsPass(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'Player' found (2 matches)"
        // "GameObject 'Player' exists in scene 'Level'"
        var target = ruleDef?.Target;
        if (msg.Contains("in scene"))
        {
            var sceneStart = msg.LastIndexOf("in scene '");
            if (sceneStart >= 0)
            {
                var sceneName = msg[(sceneStart + 9)..].TrimEnd('\'');
                return ("exists", null, $"{sceneName}.unity");
            }
        }
        if (msg.Contains("found"))
            return ("exists", "Found in project", null);

        return ("exists", null, null);
    }

    private static (string? actual, string? reason, string? source) ExtractGameObjectExistsFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'SecretRoom' not found in any scene"
        if (msg.Contains("not found"))
        {
            if (msg.Contains("in scene"))
            {
                // Scoped: "GameObject 'X' not found in scene 'Y'"
                return ("not found", "GameObject not found in specified scene", null);
            }
            return ("not found", "GameObject not found in any scene", null);
        }
        return ("not found", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // ComponentExists evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractComponentExistsPass(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'Main Camera' has component 'Camera'"
        return ("exists", null, null);
    }

    private static (string? actual, string? reason, string? source) ExtractComponentExistsFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'Main Camera' does not have component 'Camera'"
        return ("not found", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // FileExists evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractFileExistsPass(
        string msg, RuleDefinition? ruleDef)
    {
        // "File exists: 'Assets/Scripts/PlayerMove.cs'"
        var target = ruleDef?.Target;
        return ("exists", null, target);
    }

    private static (string? actual, string? reason, string? source) ExtractFileExistsFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "File not found: 'Assets/Scripts/BossFight.cs' (resolved: '/Users/tangluyi/...')"
        if (msg.Contains("not found"))
        {
            return ("not found", "File not found in project", null);
        }
        if (msg.Contains("Absolute paths"))
            return ("invalid", msg, null);
        if (msg.Contains("path traversal"))
            return ("invalid", msg, null);

        return ("not found", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // ScriptAttached evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractScriptAttachedPass(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'Player' has script 'PlayerMove' attached"
        return ("script attached", null, null);
    }

    private static (string? actual, string? reason, string? source) ExtractScriptAttachedFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'X' not found"
        // "GameObject 'X' has scripts 'A', 'B', expected 'C'"
        // "GameObject 'X' has no MonoBehaviour components"
        if (msg.Contains("not found"))
            return ("not found", "GameObject not found", null);
        if (msg.Contains("has scripts"))
        {
            var cls = ruleDef?.ExpectedClass ?? "unknown";
            return ("wrong script", $"Expected script '{cls}' but GameObject has different scripts", null);
        }
        if (msg.Contains("no MonoBehaviour"))
            return ("no scripts", "GameObject has no MonoBehaviour components", null);

        return ("not evaluated", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // GameObjectHierarchy evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractGameObjectHierarchyPass(
        string msg, RuleDefinition? ruleDef)
    {
        // "GameObject 'Child' is a child of 'Parent'"
        return ("is child of expected parent", null, null);
    }

    private static (string? actual, string? reason, string? source) ExtractGameObjectHierarchyFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "Child GameObject 'X' not found"
        // "GameObject 'X' is a root object (no parent)"
        // "GameObject 'X' parent is 'Y', expected 'Z'"
        if (msg.Contains("not found"))
            return ("not found", "Child GameObject not found", null);
        if (msg.Contains("root"))
            return ("no parent", "Child GameObject has no parent (it is a root object)", null);
        if (msg.Contains("parent is"))
            return ("wrong parent", msg, null);

        return ("not found", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // UnityEventBinding evidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractBindingPass(
        string msg, RuleDefinition? ruleDef)
    {
        return ("binding exists", null, null);
    }

    private static (string? actual, string? reason, string? source) ExtractBindingFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "No UnityEvent bindings found on 'X'"
        // "UnityEvent binding on 'X' targets method 'Y' not found"
        // "UnityEvent binding on 'X' targets class 'A', but rule expected 'B'"
        if (msg.Contains("No UnityEvent bindings"))
            return ("no bindings", "No UnityEvent bindings found on source GameObject", null);
        if (msg.Contains("not found"))
            return ("method not found", "Expected method not found in bindings", null);
        if (msg.Contains("but rule expected"))
            return ("wrong class", msg, null);

        return ("failed", msg, null);
    }

    // ═══════════════════════════════════════════════════════════
    // CodeEvidence parsing
    // ═══════════════════════════════════════════════════════════

    private static (string? actual, string? reason, string? source) ExtractCodeEvidencePass(
        string msg, RuleDefinition? ruleDef)
    {
        return ("evidence chain verified", null, null);
    }

    private static (string? actual, string? reason, string? source) ExtractCodeEvidenceFail(
        string msg, RuleDefinition? ruleDef)
    {
        // "No code evidence chain found: 'X' → 'Y'"
        // "Code evidence chain is broken: ..."
        // "Expected call 'X' not found..."
        if (msg.Contains("No code evidence chain"))
            return ("no chain", "Code evidence chain not found between source and target", null);
        if (msg.Contains("chain is broken"))
            return ("broken", msg, null);
        if (msg.Contains("Expected call"))
            return ("call not found", msg, null);

        return ("failed", msg, null);
    }
}