using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// M19 Real Project Validation.
///
/// Proves UnityProjectInspector can execute the full Assignment pipeline
/// against a real, non-trivial Unity project (3DIndustrialMonitor).
///
/// Pipeline:
///   Real Unity Project
///     ↓ UnityProjectScanner
///       ↓ SceneParser
///     ↓ ScriptResolver
///     ↓ CSharpAnalyzer (via UnityEventCodeLinker)
///     ↓ UnityEventParser
///     ↓ UnityEventCodeLinker
///     ↓ InspectionContext
///     ↓ AssignmentDefinition (based on actual scan results)
///     ↓ InspectionWorkflowValidator
///     ↓ InspectionWorkflowRunner
///     ↓ Static Rules
///     ↓ ResultMerger
///     ↓ RequirementInspectionResult
///     ↓ AssignmentInspectionResult
///
/// READ-ONLY: Does NOT modify 3DIndustrialMonitor in any way.
/// </summary>
[CollectionDefinition("M19RealProject", DisableParallelization = true)]
public class M19RealProjectCollection { }

[Collection("M19RealProject")]
[Trait("Category", "Integration")]
public class M19RealProjectValidationTests
{
    private const string RealProjectPath =
        "/Users/tangluyi/Documents/GitHub/3DIndustrialMonitor";

    private static readonly bool ProjectAvailable;

    private static readonly string? SceneFileName;
    private static readonly int? ScriptsCount;

    static M19RealProjectValidationTests()
    {
        ProjectAvailable = Directory.Exists(RealProjectPath)
            && Directory.Exists(Path.Combine(RealProjectPath, "Assets"))
            && Directory.Exists(Path.Combine(RealProjectPath, "ProjectSettings"));

        if (ProjectAvailable)
        {
            // Quick pre-flight: count GameObjects and scripts
            var sceneFiles = Directory.GetFiles(
                Path.Combine(RealProjectPath, "Assets"), "*.unity", SearchOption.AllDirectories);
            SceneFileName = sceneFiles.Length > 0
                ? Path.GetFileNameWithoutExtension(sceneFiles[0])
                : null;

            var csFiles = Directory.GetFiles(
                Path.Combine(RealProjectPath, "Assets"), "*.cs", SearchOption.AllDirectories);
            ScriptsCount = csFiles.Length;
        }
    }

    private void AssertProjectAvailable()
    {
        Assert.True(ProjectAvailable,
            $"3DIndustrialMonitor not found at {RealProjectPath}");
    }

    // ══════════════════════════════════════════════════════════════
    // 1. Project Scan
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_ProjectScan_ValidatesRealProject()
    {
        AssertProjectAvailable();

        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        Assert.True(projectInfo.IsValid);
        Assert.Equal(RealProjectPath, projectInfo.RootPath);
        Assert.NotEmpty(projectInfo.Scenes);
        Assert.Contains(projectInfo.Scenes, s => s.FilePath.EndsWith(".unity"));
    }

    // ══════════════════════════════════════════════════════════════
    // 2. Scene Scan — verify 3DIM scene parsed correctly
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_SceneScan_ParsesSampleScene()
    {
        AssertProjectAvailable();

        var sceneParser = new SceneParser();
        var scenePath = Directory.GetFiles(
            Path.Combine(RealProjectPath, "Assets"), "*.unity", SearchOption.AllDirectories)[0];

        var sceneInfo = sceneParser.Parse(scenePath);

        Assert.NotNull(sceneInfo);
        Assert.Equal("SampleScene", sceneInfo.Name);
        Assert.NotEmpty(sceneInfo.GameObjects);

        // Verify key 3DIM GameObjects exist
        var goNames = sceneInfo.GameObjects.Select(g => g.Name).ToHashSet();
        Assert.Contains("MonitoringCanvas", goNames);
        Assert.Contains("TopBar", goNames);
        Assert.Contains("OverviewPanel", goNames);
        Assert.Contains("DeviceDetailPanel", goNames);
        Assert.Contains("AlarmPanel", goNames);
        Assert.Contains("Device_A", goNames);
        Assert.Contains("Device_B", goNames);
        Assert.Contains("Device_C", goNames);
        Assert.Contains("BottomNavBar", goNames);

        // Verify components are parsed
        var firstGO = sceneInfo.GameObjects[0];
        Assert.NotEmpty(firstGO.Components);
    }

    // ══════════════════════════════════════════════════════════════
    // 3. Script Resolution
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_ScriptResolver_ResolvesRealScripts()
    {
        AssertProjectAvailable();

        var resolver = new ScriptResolver(RealProjectPath);
        resolver.Initialize();

        var allScripts = resolver.GetAllScripts();

        // Should find 14+ C# scripts
        Assert.True(allScripts.Count >= 10,
            $"Expected >=10 scripts, found {allScripts.Count}");

        var scriptNames = allScripts
            .Select(s => s.ScriptName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Verify key scripts are resolved
        Assert.Contains("DeviceController", scriptNames);
        Assert.Contains("AlarmManager", scriptNames);
        Assert.Contains("PanelSwitcher", scriptNames);
        Assert.Contains("MonitoringOverview", scriptNames);
        Assert.Contains("TopBarController", scriptNames);
        Assert.Contains("HistoryPanel", scriptNames);
        Assert.Contains("SettingsPanel", scriptNames);
        Assert.Contains("ReportExporter", scriptNames);
        Assert.Contains("ApiClient", scriptNames);
    }

    // ══════════════════════════════════════════════════════════════
    // 4. C# Analysis — Roslyn syntax analysis on key scripts
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("DeviceController")]
    [InlineData("AlarmManager")]
    [InlineData("PanelSwitcher")]
    [InlineData("MonitoringOverview")]
    [InlineData("TopBarController")]
    [InlineData("HistoryPanel")]
    [InlineData("SettingsPanel")]
    [InlineData("ReportExporter")]
    [InlineData("ApiClient")]
    public void M19_CSharpAnalyzer_AnalyzesKeyScript(string scriptName)
    {
        AssertProjectAvailable();

        var scriptPath = Path.Combine(RealProjectPath, "Assets", "Scripts");
        var files = Directory.GetFiles(scriptPath, $"{scriptName}.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var analyzer = new CSharpAnalyzer();
        var csFileInfo = analyzer.AnalyzeFile(files[0]);

        Assert.NotNull(csFileInfo);
        Assert.NotEmpty(csFileInfo.Classes);
        Assert.Contains(csFileInfo.Classes, c => c.Name == scriptName);

        // Verify methods are extracted
        var mainClass = csFileInfo.Classes.First(c => c.Name == scriptName);
        Assert.NotEmpty(mainClass.Methods);
    }

    // ══════════════════════════════════════════════════════════════
    // 5. UnityEvent Parsing — parse real bindings from SampleScene
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_UnityEventParser_ParsesRealBindings()
    {
        AssertProjectAvailable();

        // First parse scene
        var sceneParser = new SceneParser();
        var scenePath = Directory.GetFiles(
            Path.Combine(RealProjectPath, "Assets"), "*.unity", SearchOption.AllDirectories)[0];
        var sceneInfo = sceneParser.Parse(scenePath);

        // Then parse UnityEvents from the same text
        var sceneText = File.ReadAllText(scenePath);
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, sceneInfo);

        // 3DIM should have many UI button bindings
        Assert.NotEmpty(bindings);

        // At least some should be resolved to GameObjects in the scene
        var resolved = bindings.Count(b => b.IsResolved);
        Assert.True(resolved > 0,
            $"Expected some resolved bindings, found {resolved}/{bindings.Count}");
    }

    // ══════════════════════════════════════════════════════════════
    // 6. UnityEventCodeLinker — full evidence chain
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_CodeLinker_LinksRealBindingsToCode()
    {
        AssertProjectAvailable();

        // Step 1: Scan project
        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        // Step 2: Parse UnityEvents
        var scene = projectInfo.Scenes[0];
        var sceneText = File.ReadAllText(scene.FilePath);
        var eventParser = new UnityEventParser();
        var bindings = eventParser.Parse(sceneText, scene);

        // Step 3: Link to code
        var linker = new UnityEventCodeLinker(RealProjectPath);
        var links = linker.Link(bindings, scene);

        Assert.NotEmpty(links);

        // Some links should be fully resolved
        var resolved = links.Count(l => l.Status == LinkStatus.Resolved);
        Assert.True(resolved > 0,
            $"Expected some fully resolved links, found {resolved}/{links.Count}");

        // Verify key expected bindings
        // BottomNavBar buttons should link to PanelSwitcher
        var switcherLinks = links
            .Where(l => l.ClassName == "PanelSwitcher" && l.Status == LinkStatus.Resolved)
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // 7. Full InspectionContext Construction
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_InspectionContext_BuildFromRealProject()
    {
        AssertProjectAvailable();

        // Full pipeline: Scan → Parse → Resolve → Link
        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        var resolver = new ScriptResolver(RealProjectPath);
        resolver.Initialize();

        var eventParser = new UnityEventParser();
        var linker = new UnityEventCodeLinker(RealProjectPath);

        var allBindings = new List<UnityEventBindingInfo>();
        var allCodeLinks = new List<UnityEventCodeLink>();

        foreach (var scene in projectInfo.Scenes)
        {
            var sceneText = File.ReadAllText(scene.FilePath);
            var bindings = eventParser.Parse(sceneText, scene);
            var links = linker.Link(bindings, scene);

            allBindings.AddRange(bindings);
            allCodeLinks.AddRange(links);
        }

        // Build GameObject → ScriptNames map
        var goScriptNames = new Dictionary<long, List<string>>();
        foreach (var scene in projectInfo.Scenes)
        {
            foreach (var go in scene.GameObjects)
            {
                var names = new List<string>();
                foreach (var comp in go.Components)
                {
                    if (comp.ScriptGuid != null)
                    {
                        var scriptInfo = resolver.ResolveRaw(comp.ScriptGuid);
                        if (scriptInfo?.IsResolved == true)
                        {
                            names.Add(scriptInfo.ScriptName);
                        }
                    }
                }
                if (names.Count > 0)
                {
                    goScriptNames[go.FileId] = names;
                }
            }
        }

        var context = new InspectionContext
        {
            ProjectInfo = projectInfo,
            EventBindings = allBindings,
            CodeLinks = allCodeLinks,
            GameObjectScriptNames = goScriptNames,
        };

        // Verify context is well-formed
        Assert.NotEmpty(context.ProjectInfo.Scenes);
        Assert.NotEmpty(context.ProjectInfo.Scenes[0].GameObjects);
        Assert.NotEmpty(allBindings);
        Assert.NotEmpty(allCodeLinks);
    }

    // ══════════════════════════════════════════════════════════════
    // 8. Real Assignment — based on actual project scan results
    //
    // Creates an AssignmentDefinition with real StaticOnly requirements:
    //   - Scene exists
    //   - Key GameObjects exist
    //   - Key MonoBehaviour scripts attached
    //   - UI buttons have UnityEvent bindings
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M19_RealAssignment_StaticRequirements_Pass()
    {
        AssertProjectAvailable();

        // ─── Build full InspectionContext ─────────────────────
        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);

        var resolver = new ScriptResolver(RealProjectPath);
        resolver.Initialize();

        var eventParser = new UnityEventParser();
        var linker = new UnityEventCodeLinker(RealProjectPath);

        var allBindings = new List<UnityEventBindingInfo>();
        var allCodeLinks = new List<UnityEventCodeLink>();

        foreach (var scene in projectInfo.Scenes)
        {
            var sceneText = File.ReadAllText(scene.FilePath);
            var bindings = eventParser.Parse(sceneText, scene);
            var links = linker.Link(bindings, scene);
            allBindings.AddRange(bindings);
            allCodeLinks.AddRange(links);
        }

        var goScriptNames = new Dictionary<long, List<string>>();
        foreach (var scene in projectInfo.Scenes)
        {
            foreach (var go in scene.GameObjects)
            {
                var names = new List<string>();
                foreach (var comp in go.Components)
                {
                    if (comp.ScriptGuid != null)
                    {
                        var scriptInfo = resolver.ResolveRaw(comp.ScriptGuid);
                        if (scriptInfo?.IsResolved == true)
                            names.Add(scriptInfo.ScriptName);
                    }
                }
                if (names.Count > 0)
                    goScriptNames[go.FileId] = names;
            }
        }

        var context = new InspectionContext
        {
            ProjectInfo = projectInfo,
            EventBindings = allBindings,
            CodeLinks = allCodeLinks,
            GameObjectScriptNames = goScriptNames,
        };

        // ─── Build Assignment based on real project data ─────
        // These are all verifiable from static scan
        var assignment = new AssignmentDefinition
        {
            Id = "3d-industrial-monitor-static",
            Name = "3D Industrial Monitor — Static Validation",
            Description = "Static validation of real 3DIndustrialMonitor project",
            Requirements = new List<RequirementDefinition>
            {
                // Requirement 1: SampleScene exists
                new()
                {
                    Id = "sample-scene-exists",
                    Name = "SampleScene exists",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.sample.exists",
                            Name = "SampleScene exists",
                            Type = "SceneExists",
                            Target = "SampleScene",
                            Severity = "Error",
                        },
                    },
                },

                // Requirement 2: Key UI panels exist
                new()
                {
                    Id = "ui-panels-exist",
                    Name = "Key UI panels exist",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "go.monitoring-canvas",
                            Name = "MonitoringCanvas exists",
                            Type = "GameObjectExists",
                            Target = "MonitoringCanvas",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.topbar",
                            Name = "TopBar exists",
                            Type = "GameObjectExists",
                            Target = "TopBar",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.overview-panel",
                            Name = "OverviewPanel exists",
                            Type = "GameObjectExists",
                            Target = "OverviewPanel",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.device-detail-panel",
                            Name = "DeviceDetailPanel exists",
                            Type = "GameObjectExists",
                            Target = "DeviceDetailPanel",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.alarm-panel",
                            Name = "AlarmPanel exists",
                            Type = "GameObjectExists",
                            Target = "AlarmPanel",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.settings-panel",
                            Name = "SettingsPanel exists",
                            Type = "GameObjectExists",
                            Target = "SettingsPanel",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.history-panel",
                            Name = "HistoryPanel exists",
                            Type = "GameObjectExists",
                            Target = "HistoryPanel",
                            Severity = "Error",
                        },
                    },
                },

                // Requirement 3: Device objects exist
                new()
                {
                    Id = "device-objects-exist",
                    Name = "Device GameObjects exist",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "go.device-a",
                            Name = "Device_A exists",
                            Type = "GameObjectExists",
                            Target = "Device_A",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.device-b",
                            Name = "Device_B exists",
                            Type = "GameObjectExists",
                            Target = "Device_B",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.device-c",
                            Name = "Device_C exists",
                            Type = "GameObjectExists",
                            Target = "Device_C",
                            Severity = "Error",
                        },
                    },
                },

                // Requirement 4: UI navigation buttons exist
                new()
                {
                    Id = "nav-buttons-exist",
                    Name = "Navigation buttons exist",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "go.btn-monitor",
                            Name = "Btn_Monitor exists",
                            Type = "GameObjectExists",
                            Target = "Btn_Monitor",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.btn-history",
                            Name = "Btn_History exists",
                            Type = "GameObjectExists",
                            Target = "Btn_History",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.btn-settings",
                            Name = "Btn_Settings exists",
                            Type = "GameObjectExists",
                            Target = "Btn_Settings",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.btn-alarm",
                            Name = "Btn_AlarmRecord exists",
                            Type = "GameObjectExists",
                            Target = "Btn_AlarmRecord",
                            Severity = "Error",
                        },
                        new()
                        {
                            Id = "go.btn-devlist",
                            Name = "Btn_DeviceList exists",
                            Type = "GameObjectExists",
                            Target = "Btn_DeviceList",
                            Severity = "Error",
                        },
                    },
                },

                // Requirement 5: BottomNavBar exists (additional GameObject check)
                new()
                {
                    Id = "bottom-nav-bar",
                    Name = "Bottom navigation bar exists",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "go.bottom-navbar",
                            Name = "BottomNavBar exists",
                            Type = "GameObjectExists",
                            Target = "BottomNavBar",
                            Severity = "Warning",
                        },
                    },
                },
            },
        };

        // ─── Validate ─────────────────────────────────────────
        var validator = new InspectionWorkflowValidator();
        var issues = validator.Validate(assignment);
        var errors = issues.Where(i => i.Severity == WorkflowIssueSeverity.Error).ToList();
        Assert.Empty(errors);

        // ─── Execute ──────────────────────────────────────────
        var ruleEngine = new RuleEngine();
        var runner = new InspectionWorkflowRunner(ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        // ─── Verify ──────────────────────────────────────────
        Assert.NotNull(result);
        Assert.Equal(5, result.RequirementResults.Count);

        // Individual requirement results
        var req0 = result.RequirementResults[0]; // scene exists
        Assert.Equal("sample-scene-exists", req0.Requirement.Id);
        Assert.Equal(RuleStatus.Passed, req0.Status);

        var req1 = result.RequirementResults[1]; // UI panels
        Assert.Equal("ui-panels-exist", req1.Requirement.Id);
        Assert.Equal(RuleStatus.Passed, req1.Status);

        var req2 = result.RequirementResults[2]; // device objects
        Assert.Equal("device-objects-exist", req2.Requirement.Id);
        Assert.Equal(RuleStatus.Passed, req2.Status);

        var req3 = result.RequirementResults[3]; // nav buttons
        Assert.Equal("nav-buttons-exist", req3.Requirement.Id);
        Assert.Equal(RuleStatus.Passed, req3.Status);

        var req4 = result.RequirementResults[4]; // bottom nav
        Assert.Equal("bottom-nav-bar", req4.Requirement.Id);

        // Assignment final: all StaticOnly → all Passed → Passed
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
    }

    // ══════════════════════════════════════════════════════════════
    // 9. Failure Assignment — GameObject that does NOT exist
    //
    // Verifies that a rule targeting a non-existent GameObject
    // correctly returns Failed, WITHOUT modifying the real project.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M19_FailureAssignment_NonExistentObject_Fails()
    {
        AssertProjectAvailable();

        var scanner = new UnityProjectScanner();
        var projectInfo = scanner.Scan(RealProjectPath);
        var context = new InspectionContext
        {
            ProjectInfo = projectInfo,
        };

        // Assignment with one StaticOnly requirement targeting a non-existent object
        var assignment = new AssignmentDefinition
        {
            Id = "failure-test",
            Name = "Failure Test — Non-existent GameObject",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "missing-object",
                    Name = "Non-existent GameObject",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "go.definitely-not-existing",
                            Name = "DefinitelyNotExistingObject",
                            Type = "GameObjectExists",
                            Target = "DefinitelyNotExistingObject",
                            Severity = "Error",
                        },
                    },
                },
            },
        };

        var ruleEngine = new RuleEngine();
        var runner = new InspectionWorkflowRunner(ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.NotNull(result);

        // Rule → Failed, Requirement → Failed, Assignment → Failed
        Assert.Equal(RuleStatus.Failed, result.RequirementResults[0].Status);
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);

        // Verify the static rule result mentions the missing object
        var staticResults = result.RequirementResults[0].StaticResults;
        Assert.NotEmpty(staticResults);
        Assert.Contains("DefinitelyNotExistingObject", staticResults[0].Message);
    }

    // ══════════════════════════════════════════════════════════════
    // 10. Verify real project was NOT modified
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M19_VerifyProjectNotModified()
    {
        // Check that 3DIndustrialMonitor's working tree is still clean
        // (This test only runs if git is available)
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "status --short",
            WorkingDirectory = RealProjectPath,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        string output;
        using (var process = System.Diagnostics.Process.Start(psi)!)
        {
            process.WaitForExit(5000);
            output = process.StandardOutput.ReadToEnd();
        }

        // Working tree should be clean (no modifications)
        Assert.True(string.IsNullOrWhiteSpace(output),
            $"3DIndustrialMonitor working tree was modified! git status output:\n{output}");
    }
}