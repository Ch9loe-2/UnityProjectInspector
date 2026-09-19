using System.Text.Json;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Rules;
using Xunit.Abstractions;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Milestone 7: Rule Configuration — JSON → RuleDefinition → RuleFactory → IRule → RuleEngine.
///
/// Tests cover:
///   A. RuleDefinition model deserialization
///   B. RuleDefinitionLoader (normal, file-not-found, invalid JSON, empty array)
///   C. RuleFactory (all 5 types, unknown type, missing required fields)
///   D. Integration: rules.json → Loader → Factory → Engine → real 3DIndustrialMonitor
/// </summary>
public class Milestone7_RuleConfigurationTests
{
    private const string RealProjectPath =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor";

    private static string ScenePath =>
        Path.Combine(RealProjectPath, "Assets/Scenes/SampleScene.unity");

    private static string TestRulesJsonPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "rules.json");

    private readonly ITestOutputHelper _output;

    public Milestone7_RuleConfigurationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ═══════════════════════════════════════════════════════════════
    // A. RuleDefinition deserialization
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuleDefinition_DeserializesSceneExists()
    {
        // Arrange
        var json = """
            {
                "id": "scene.sample.exists",
                "name": "SampleScene exists",
                "type": "SceneExists",
                "target": "SampleScene",
                "severity": "Info"
            }
            """;

        // Act
        var def = JsonSerializer.Deserialize<RuleDefinition>(json);

        // Assert
        Assert.NotNull(def);
        Assert.Equal("scene.sample.exists", def.Id);
        Assert.Equal("SampleScene exists", def.Name);
        Assert.Equal("SceneExists", def.Type);
        Assert.Equal("SampleScene", def.Target);
        Assert.Null(def.ExpectedClass);
        Assert.Null(def.ExpectedMethod);
        Assert.Equal("Info", def.Severity);
        Assert.Null(def.Message);
    }

    [Fact]
    public void RuleDefinition_DeserializesUnityEventBinding()
    {
        // Arrange
        var json = """
            {
                "id": "ui.device_list.binding",
                "name": "设备列表按钮绑定",
                "type": "UnityEventBinding",
                "target": "Btn_DeviceList",
                "expectedClass": "PanelSwitcher",
                "expectedMethod": "ShowOverview",
                "severity": "Error"
            }
            """;

        // Act
        var def = JsonSerializer.Deserialize<RuleDefinition>(json);

        // Assert
        Assert.NotNull(def);
        Assert.Equal("ui.device_list.binding", def.Id);
        Assert.Equal("UnityEventBinding", def.Type);
        Assert.Equal("Btn_DeviceList", def.Target);
        Assert.Equal("PanelSwitcher", def.ExpectedClass);
        Assert.Equal("ShowOverview", def.ExpectedMethod);
        Assert.Equal("Error", def.Severity);
    }

    [Fact]
    public void RuleDefinition_DeserializesMultipleRules()
    {
        // Arrange
        var json = """
            [
                { "id": "a", "name": "A", "type": "SceneExists", "target": "SceneA" },
                { "id": "b", "name": "B", "type": "GameObjectExists", "target": "ObjB" },
                { "id": "c", "name": "C", "type": "ComponentExists", "target": "ObjC", "expectedClass": "Canvas" }
            ]
            """;

        // Act
        var defs = JsonSerializer.Deserialize<List<RuleDefinition>>(json);

        // Assert
        Assert.NotNull(defs);
        Assert.Equal(3, defs.Count);
        Assert.Equal("a", defs[0].Id);
        Assert.Equal("SceneExists", defs[0].Type);
        Assert.Equal("SceneA", defs[0].Target);
        Assert.Equal("b", defs[1].Id);
        Assert.Equal("GameObjectExists", defs[1].Type);
        Assert.Equal("ObjB", defs[1].Target);
        Assert.Equal("c", defs[2].Id);
        Assert.Equal("ComponentExists", defs[2].Type);
        Assert.Equal("ObjC", defs[2].Target);
        Assert.Equal("Canvas", defs[2].ExpectedClass);
    }

    [Fact]
    public void RuleDefinition_OptionalFields_NullWhenMissing()
    {
        // Arrange — only required fields
        var json = """
            {
                "id": "minimal",
                "name": "Minimal",
                "type": "SceneExists"
            }
            """;

        // Act
        var def = JsonSerializer.Deserialize<RuleDefinition>(json);

        // Assert
        Assert.NotNull(def);
        Assert.Equal("minimal", def.Id);
        Assert.Null(def.Target);
        Assert.Null(def.ExpectedClass);
        Assert.Null(def.ExpectedMethod);
        Assert.Null(def.Severity);
        Assert.Null(def.Message);
    }

    [Fact]
    public void RuleDefinition_UnknownFields_Ignored()
    {
        // Arrange
        var json = """
            {
                "id": "test",
                "name": "Test",
                "type": "SceneExists",
                "target": "TestScene",
                "extraField": "should be ignored",
                "anotherExtra": 42
            }
            """;

        // Act
        var def = JsonSerializer.Deserialize<RuleDefinition>(json);

        // Assert
        Assert.NotNull(def);
        Assert.Equal("test", def.Id);
    }

    // ═══════════════════════════════════════════════════════════════
    // B. RuleDefinitionLoader
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Loader_LoadsValidJson()
    {
        // Act
        var defs = RuleDefinitionLoader.Load(TestRulesJsonPath);

        // Assert
        Assert.NotNull(defs);
        Assert.Equal(8, defs.Count);

        // Verify first entry
        Assert.Equal("scene.sample.exists", defs[0].Id);
        Assert.Equal("SampleScene", defs[0].Target);
        Assert.Equal("SceneExists", defs[0].Type);

        // Verify UnityEventBinding entry
        Assert.Equal("UnityEventBinding", defs[3].Type);
        Assert.Equal("Btn_DeviceList", defs[3].Target);
        Assert.Equal("PanelSwitcher", defs[3].ExpectedClass);
        Assert.Equal("ShowOverview", defs[3].ExpectedMethod);

        // Verify CodeEvidence entry
        Assert.Equal("CodeEvidence", defs[4].Type);
        Assert.Equal("Btn_DeviceList", defs[4].Target);
        Assert.Equal("ShowOverview", defs[4].ExpectedMethod);
    }

    [Fact]
    public void Loader_FileNotFound_Throws()
    {
        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() =>
            RuleDefinitionLoader.Load("/nonexistent/path/rules.json"));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Loader_InvalidJson_Throws()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "{ invalid json }");

        try
        {
            // Act & Assert
            var ex = Assert.Throws<JsonException>(() =>
                RuleDefinitionLoader.Load(tempFile));

            Assert.Contains("Failed to parse", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Loader_EmptyArray_ReturnsEmpty()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "[]");

        try
        {
            // Act
            var defs = RuleDefinitionLoader.Load(tempFile);

            // Assert
            Assert.NotNull(defs);
            Assert.Empty(defs);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Loader_NullLiteral_Throws()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "null");

        try
        {
            // Act & Assert
            var ex = Assert.Throws<JsonException>(() =>
                RuleDefinitionLoader.Load(tempFile));

            Assert.Contains("null", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Loader_EmptyFile_Throws()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "");

        try
        {
            // Act & Assert
            var ex = Assert.Throws<JsonException>(() =>
                RuleDefinitionLoader.Load(tempFile));

            Assert.Contains("empty", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Loader_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RuleDefinitionLoader.Load(null!));
    }

    [Fact]
    public void Loader_WhitespacePath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RuleDefinitionLoader.Load("   "));
    }

    // ═══════════════════════════════════════════════════════════════
    // C. RuleFactory
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Factory_CreatesSceneExists()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "SceneExists", Target = "SampleScene"
        };

        // Act
        var rule = RuleFactory.Create(def);

        // Assert
        Assert.IsType<SceneExistsRule>(rule);
        var typed = (SceneExistsRule)rule;
        Assert.Equal("SampleScene", typed.SceneName);
    }

    [Fact]
    public void Factory_CreatesGameObjectExists()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "GameObjectExists", Target = "MonitoringCanvas"
        };

        // Act
        var rule = RuleFactory.Create(def);

        // Assert
        Assert.IsType<GameObjectExistsRule>(rule);
    }

    [Fact]
    public void Factory_CreatesComponentExists()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "ComponentExists",
            Target = "MonitoringCanvas",
            ExpectedClass = "Canvas"
        };

        // Act
        var rule = RuleFactory.Create(def);

        // Assert
        Assert.IsType<ComponentExistsRule>(rule);
    }

    [Fact]
    public void Factory_CreatesUnityEventBinding()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "UnityEventBinding",
            Target = "Btn_DeviceList",
            ExpectedMethod = "ShowOverview",
            ExpectedClass = "PanelSwitcher"
        };

        // Act
        var rule = RuleFactory.Create(def);

        // Assert
        Assert.IsType<UnityEventBindingRule>(rule);
    }

    [Fact]
    public void Factory_CreatesCodeEvidence()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "CodeEvidence",
            Target = "Btn_DeviceList",
            ExpectedMethod = "ShowOverview"
        };

        // Act
        var rule = RuleFactory.Create(def);

        // Assert
        Assert.IsType<CodeEvidenceRule>(rule);
    }

    [Fact]
    public void Factory_UnknownType_Throws()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "NonExistentType"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleFactory.Create(def));

        Assert.Contains("NonExistentType", ex.Message);
        Assert.Contains("Supported types", ex.Message);
    }

    [Fact]
    public void Factory_NullDefinition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RuleFactory.Create(null!));
    }

    [Fact]
    public void Factory_SceneExistsMissingTarget_Throws()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "SceneExists"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleFactory.Create(def));

        Assert.Contains("target", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Factory_ComponentExistsMissingClass_Throws()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "ComponentExists",
            Target = "SomeObject"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleFactory.Create(def));

        Assert.Contains("expectedclass", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Factory_UnityEventBindingMissingMethod_Throws()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "UnityEventBinding",
            Target = "Btn_DeviceList"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleFactory.Create(def));

        Assert.Contains("expectedmethod", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Factory_CodeEvidenceMissingMethod_Throws()
    {
        // Arrange
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "CodeEvidence",
            Target = "Btn_DeviceList"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleFactory.Create(def));

        Assert.Contains("expectedmethod", ex.Message.ToLowerInvariant());
    }

    // ═══════════════════════════════════════════════════════════════
    // D. Integration: JSON → Loader → Factory → Engine → Real Project
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Integration_JsonRulesAgainstRealProject()
    {
        // ── Arrange ──

        // 1. Load JSON rules
        var definitions = RuleDefinitionLoader.Load(TestRulesJsonPath);
        Assert.NotEmpty(definitions);
        _output.WriteLine($"Loaded {definitions.Count} rule definitions from JSON");

        // 2. Convert to IRule instances via RuleFactory
        var rules = definitions.Select(RuleFactory.Create).ToList();
        Assert.Equal(definitions.Count, rules.Count);

        // 3. Build InspectionContext from real 3DIndustrialMonitor project
        var sceneText = File.ReadAllText(ScenePath);
        var sceneParser = new SceneParser();
        var scene = sceneParser.Parse(ScenePath);
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(RealProjectPath);
        var links = linker.Link(bindings, scene);
        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        // Build GameObjectScriptNames for ScriptAttachedRule support
        var resolver = new ScriptResolver(RealProjectPath);
        var scriptMap = new Dictionary<long, List<string>>();
        foreach (var go in scene.GameObjects)
        {
            var scriptNames = new List<string>();
            foreach (var comp in go.Components)
            {
                if (comp.ScriptGuid != null)
                {
                    var scriptInfo = resolver.ResolveScript(comp.ScriptGuid);
                    if (scriptInfo != null)
                    {
                        scriptNames.Add(scriptInfo.ScriptName);
                    }
                }
            }
            if (scriptNames.Count > 0)
            {
                scriptMap[go.FileId] = scriptNames;
            }
        }

        var context = new InspectionContext
        {
            ProjectInfo = projectInfo,
            EventBindings = bindings,
            CodeLinks = links,
            GameObjectScriptNames = scriptMap,
        };

        // 4. Run through RuleEngine
        var engine = new Core.Rules.RuleEngine();
        var results = engine.Run(context, rules);

        // ── Assert ──
        Assert.Equal(rules.Count, results.Count);

        _output.WriteLine("\n=== Integration Results (JSON Rules vs Real Project) ===\n");
        for (int i = 0; i < results.Count; i++)
        {
            _output.WriteLine($"  [{results[i].Status}] [{results[i].Severity}] " +
                              $"{definitions[i].Id}: {results[i].Message}");
        }

        var passed = results.Count(r => r.Status == RuleStatus.Passed);
        var failed = results.Count(r => r.Status == RuleStatus.Failed);
        var notEvaluated = results.Count(r => r.Status == RuleStatus.NotEvaluated);

        _output.WriteLine($"\n--- Summary ---");
        _output.WriteLine($"  Total: {results.Count}");
        _output.WriteLine($"  Passed: {passed}");
        _output.WriteLine($"  Failed: {failed}");
        _output.WriteLine($"  NotEvaluated: {notEvaluated}");

        // Assert specific expected results
        // Index 0: scene.sample.exists → Passed
        Assert.Equal(RuleStatus.Passed, results[0].Status);

        // Index 1: ui.monitoring_canvas.exists → Passed
        Assert.Equal(RuleStatus.Passed, results[1].Status);

        // Index 2: ui.monitoring_canvas.canvas → Passed
        Assert.Equal(RuleStatus.Passed, results[2].Status);

        // Index 3: ui.device_list.binding → Passed (class name ≠ GO name regression)
        Assert.Equal(RuleStatus.Passed, results[3].Status);

        // Index 4: code.device_list.evidence → Not NotEvaluated
        Assert.NotEqual(RuleStatus.NotEvaluated, results[4].Status);

        // Index 5: hierarchy.btn_detail.parent → Btn_DeviceList under BottomNavBar → Passed
        Assert.Equal(RuleStatus.Passed, results[5].Status);

        // Index 6: script.monitoring.panel_switcher → MonitoringCanvas has PanelSwitcher → Passed
        Assert.Equal(RuleStatus.Passed, results[6].Status);

        // Index 7: scene.sample.file_exists → SampleScene.unity file exists → Passed
        Assert.Equal(RuleStatus.Passed, results[7].Status);
    }
}