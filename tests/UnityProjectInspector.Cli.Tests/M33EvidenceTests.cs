using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M33 Inspection Evidence &amp; Explainability tests.
///
/// Covers:
///   - RuleEvidenceBuilder extractors for all 8 static rule types (PASS + FAIL = 16)
///   - Evidence JSON serialization (null fields omitted, non-null fields present)
///   - Evidence appearance in Markdown report output
///   - Evidence appearance in HTML report output
///   - Evidence appearance in Chinese (UTF-8) output
///   - Backward compatibility (evidence does not break existing contract)
///   - Null/edge-case handling
///
/// Tests build Core DTOs directly (no fixture project needed) and verify evidence
/// at the CLI layer — the same path used by real inspections.
/// </summary>
public class M33EvidenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a Core <see cref="AssignmentInspectionResult"/> with one requirement
    /// containing the given static rules and (optionally expected) results.
    /// Results default to auto-generated from the rule definitions.
    /// </summary>
    private static AssignmentInspectionResult MakeCoreResult(
        RequirementDefinition reqDef,
        List<RuleResult> staticResults)
    {
        return new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition
            {
                Id = "m33-test",
                Name = "M33 Evidence Test",
                Description = "Inspection evidence explainability tests.",
            },
            RequirementResults = new List<RequirementInspectionResult>
            {
                new()
                {
                    Requirement = reqDef,
                    Status = staticResults.Any(r => r.Status == RuleStatus.Failed)
                        ? RuleStatus.Failed
                        : RuleStatus.Passed,
                    Message = staticResults.Any(r => r.Status == RuleStatus.Failed)
                        ? "Some checks failed."
                        : "All checks passed.",
                    StaticResults = staticResults,
                },
            },
            FinalStatus = staticResults.Any(r => r.Status == RuleStatus.Failed)
                ? RuleStatus.Failed
                : RuleStatus.Passed,
            Message = "Assignment completed.",
        };
    }

    private static RequirementDefinition SimpleRequirement(string id, params RuleDefinition[] rules) =>
        new()
        {
            Id = id,
            Name = id + " requirement",
            EvidenceRequirement = "StaticOnly",
            StaticRules = rules.ToList(),
        };

    private static RuleDefinition MakeRuleDef(
        string id, string type, string? target = null,
        string? expectedClass = null, string? expectedParent = null,
        string? expectedMethod = null) =>
        new()
        {
            Id = id,
            Name = id,
            Type = type,
            Target = target,
            ExpectedClass = expectedClass,
            ExpectedParent = expectedParent,
            ExpectedMethod = expectedMethod,
        };

    private static RuleResult MakeResult(string ruleId, string ruleName, RuleStatus status, string message) =>
        new()
        {
            RuleId = ruleId,
            RuleName = ruleName,
            Status = status,
            Severity = RuleSeverity.Error,
            Message = message,
        };

    /// <summary>Serialise a CliInspectionResult or InspectionReport to JSON string.</summary>
    private static string ToJson<T>(T obj) => JsonSerializer.Serialize(obj, JsonOptions);

    /// <summary>Parse JSON back to JsonElement.</summary>
    private static JsonElement ParseJson(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    // ═══════════════════════════════════════════════════════════════
    // 1. SceneExists evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SceneExists_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.main", "SceneExists", target: "Main"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.main", "SceneExists", RuleStatus.Passed, "Scene 'Main' exists"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("scene.main", cli.RuleId);
        Assert.Equal("SceneExists", cli.RuleType);
        Assert.Equal("PASSED", cli.Status);
        Assert.Equal("Main", cli.Target);
        Assert.Null(cli.Expected);
        Assert.Equal("exists", cli.Actual);
        Assert.Equal("Scenes/Main.unity", cli.Source);
        // Reason falls back to result.Message when extractor returns null
        Assert.Equal("Scene 'Main' exists", cli.Reason);
    }

    [Fact]
    public void SceneExists_Fail_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.boss", "SceneExists", target: "BossLevel"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.boss", "SceneExists", RuleStatus.Failed,
                "Scene 'BossLevel' not found in project"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("scene.boss", cli.RuleId);
        Assert.Equal("SceneExists", cli.RuleType);
        Assert.Equal("FAILED", cli.Status);
        Assert.Equal("BossLevel", cli.Target);
        Assert.Equal("not found", cli.Actual);
        Assert.Equal("Scene not found in project", cli.Reason);
        Assert.Null(cli.Source);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. GameObjectExists evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GameObjectExists_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("gameobjects",
            MakeRuleDef("go.player", "GameObjectExists", target: "Player"));
        var results = new List<RuleResult>
        {
            MakeResult("go.player", "GameObjectExists", RuleStatus.Passed,
                "GameObject 'Player' found (2 matches)"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("Player", cli.Target);
        Assert.Equal("exists", cli.Actual);
        Assert.Equal("Found in project", cli.Reason);
        Assert.Null(cli.Source); // no scene specified in message
    }

    [Fact]
    public void GameObjectExists_Fail_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("gameobjects",
            MakeRuleDef("go.secret", "GameObjectExists", target: "SecretRoom"));
        var results = new List<RuleResult>
        {
            MakeResult("go.secret", "GameObjectExists", RuleStatus.Failed,
                "GameObject 'SecretRoom' not found in any scene"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("SecretRoom", cli.Target);
        Assert.Equal("not found", cli.Actual);
        Assert.Equal("GameObject not found in any scene", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. ComponentExists evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ComponentExists_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("components",
            MakeRuleDef("comp.camera", "ComponentExists", target: "Main Camera",
                expectedClass: "Camera"));
        var results = new List<RuleResult>
        {
            MakeResult("comp.camera", "ComponentExists", RuleStatus.Passed,
                "GameObject 'Main Camera' has component 'Camera'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("Main Camera", cli.Target);
        Assert.Equal("Camera", cli.Expected);
        Assert.Equal("exists", cli.Actual);
        // Reason falls back to result.Message when extractor returns null
        Assert.Equal("GameObject 'Main Camera' has component 'Camera'", cli.Reason);
        Assert.Null(cli.Source); // ComponentExists has no file path source
    }

    [Fact]
    public void ComponentExists_Fail_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("components",
            MakeRuleDef("comp.missing", "ComponentExists", target: "Main Camera",
                expectedClass: "Rigidbody"));
        var results = new List<RuleResult>
        {
            MakeResult("comp.missing", "ComponentExists", RuleStatus.Failed,
                "GameObject 'Main Camera' does not have component 'Rigidbody'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("Main Camera", cli.Target);
        Assert.Equal("Rigidbody", cli.Expected);
        Assert.Equal("not found", cli.Actual);
        Assert.Equal("GameObject 'Main Camera' does not have component 'Rigidbody'", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. FileExists evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FileExists_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("files",
            MakeRuleDef("file.move", "FileExists", target: "Assets/Scripts/PlayerMove.cs"));
        var results = new List<RuleResult>
        {
            MakeResult("file.move", "FileExists", RuleStatus.Passed,
                "File exists: 'Assets/Scripts/PlayerMove.cs'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("Assets/Scripts/PlayerMove.cs", cli.Target);
        Assert.Equal("exists", cli.Actual);
        Assert.Equal("Assets/Scripts/PlayerMove.cs", cli.Source); // source = target for FileExists
        Assert.Equal("File exists: 'Assets/Scripts/PlayerMove.cs'", cli.Reason);
    }

    [Fact]
    public void FileExists_Fail_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("files",
            MakeRuleDef("file.boss", "FileExists", target: "Assets/Scripts/BossFight.cs"));
        var results = new List<RuleResult>
        {
            MakeResult("file.boss", "FileExists", RuleStatus.Failed,
                "File not found: 'Assets/Scripts/BossFight.cs' (resolved: '/Users/test/...')"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("Assets/Scripts/BossFight.cs", cli.Target);
        Assert.Equal("not found", cli.Actual);
        Assert.Equal("File not found in project", cli.Reason);
        Assert.Null(cli.Source); // source is null on FAIL because we can't reliably extract it
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. ScriptAttached evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScriptAttached_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("scripts",
            MakeRuleDef("script.player", "ScriptAttached", target: "Player",
                expectedClass: "PlayerMove"));
        var results = new List<RuleResult>
        {
            MakeResult("script.player", "ScriptAttached", RuleStatus.Passed,
                "GameObject 'Player' has script 'PlayerMove' attached"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("Player", cli.Target);
        Assert.Equal("PlayerMove", cli.Expected);
        Assert.Equal("script attached", cli.Actual);
    }

    [Fact]
    public void ScriptAttached_Fail_GameObjectNotFound()
    {
        var reqDef = SimpleRequirement("scripts",
            MakeRuleDef("script.missing", "ScriptAttached", target: "MissingObj",
                expectedClass: "SomeScript"));
        var results = new List<RuleResult>
        {
            MakeResult("script.missing", "ScriptAttached", RuleStatus.Failed,
                "GameObject 'MissingObj' not found"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("not found", cli.Actual);
        Assert.Equal("GameObject not found", cli.Reason);
    }

    [Fact]
    public void ScriptAttached_Fail_WrongScript()
    {
        var reqDef = SimpleRequirement("scripts",
            MakeRuleDef("script.wrong", "ScriptAttached", target: "Player",
                expectedClass: "PlayerMove"));
        var results = new List<RuleResult>
        {
            MakeResult("script.wrong", "ScriptAttached", RuleStatus.Failed,
                "GameObject 'Player' has scripts 'Controller', 'Health', expected 'PlayerMove'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("wrong script", cli.Actual);
        Assert.Equal("Expected script 'PlayerMove' but GameObject has different scripts", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. GameObjectHierarchy evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GameObjectHierarchy_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("hierarchy",
            MakeRuleDef("hier.child", "GameObjectHierarchy", target: "ChildObj",
                expectedParent: "ParentObj"));
        var results = new List<RuleResult>
        {
            MakeResult("hier.child", "GameObjectHierarchy", RuleStatus.Passed,
                "GameObject 'ChildObj' is a child of 'ParentObj'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("ChildObj", cli.Target);
        Assert.Equal("ParentObj", cli.Expected);
        Assert.Equal("is child of expected parent", cli.Actual);
    }

    [Fact]
    public void GameObjectHierarchy_Fail_RootObject()
    {
        var reqDef = SimpleRequirement("hierarchy",
            MakeRuleDef("hier.root", "GameObjectHierarchy", target: "RootObj",
                expectedParent: "SomeParent"));
        var results = new List<RuleResult>
        {
            MakeResult("hier.root", "GameObjectHierarchy", RuleStatus.Failed,
                "GameObject 'RootObj' is a root object (no parent)"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("no parent", cli.Actual);
        Assert.Equal("Child GameObject has no parent (it is a root object)", cli.Reason);
    }

    [Fact]
    public void GameObjectHierarchy_Fail_WrongParent()
    {
        var reqDef = SimpleRequirement("hierarchy",
            MakeRuleDef("hier.wrong", "GameObjectHierarchy", target: "ChildObj",
                expectedParent: "ExpectedParent"));
        var results = new List<RuleResult>
        {
            MakeResult("hier.wrong", "GameObjectHierarchy", RuleStatus.Failed,
                "GameObject 'ChildObj' parent is 'ActualParent', expected 'ExpectedParent'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("wrong parent", cli.Actual);
        Assert.Contains("parent is 'ActualParent', expected 'ExpectedParent'", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. UnityEventBinding evidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UnityEventBinding_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("bindings",
            MakeRuleDef("bind.btn", "UnityEventBinding", target: "StartButton",
                expectedClass: "PanelSwitcher", expectedMethod: "SwitchPanel"));
        var results = new List<RuleResult>
        {
            MakeResult("bind.btn", "UnityEventBinding", RuleStatus.Passed,
                "UnityEvent binding verified on 'StartButton'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("StartButton", cli.Target);
        // UnityEventBinding Expected = expectedMethod ("SwitchPanel"), not expectedClass
        Assert.Equal("SwitchPanel", cli.Expected);
        Assert.Equal("binding exists", cli.Actual);
    }

    [Fact]
    public void UnityEventBinding_Fail_MethodNotFound()
    {
        var reqDef = SimpleRequirement("bindings",
            MakeRuleDef("bind.missing", "UnityEventBinding", target: "StartButton",
                expectedClass: "PanelSwitcher", expectedMethod: "MissingMethod"));
        var results = new List<RuleResult>
        {
            MakeResult("bind.missing", "UnityEventBinding", RuleStatus.Failed,
                "UnityEvent binding on 'StartButton' targets method 'MissingMethod' not found"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("method not found", cli.Actual);
        Assert.Equal("Expected method not found in bindings", cli.Reason);
    }

    [Fact]
    public void UnityEventBinding_Fail_NoBindings()
    {
        var reqDef = SimpleRequirement("bindings",
            MakeRuleDef("bind.none", "UnityEventBinding", target: "PlainObject",
                expectedClass: "SomeClass", expectedMethod: "SomeMethod"));
        var results = new List<RuleResult>
        {
            MakeResult("bind.none", "UnityEventBinding", RuleStatus.Failed,
                "No UnityEvent bindings found on 'PlainObject'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("no bindings", cli.Actual);
        Assert.Equal("No UnityEvent bindings found on source GameObject", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. CodeEvidence
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CodeEvidence_Pass_HasCorrectEvidence()
    {
        var reqDef = SimpleRequirement("code",
            MakeRuleDef("code.chain", "CodeEvidence", target: "SourceObj",
                expectedMethod: "TargetMethod"));
        var results = new List<RuleResult>
        {
            MakeResult("code.chain", "CodeEvidence", RuleStatus.Passed,
                "Code evidence chain verified: SourceObj → TargetMethod"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("SourceObj", cli.Target);
        Assert.Equal("TargetMethod", cli.Expected);
        Assert.Equal("evidence chain verified", cli.Actual);
    }

    [Fact]
    public void CodeEvidence_Fail_NoChain()
    {
        var reqDef = SimpleRequirement("code",
            MakeRuleDef("code.broken", "CodeEvidence", target: "SourceObj",
                expectedMethod: "MissingMethod"));
        var results = new List<RuleResult>
        {
            MakeResult("code.broken", "CodeEvidence", RuleStatus.Failed,
                "No code evidence chain found: 'SourceObj' → 'MissingMethod'"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("no chain", cli.Actual);
        Assert.Equal("Code evidence chain not found between source and target", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. JSON serialization — null fields omitted
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CliJson_OmitsNullEvidenceFields()
    {
        // ComponentExists PASS: has Target, Expected, Actual but no Reason or Source
        var reqDef = SimpleRequirement("comp",
            MakeRuleDef("comp.cam", "ComponentExists", target: "Main Camera",
                expectedClass: "Camera"));
        var results = new List<RuleResult>
        {
            MakeResult("comp.cam", "ComponentExists", RuleStatus.Passed,
                "GameObject 'Main Camera' has component 'Camera'"),
        };
        var core = MakeCoreResult(reqDef, results);
        var cli = ResultFormatter.ToCliResult(core);
        var json = ToJson(cli);
        var parsed = ParseJson(json);

        var reqs = parsed.GetProperty("requirements");
        Assert.Equal(1, reqs.GetArrayLength());
        var evidence = reqs[0].GetProperty("ruleEvidence");
        Assert.Equal(1, evidence.GetArrayLength());
        var ev = evidence[0];

        // Non-null properties exist
        Assert.Equal("comp.cam", ev.GetProperty("ruleId").GetString());
        Assert.Equal("ComponentExists", ev.GetProperty("ruleType").GetString());
        Assert.Equal("PASSED", ev.GetProperty("status").GetString());
        Assert.Equal("Main Camera", ev.GetProperty("target").GetString());
        Assert.Equal("Camera", ev.GetProperty("expected").GetString());
        Assert.Equal("exists", ev.GetProperty("actual").GetString());

        // ComponentExists PASS: reason falls back to message, source is null
        // Reason is non-null (fallback to result.Message), source is null
        Assert.True(ev.TryGetProperty("reason", out var reasonVal),
            "'reason' must be present (falls back to result.Message)");
        Assert.Equal("GameObject 'Main Camera' has component 'Camera'", reasonVal.GetString());
        Assert.False(ev.TryGetProperty("source", out _),
            "Null 'source' must be omitted from JSON");
    }

    [Fact]
    public void ReportJson_OmitsNullEvidenceFields()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.m", "SceneExists", target: "Main"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.m", "SceneExists", RuleStatus.Passed, "Scene 'Main' exists"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);
        var json = ToJson(report);
        var parsed = ParseJson(json);

        var reqs = parsed.GetProperty("requirements");
        var ev = reqs[0].GetProperty("ruleEvidence")[0];

        // Non-null fields present
        Assert.Equal("Scenes/Main.unity", ev.GetProperty("source").GetString());
        Assert.True(ev.TryGetProperty("reason", out _));

        // Null fields absent — SceneExists has no "expected" field
        Assert.False(ev.TryGetProperty("expected", out _));
    }

    [Fact]
    public void CliJson_NoEvidence_OmitsRuleEvidenceProperty()
    {
        // When RuleEvidence is null, the "ruleEvidence" key must not appear
        var core = new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition { Id = "a", Name = "A" },
            FinalStatus = RuleStatus.Passed,
            Message = "ok",
            RequirementResults = new List<RequirementInspectionResult>
            {
                new()
                {
                    Requirement = new RequirementDefinition
                    {
                        Id = "r1", Name = "R1", EvidenceRequirement = "StaticOnly",
                        StaticRules = new List<RuleDefinition>(),
                    },
                    Status = RuleStatus.Passed,
                    Message = "ok",
                    StaticResults = new List<RuleResult>(), // empty → evidence null
                },
            },
        };
        var cli = ResultFormatter.ToCliResult(core);
        var json = ToJson(cli);
        var parsed = ParseJson(json);

        var reqs = parsed.GetProperty("requirements");
        Assert.Equal(1, reqs.GetArrayLength());
        Assert.False(reqs[0].TryGetProperty("ruleEvidence", out _),
            "Empty static results must not produce 'ruleEvidence' key in JSON");
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. Markdown evidence rendering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Markdown_ContainsEvidenceSections()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.m", "SceneExists", target: "TrainingScene"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.m", "SceneExists", RuleStatus.Passed, "Scene 'TrainingScene' exists"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        var writer = new StringWriter();
        InspectionReportWriter.WriteMarkdown(report, writer);
        var md = writer.ToString();

        // Evidence section header
        Assert.Contains("### Evidence", md, StringComparison.Ordinal);
        // Evidence field labels
        Assert.Contains("Status", md, StringComparison.Ordinal);
        Assert.Contains("Target", md, StringComparison.Ordinal);
        Assert.Contains("Result", md, StringComparison.Ordinal);
        Assert.Contains("Source", md, StringComparison.Ordinal);
        // Rule type in evidence
        Assert.Contains("SceneExists", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_ContainsFailEvidence()
    {
        var reqDef = SimpleRequirement("missing",
            MakeRuleDef("scene.boss", "SceneExists", target: "BossLevel"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.boss", "SceneExists", RuleStatus.Failed,
                "Scene 'BossLevel' not found in project"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        var writer = new StringWriter();
        InspectionReportWriter.WriteMarkdown(report, writer);
        var md = writer.ToString();

        Assert.Contains("not found", md, StringComparison.Ordinal);
        Assert.Contains("Scene not found in project", md, StringComparison.Ordinal);
        Assert.Contains("FAILED", md, StringComparison.Ordinal);
        Assert.Contains("SceneExists", md, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. HTML evidence rendering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Html_ContainsEvidenceSections()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.m", "SceneExists", target: "TrainingScene"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.m", "SceneExists", RuleStatus.Passed, "Scene 'TrainingScene' exists"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        var writer = new StringWriter();
        InspectionReportWriter.WriteHtml(report, writer);
        var html = writer.ToString();

        // Evidence section heading
        Assert.Contains("<h3>Evidence</h3>", html, StringComparison.Ordinal);
        // Evidence card with rule type
        Assert.Contains("SceneExists", html, StringComparison.Ordinal);
        // Field labels
        Assert.Contains("Target</td><td>TrainingScene", html, StringComparison.Ordinal);
        Assert.Contains("Result</td><td>exists", html, StringComparison.Ordinal);
        Assert.Contains("Source</td><td>Scenes", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_ContainsFailEvidence()
    {
        var reqDef = SimpleRequirement("missing",
            MakeRuleDef("file.boss", "FileExists", target: "Assets/Scripts/BossFight.cs"));
        var results = new List<RuleResult>
        {
            MakeResult("file.boss", "FileExists", RuleStatus.Failed,
                "File not found: 'Assets/Scripts/BossFight.cs' (resolved: '/u/test')"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        var writer = new StringWriter();
        InspectionReportWriter.WriteHtml(report, writer);
        var html = writer.ToString();

        Assert.Contains("FileExists", html, StringComparison.Ordinal);
        Assert.Contains("FAILED", html, StringComparison.Ordinal);
        Assert.Contains("not found", html, StringComparison.Ordinal);
        Assert.Contains("File not found in project", html, StringComparison.Ordinal);
        Assert.Contains("evidence-card", html, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. Chinese UTF-8 evidence rendering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ChineseOutput_ContainsEvidenceSections()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.m", "SceneExists", target: "TrainingScene"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.m", "SceneExists", RuleStatus.Passed, "Scene 'TrainingScene' exists"),
        };
        var core = MakeCoreResult(reqDef, results);
        var cli = ResultFormatter.ToCliResult(core);

        var writer = new StringWriter();
        ChineseResultFormatter.WriteDetailed(cli, writer);
        var output = writer.ToString();

        // Evidence labels in Chinese
        Assert.Contains("目标", output, StringComparison.Ordinal);
        Assert.Contains("结果", output, StringComparison.Ordinal);
        Assert.Contains("来源", output, StringComparison.Ordinal);
        // Rule type (translated to Chinese by ChineseResultFormatter)
        Assert.Contains("场景存在", output, StringComparison.Ordinal);
        // Chinese status
        Assert.Contains("通过", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ChineseOutput_ContainsFailEvidence()
    {
        var reqDef = SimpleRequirement("missing",
            MakeRuleDef("go.secret", "GameObjectExists", target: "SecretRoom"));
        var results = new List<RuleResult>
        {
            MakeResult("go.secret", "GameObjectExists", RuleStatus.Failed,
                "GameObject 'SecretRoom' not found in any scene"),
        };
        var core = MakeCoreResult(reqDef, results);
        var cli = ResultFormatter.ToCliResult(core);

        var writer = new StringWriter();
        ChineseResultFormatter.WriteDetailed(cli, writer);
        var output = writer.ToString();

        Assert.Contains("SecretRoom", output, StringComparison.Ordinal);
        Assert.Contains("不存在", output, StringComparison.Ordinal);
        Assert.Contains("未通过", output, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. Backward compatibility — existing contract unchanged
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ExistingReportContract_UnchangedWithEvidence()
    {
        // Verify that adding evidence does not alter existing report contract fields
        // by comparing a result WITH evidence against a baseline
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.m", "SceneExists", target: "TrainingScene"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.m", "SceneExists", RuleStatus.Passed, "Scene 'TrainingScene' exists"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        using var writer = new StringWriter();
        InspectionReportWriter.WriteJson(report, writer);
        var json = writer.ToString();
        var parsed = ParseJson(json);

        // Standard contract fields still present
        Assert.Equal("1.0", parsed.GetProperty("schemaVersion").GetString());
        Assert.Equal("m33-test", parsed.GetProperty("assignmentId").GetString());
        Assert.Equal("PASSED", parsed.GetProperty("finalStatus").GetString());

        var reqs = parsed.GetProperty("requirements");
        Assert.Equal(1, reqs.GetArrayLength());
        var r = reqs[0];
        Assert.Equal("scenes", r.GetProperty("id").GetString());
        Assert.Equal("StaticOnly", r.GetProperty("evidenceRequirement").GetString());
        Assert.False(r.GetProperty("hasRuntime").GetBoolean());
        Assert.True(r.TryGetProperty("staticRules", out _));

        // Evidence is present but doesn't replace existing fields
        Assert.True(r.TryGetProperty("ruleEvidence", out _));
    }

    [Fact]
    public void TextOutput_EvidenceAppearsAfterRules()
    {
        var reqDef = SimpleRequirement("scenes",
            MakeRuleDef("scene.m", "SceneExists", target: "TrainingScene"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.m", "SceneExists", RuleStatus.Passed, "Scene 'TrainingScene' exists"),
        };
        var core = MakeCoreResult(reqDef, results);
        var cli = ResultFormatter.ToCliResult(core);

        var writer = new StringWriter();
        ResultFormatter.WriteText(cli, writer);
        var output = writer.ToString();

        // Can't use Contains for Chinese characters in ASCII comparison
        // but we can check that evidence content is present
        Assert.Contains("SceneExists", output, StringComparison.Ordinal);
        Assert.Contains("TrainingScene", output, StringComparison.Ordinal);
        Assert.Contains("Scenes/TrainingScene.unity", output, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. Null &amp; edge-case handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NoStaticRules_EvidenceIsNull()
    {
        // Multiple rules but no results → staticResults.Count == 0 → null
        var reqDef = new RequirementDefinition
        {
            Id = "static-only",
            Name = "No static rules",
            EvidenceRequirement = "StaticOnly",
            StaticRules = new List<RuleDefinition>
            {
                new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "Main" },
            },
        };

        var evidence = RuleEvidenceBuilder.BuildEvidence(reqDef, new List<RuleResult>());
        Assert.Null(evidence);
    }

    [Fact]
    public void NullRequirement_EvidenceIsNull()
    {
        var evidence = RuleEvidenceBuilder.BuildEvidence(null!, new List<RuleResult>
        {
            new() { RuleId = "s1", RuleName = "S1", Status = RuleStatus.Passed, Severity = RuleSeverity.Info, Message = "ok" },
        });
        Assert.Null(evidence);
    }

    [Fact]
    public void NullStaticResults_EvidenceIsNull()
    {
        var reqDef = new RequirementDefinition
        {
            Id = "r1", Name = "R1", EvidenceRequirement = "StaticOnly",
        };
        var evidence = RuleEvidenceBuilder.BuildEvidence(reqDef, null);
        Assert.Null(evidence);
    }

    [Fact]
    public void UnknownRuleType_UsesDefaultActual()
    {
        var reqDef = SimpleRequirement("unknown",
            MakeRuleDef("u1", "BogusType", target: "SomeTarget"));
        var results = new List<RuleResult>
        {
            MakeResult("u1", "BogusType", RuleStatus.Passed, "Some unknown check passed"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("exists", cli.Actual);
        // Reason falls back to result.Message
        Assert.Equal("Some unknown check passed", cli.Reason);
        Assert.Null(cli.Source);
    }

    [Fact]
    public void UnknownRuleType_Fail_UsesDefaultActual()
    {
        var reqDef = SimpleRequirement("unknown",
            MakeRuleDef("u1", "BogusType", target: "SomeTarget"));
        var results = new List<RuleResult>
        {
            MakeResult("u1", "BogusType", RuleStatus.Failed, "Some unknown check failed"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("not found", cli.Actual);
        Assert.Equal("Some unknown check failed", cli.Reason);
    }

    [Fact]
    public void NotEvaluatedStatus_UsesNotEvaluatedActual()
    {
        var reqDef = SimpleRequirement("ne",
            MakeRuleDef("n1", "SceneExists", target: "SomeScene"));
        var results = new List<RuleResult>
        {
            MakeResult("n1", "SceneExists", RuleStatus.NotEvaluated, "Precondition not met"),
        };
        var cli = GetFirstEvidence(reqDef, results);

        Assert.Equal("not evaluated", cli.Actual);
        Assert.Equal("Precondition not met", cli.Reason);
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. Report builder — evidence mapping from CLI DTO
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ReportEvidence_MapsAllFields()
    {
        var reqDef = SimpleRequirement("files",
            MakeRuleDef("file.pm", "FileExists", target: "Assets/Scripts/PlayerMove.cs"));
        var results = new List<RuleResult>
        {
            MakeResult("file.pm", "FileExists", RuleStatus.Passed,
                "File exists: 'Assets/Scripts/PlayerMove.cs'"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        var ev = report.Requirements[0].RuleEvidence![0];
        Assert.Equal("file.pm", ev.RuleId);
        Assert.Equal("FileExists", ev.RuleType);
        Assert.Equal("PASSED", ev.Status);
        Assert.Equal("Assets/Scripts/PlayerMove.cs", ev.Target);
        Assert.Equal("exists", ev.Actual);
        Assert.Equal("Assets/Scripts/PlayerMove.cs", ev.Source);
        Assert.Null(ev.Expected);
    }

    [Fact]
    public void ReportEvidence_MultipleRules_AllPresent()
    {
        var reqDef = SimpleRequirement("multi",
            MakeRuleDef("s1", "SceneExists", target: "Main"),
            MakeRuleDef("s2", "SceneExists", target: "Level"),
            MakeRuleDef("f1", "FileExists", target: "Assets/Scripts/PlayerMove.cs"));
        var results = new List<RuleResult>
        {
            MakeResult("s1", "SceneExists", RuleStatus.Passed, "Scene 'Main' exists"),
            MakeResult("s2", "SceneExists", RuleStatus.Passed, "Scene 'Level' exists"),
            MakeResult("f1", "FileExists", RuleStatus.Passed,
                "File exists: 'Assets/Scripts/PlayerMove.cs'"),
        };
        var core = MakeCoreResult(reqDef, results);
        var report = InspectionReportBuilder.Build(core);

        var ev = report.Requirements[0].RuleEvidence;
        Assert.NotNull(ev);
        Assert.Equal(3, ev.Count);
        Assert.Equal("s1", ev[0].RuleId);
        Assert.Equal("s2", ev[1].RuleId);
        Assert.Equal("f1", ev[2].RuleId);
    }

    // ═══════════════════════════════════════════════════════════════
    // 16. Mixed PASS/FAIL evidence in one requirement
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MixedPassFail_EvidenceHasCorrectStatus()
    {
        var reqDef = SimpleRequirement("mixed",
            MakeRuleDef("scene.main", "SceneExists", target: "Main"),
            MakeRuleDef("scene.boss", "SceneExists", target: "BossLevel"));
        var results = new List<RuleResult>
        {
            MakeResult("scene.main", "SceneExists", RuleStatus.Passed, "Scene 'Main' exists"),
            MakeResult("scene.boss", "SceneExists", RuleStatus.Failed,
                "Scene 'BossLevel' not found in project"),
        };
        var core = MakeCoreResult(reqDef, results);
        var cli = ResultFormatter.ToCliResult(core);

        var ev = cli.Requirements[0].RuleEvidence;
        Assert.NotNull(ev);
        Assert.Equal(2, ev.Count);
        Assert.Equal("PASSED", ev[0].Status);
        Assert.Equal("FAILED", ev[1].Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // Private helper
    // ═══════════════════════════════════════════════════════════════

    private static CliRuleEvidence GetFirstEvidence(
        RequirementDefinition reqDef, List<RuleResult> results)
    {
        var core = MakeCoreResult(reqDef, results);
        var cli = ResultFormatter.ToCliResult(core);
        var evidence = cli.Requirements[0].RuleEvidence;
        Assert.NotNull(evidence);
        Assert.Single(evidence);
        return evidence[0];
    }
}