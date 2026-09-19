using Xunit.Abstractions;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Validates Milestone 1-6 against the real Unity project: 3DIndustrialMonitor.
/// This is NOT a test of the project — it is a test of whether the Inspector
/// tools can correctly process a real-world Unity project.
///
/// Results are recorded via xUnit output. Failures in rules against the real
/// project are valid findings, not test failures.
/// </summary>
public class RealProjectValidationTests
{
    private const string RealProjectPath =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor";

    private static string ScenePath =>
        Path.Combine(RealProjectPath, "Assets/Scenes/SampleScene.unity");

    // ═══════════════════════════════════════════════════════════════
    // 1. Scanner
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Scanner_IdentifiesValidUnityProject()
    {
        // Act
        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        // Assert
        Assert.NotNull(projectInfo);
        Assert.True(projectInfo.IsValid,
            $"Project at {RealProjectPath} should be a valid Unity project");
        Assert.NotEmpty(projectInfo.Scenes);

        // Report
        var sceneNames = projectInfo.Scenes.Select(s => s.Name);
        _output.WriteLine($"Project Root: {RealProjectPath}");
        _output.WriteLine($"Unity Project Valid: {projectInfo.IsValid}");
        _output.WriteLine($"Scene Count: {projectInfo.Scenes.Count}");
        _output.WriteLine($"Scenes: {string.Join(", ", sceneNames)}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. SceneParser
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SceneParser_ParsesRealScene()
    {
        // Arrange
        var parser = new SceneParser();
        Assert.True(File.Exists(ScenePath), $"Scene file not found: {ScenePath}");

        // Act
        var scene = parser.Parse(ScenePath);

        // Assert
        Assert.NotNull(scene);
        Assert.Equal("SampleScene", scene.Name);
        Assert.NotEmpty(scene.GameObjects);

        _output.WriteLine($"Scene Name: {scene.Name}");
        _output.WriteLine($"GameObject Count: {scene.GameObjects.Count}");

        // List all GameObjects
        foreach (var go in scene.GameObjects)
        {
            _output.WriteLine($"  GO: {go.Name} (fileId={go.FileId}, parentId={go.ParentFileId})");
            foreach (var comp in go.Components)
            {
                _output.WriteLine($"    Component: {comp.Type} (fileId={comp.FileId})" +
                                  (comp.ScriptGuid != null ? $" guid={comp.ScriptGuid}" : ""));
            }
        }

        // Key GameObjects that should exist in a 3DIndustrialMonitor
        var expectedNames = new[]
        {
            "MonitoringCanvas", "TopBar", "OverviewPanel", "AlarmPanel",
            "DeviceDetailPanel", "SettingsPanel", "HistoryPanel",
            "Device_A", "Device_B", "Device_C",
            "WorkshopFloor", "WorkshopBackWall", "WorkshopLeftWall", "WorkshopRightWall",
            "Main Camera", "Directional Light",
        };

        _output.WriteLine("\n--- Checking expected GameObjects ---");
        foreach (var name in expectedNames)
        {
            var found = scene.GameObjects.Any(go => go.Name == name);
            _output.WriteLine($"  {name}: {(found ? "✓" : "✗ NOT FOUND")}");
        }

        // Verify parent-child hierarchy
        _output.WriteLine("\n--- Parent-Child Hierarchy ---");
        var goById = scene.GameObjects.ToDictionary(go => go.FileId);
        foreach (var go in scene.GameObjects.Where(g => g.ParentFileId != 0))
        {
            if (goById.TryGetValue(go.ParentFileId, out var parent))
            {
                _output.WriteLine($"  {parent.Name} → {go.Name}");
            }
        }

        // Check known component type coverage
        _output.WriteLine("\n--- Component Type Stats ---");
        var allCompTypes = scene.GameObjects
            .SelectMany(go => go.Components)
            .Select(c => c.Type)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var kv in allCompTypes.OrderBy(kv => kv.Key))
        {
            _output.WriteLine($"  {kv.Key}: {kv.Value}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. ScriptResolver
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScriptResolver_ResolvesRealScripts()
    {
        // Arrange
        var resolver = new ScriptResolver(RealProjectPath);

        // Act
        var allScripts = resolver.GetAllScripts();

        // Assert
        Assert.NotEmpty(allScripts);

        _output.WriteLine($"Total scripts discovered: {allScripts.Count}");
        foreach (var script in allScripts)
        {
            _output.WriteLine($"  GUID: {script.Guid} → " +
                              $"{(script.IsResolved ? script.ScriptPath : "✗ UNRESOLVED")}");
        }

        // Check key scripts specifically
        _output.WriteLine("\n--- Key Scripts ---");
        var keyScripts = new[] { "DeviceController", "AlarmManager", "PanelSwitcher",
                                  "MonitoringOverview", "TopBarController", "HistoryPanel",
                                  "SettingsPanel", "ReportExporter", "ApiClient" };

        foreach (var name in keyScripts)
        {
            var script = allScripts.FirstOrDefault(s =>
                s.ScriptName.Equals(name, StringComparison.OrdinalIgnoreCase));
            _output.WriteLine(
                $"  {name}: {(script != null ? (script.IsResolved ? "✓" : "✗ unresolved") : "✗ NOT FOUND")}" +
                (script != null ? $" (guid={script.Guid})" : ""));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. CSharpAnalyzer on real scripts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CSharpAnalyzer_AnalyzesDeviceController()
    {
        // Arrange
        var deviceControllerPath = Path.Combine(
            RealProjectPath, "Assets/Scripts/Equipment/DeviceController.cs");
        Assert.True(File.Exists(deviceControllerPath));

        var analyzer = new CSharpAnalyzer();

        // Act
        var result = analyzer.AnalyzeFile(deviceControllerPath);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Classes);

        _output.WriteLine($"File: {result.FilePath}");
        foreach (var cls in result.Classes)
        {
            _output.WriteLine($"  Class: {cls.Name}" +
                              (cls.Namespace != null ? $" (namespace: {cls.Namespace})" : ""));
            _output.WriteLine($"  Methods ({cls.Methods.Count}):");
            foreach (var method in cls.Methods)
            {
                _output.WriteLine($"    {method.Name}() — {method.Calls.Count} call(s)");
                foreach (var call in method.Calls)
                {
                    var args = call.Arguments.Count > 0
                        ? $"({string.Join(", ", call.Arguments)})"
                        : "()";
                    _output.WriteLine($"      → {call.Target}{args}");
                }
            }
        }
    }

    [Fact]
    public void CSharpAnalyzer_AnalyzesAlarmManager()
    {
        // Arrange
        var alarmManagerPath = Path.Combine(
            RealProjectPath, "Assets/Scripts/Simulation/AlarmManager.cs");
        Assert.True(File.Exists(alarmManagerPath));

        var analyzer = new CSharpAnalyzer();

        // Act
        var result = analyzer.AnalyzeFile(alarmManagerPath);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Classes);

        _output.WriteLine($"File: {result.FilePath}");
        foreach (var cls in result.Classes)
        {
            _output.WriteLine($"  Class: {cls.Name}");
            _output.WriteLine($"  Methods ({cls.Methods.Count}):");
            foreach (var method in cls.Methods)
            {
                _output.WriteLine($"    {method.Name}() — {method.Calls.Count} call(s)");
                foreach (var call in method.Calls)
                {
                    var args = call.Arguments.Count > 0
                        ? $"({string.Join(", ", call.Arguments)})"
                        : "()";
                    _output.WriteLine($"      → {call.Target}{args}");
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. UnityEventParser on real scene
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UnityEventParser_ParsesRealBindings()
    {
        // Arrange
        var sceneText = File.ReadAllText(ScenePath);
        var sceneParser = new SceneParser();
        var scene = sceneParser.Parse(ScenePath);
        var eventParser = new UnityEventParser();

        // Act
        var bindings = eventParser.Parse(sceneText, scene);

        // Assert
        _output.WriteLine($"Total UnityEvent bindings found: {bindings.Count}");

        // Build GameObject name lookup
        var goNames = new Dictionary<long, string>();
        foreach (var go in scene.GameObjects)
        {
            goNames[go.FileId] = go.Name;
        }

        int resolved = 0, unresolved = 0;
        foreach (var binding in bindings)
        {
            var srcName = goNames.GetValueOrDefault(binding.SourceGameObjectFileId, $"fileId={binding.SourceGameObjectFileId}");
            var tgtName = goNames.GetValueOrDefault(binding.TargetGameObjectFileId, $"fileId={binding.TargetGameObjectFileId}");

            _output.WriteLine(
                $"  [{binding.IsResolved}] {srcName} → {tgtName}.{binding.MethodName}" +
                $"  (targetAssembly={binding.TargetAssemblyTypeName})");

            if (binding.IsResolved) resolved++;
            else unresolved++;
        }

        _output.WriteLine($"\nResolved: {resolved}, Unresolved: {unresolved}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. UnityEventCodeLinker on real bindings
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UnityEventCodeLinker_LinksRealBindings()
    {
        // Arrange
        var sceneText = File.ReadAllText(ScenePath);
        var sceneParser = new SceneParser();
        var scene = sceneParser.Parse(ScenePath);
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(RealProjectPath);

        // Act
        var links = linker.Link(bindings, scene);

        // Assert
        _output.WriteLine($"Total code links: {links.Count}");

        var goNames = new Dictionary<long, string>();
        foreach (var go in scene.GameObjects)
        {
            goNames[go.FileId] = go.Name;
        }

        foreach (var link in links)
        {
            var srcName = goNames.GetValueOrDefault(link.SourceGameObjectFileId, $"fileId={link.SourceGameObjectFileId}");
            var tgtName = goNames.GetValueOrDefault(link.TargetGameObjectFileId, $"fileId={link.TargetGameObjectFileId}");

            _output.WriteLine(
                $"  [{link.Status}] {srcName} → {tgtName}.{link.MethodName}" +
                $"  (class={link.ClassName}, script={link.ScriptPath})");

            if (link.StatusMessage != null)
            {
                _output.WriteLine($"    StatusMessage: {link.StatusMessage}");
            }

            foreach (var call in link.Calls)
            {
                var args = call.Arguments.Count > 0
                    ? $"({string.Join(", ", call.Arguments)})"
                    : "()";
                _output.WriteLine($"    Call: {call.SourceClass}.{call.SourceMethod} → {call.FullTarget}{args}" +
                                  $"  [staticArgs={call.AreAllArgumentsStatic}]");
            }
        }

        // Report resolution statistics
        _output.WriteLine("\n--- CodeLink Status Summary ---");
        _output.WriteLine($"  Resolved: {links.Count(l => l.Status == LinkStatus.Resolved)}");
        _output.WriteLine($"  PartiallyResolved: {links.Count(l => l.Status == LinkStatus.PartiallyResolved)}");
        _output.WriteLine($"  Unresolved: {links.Count(l => l.Status == LinkStatus.Unresolved)}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. Rule Engine on real project
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuleEngine_ValidatesRealProject()
    {
        // Arrange — Build full context
        var sceneText = File.ReadAllText(ScenePath);
        var sceneParser = new SceneParser();
        var scene = sceneParser.Parse(ScenePath);
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(RealProjectPath);
        var links = linker.Link(bindings, scene);

        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        var context = new InspectionContext
        {
            ProjectInfo = projectInfo,
            EventBindings = bindings,
            CodeLinks = links,
        };

        // Build rules based on what we know exists in this real project
        var rules = new List<IRule>
        {
            // Scene exists
            new SceneExistsRule("SampleScene"),
            new SceneExistsRule("NonExistentScene"),

            // GameObjects that exist
            new GameObjectExistsRule("MonitoringCanvas"),
            new GameObjectExistsRule("AlarmPanel"),
            new GameObjectExistsRule("DeviceDetailPanel"),
            new GameObjectExistsRule("Device_A"),
            new GameObjectExistsRule("Device_B"),
            new GameObjectExistsRule("Device_C"),
            new GameObjectExistsRule("TopBar"),
            new GameObjectExistsRule("OverviewPanel"),
            new GameObjectExistsRule("SettingsPanel"),
            new GameObjectExistsRule("HistoryPanel"),

            // GameObjects that DON'T exist
            new GameObjectExistsRule("NonExistentObject"),

            // Component checks (uses SceneParser known types)
            new ComponentExistsRule("Main Camera", "Camera"),
            new ComponentExistsRule("Directional Light", "Light"),
            new ComponentExistsRule("Main Camera", "Transform"),

            // Component checks that may fail (unknown types in SceneParser)
            new ComponentExistsRule("MonitoringCanvas", "Canvas"),
            new ComponentExistsRule("MonitoringCanvas", "RectTransform"),
        };

        var engine = new Core.Rules.RuleEngine();

        // Act
        var results = engine.Run(context, rules);

        // Assert — just ensure all rules ran without exceptions
        Assert.Equal(rules.Count, results.Count);

        _output.WriteLine($"\n=== Rule Engine Results ({results.Count} rules) ===\n");
        foreach (var result in results)
        {
            _output.WriteLine($"  [{result.Status}] [{result.Severity}] {result.RuleId}: {result.Message}");
        }

        // Summary statistics
        _output.WriteLine("\n--- Summary ---");
        _output.WriteLine($"  Passed: {results.Count(r => r.Status == RuleStatus.Passed)}");
        _output.WriteLine($"  Failed: {results.Count(r => r.Status == RuleStatus.Failed)}");
        _output.WriteLine($"  NotEvaluated: {results.Count(r => r.Status == RuleStatus.NotEvaluated)}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. UnityEventBindingRules + CodeEvidenceRules on real bindings
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuleEngine_UnityEventAndCodeEvidenceRules()
    {
        // Arrange — Build full context
        var sceneText = File.ReadAllText(ScenePath);
        var sceneParser = new SceneParser();
        var scene = sceneParser.Parse(ScenePath);
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);
        var linker = new UnityEventCodeLinker(RealProjectPath);
        var links = linker.Link(bindings, scene);

        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        var context = new InspectionContext
        {
            ProjectInfo = projectInfo,
            EventBindings = bindings,
            CodeLinks = links,
        };

        // Identify bindings from the parser output
        _output.WriteLine("=== Discovered UnityEvent Bindings ===\n");
        var goNames = new Dictionary<long, string>();
        foreach (var go in scene.GameObjects) goNames[go.FileId] = go.Name;

        // Group bindings by source GameObject for display
        foreach (var binding in bindings)
        {
            var srcName = goNames.GetValueOrDefault(binding.SourceGameObjectFileId, $"fileId={binding.SourceGameObjectFileId}");
            var tgtName = goNames.GetValueOrDefault(binding.TargetGameObjectFileId, $"fileId={binding.TargetGameObjectFileId}");
            _output.WriteLine($"  {srcName} → {tgtName}.{binding.MethodName}");
        }

        // Build rules from actual bindings
        var rules = new List<IRule>();
        foreach (var binding in bindings)
        {
            var srcName = goNames.GetValueOrDefault(binding.SourceGameObjectFileId, $"f{binding.SourceGameObjectFileId}");

            // Check if the source GameObject has a known name
            if (goNames.ContainsKey(binding.SourceGameObjectFileId))
            {
                rules.Add(new UnityEventBindingRule(
                    srcName, binding.MethodName, binding.TargetAssemblyTypeName?.Split(',')[0]?.Trim()));

                rules.Add(new CodeEvidenceRule(
                    srcName, binding.MethodName));
            }
        }

        var engine = new Core.Rules.RuleEngine();

        // Act
        var results = engine.Run(context, rules);

        // Assert
        Assert.Equal(rules.Count, results.Count);

        _output.WriteLine($"\n=== UnityEventBinding & CodeEvidence Rules ({results.Count}) ===\n");
        foreach (var result in results)
        {
            _output.WriteLine($"  [{result.Status}] [{result.Severity}] {result.RuleId}: {result.Message}");
        }

        _output.WriteLine("\n--- Summary ---");
        _output.WriteLine($"  Passed: {results.Count(r => r.Status == RuleStatus.Passed)}");
        _output.WriteLine($"  Failed: {results.Count(r => r.Status == RuleStatus.Failed)}");
        _output.WriteLine($"  NotEvaluated: {results.Count(r => r.Status == RuleStatus.NotEvaluated)}");
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. ScriptResolver: spot-check .meta resolution
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScriptResolver_SpotCheckKeyScripts()
    {
        // Arrange
        var resolver = new ScriptResolver(RealProjectPath);

        // Read .meta files to get GUIDs for DeviceController and AlarmManager
        var deviceMetaPath = Path.Combine(RealProjectPath,
            "Assets/Scripts/Equipment/DeviceController.cs.meta");
        var alarmMetaPath = Path.Combine(RealProjectPath,
            "Assets/Scripts/Simulation/AlarmManager.cs.meta");

        Assert.True(File.Exists(deviceMetaPath));
        Assert.True(File.Exists(alarmMetaPath));

        var deviceMetaLines = File.ReadLines(deviceMetaPath).Take(20).ToArray();
        var alarmMetaLines = File.ReadLines(alarmMetaPath).Take(20).ToArray();

        // Extract GUIDs from .meta files
        var deviceGuid = ExtractGuid(deviceMetaLines);
        var alarmGuid = ExtractGuid(alarmMetaLines);

        _output.WriteLine($"DeviceController guid: {deviceGuid}");
        _output.WriteLine($"AlarmManager guid: {alarmGuid}");

        Assert.NotNull(deviceGuid);
        Assert.NotNull(alarmGuid);

        // Resolve via ScriptResolver
        var deviceScript = resolver.ResolveScript(deviceGuid);
        var alarmScript = resolver.ResolveScript(alarmGuid);

        _output.WriteLine($"\nDeviceController:");
        _output.WriteLine($"  Resolved: {deviceScript?.IsResolved}");
        _output.WriteLine($"  ScriptPath: {deviceScript?.ScriptPath}");

        _output.WriteLine($"\nAlarmManager:");
        _output.WriteLine($"  Resolved: {alarmScript?.IsResolved}");
        _output.WriteLine($"  ScriptPath: {alarmScript?.ScriptPath}");

        Assert.NotNull(deviceScript);
        Assert.True(deviceScript.IsResolved);
        Assert.NotNull(alarmScript);
        Assert.True(alarmScript.IsResolved);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static string? ExtractGuid(string[] lines)
    {
        foreach (var line in lines)
        {
            // .meta GUID format: "guid: abcdef1234567890abcdef1234567890"
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && line[..colonIdx].Trim() == "guid")
            {
                return line[(colonIdx + 1)..].Trim();
            }
        }
        return null;
    }

    private readonly ITestOutputHelper _output;

    public RealProjectValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }
}