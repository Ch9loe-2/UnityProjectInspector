using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Rules;
using Xunit.Abstractions;

namespace UnityProjectInspector.Tests;

/// <summary>
/// Milestone 8: Expanded Rule Capabilities.
///
/// Tests for:
///   - GameObjectHierarchyRule (D. GameObject under specified parent)
///   - ScriptAttachedRule (E. GameObject has specified script)
///   - FileExistsRule (F. Project file exists)
///
/// All rules consume existing InspectionContext data — no re-parsing.
/// </summary>
public class Milestone8_RuleCapabilityTests
{
    private readonly ITestOutputHelper _output;

    public Milestone8_RuleCapabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper: Build a minimal context with hierarchy
    // ═══════════════════════════════════════════════════════════════

    private static InspectionContext MakeHierarchyContext()
    {
        return new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "MainScene",
                        FilePath = "/test/Scenes/MainScene.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new() { FileId = 100, Name = "Canvas", ParentFileId = 0 },
                            new() { FileId = 200, Name = "StartButton", ParentFileId = 100 },
                            new() { FileId = 300, Name = "QuitButton", ParentFileId = 100 },
                            new() { FileId = 400, Name = "Panel", ParentFileId = 0 },
                            new() { FileId = 500, Name = "ChildOfPanel", ParentFileId = 400 },
                        },
                    },
                },
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. GameObjectHierarchyRule
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Hierarchy_CorrectParent_Passes()
    {
        var context = MakeHierarchyContext();
        var rule = new GameObjectHierarchyRule("StartButton", "Canvas");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Passed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void Hierarchy_WrongParent_Fails()
    {
        var context = MakeHierarchyContext();
        var rule = new GameObjectHierarchyRule("StartButton", "Panel");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void Hierarchy_ChildNotFound_Fails()
    {
        var context = MakeHierarchyContext();
        var rule = new GameObjectHierarchyRule("NonExistentChild", "Canvas");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    [Fact]
    public void Hierarchy_ChildIsRoot_Fails()
    {
        var context = MakeHierarchyContext();
        var rule = new GameObjectHierarchyRule("Canvas", "SomeParent");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void Hierarchy_SceneNotFound_NotEvaluated()
    {
        var context = MakeHierarchyContext();
        var rule = new GameObjectHierarchyRule("StartButton", "Canvas", "NonExistentScene");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.NotEvaluated, result.Status);
    }

    [Fact]
    public void Hierarchy_MultipleScenes_FindsCorrectParent()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "SceneA",
                        FilePath = "/test/Scenes/SceneA.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new() { FileId = 100, Name = "Player", ParentFileId = 0 },
                            new() { FileId = 200, Name = "HUD", ParentFileId = 100 },
                        },
                    },
                    new()
                    {
                        Name = "SceneB",
                        FilePath = "/test/Scenes/SceneB.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new() { FileId = 300, Name = "Player", ParentFileId = 0 },
                            new() { FileId = 400, Name = "HUD", ParentFileId = 300 },
                        },
                    },
                },
            },
        };

        var rule = new GameObjectHierarchyRule("HUD", "Player");

        var result = rule.Evaluate(context);

        // Should find the first match — could pass or fail depending on which
        // scene's "HUD" is found first. With FirstOrDefault across all scenes,
        // it finds SceneA's HUD (parent=100=Player in SceneA).
        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. ScriptAttachedRule
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScriptAttached_ScriptFoundViaMap_Passes()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "MainScene",
                        FilePath = "/test/Scenes/MainScene.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new()
                            {
                                FileId = 100,
                                Name = "MonitoringCanvas",
                                Components = new List<ComponentInfo>
                                {
                                    new() { Type = "MonoBehaviour", FileId = 101, ScriptGuid = "guid_panel" },
                                    new() { Type = "Canvas", FileId = 102 },
                                },
                            },
                        },
                    },
                },
            },
            GameObjectScriptNames = new Dictionary<long, List<string>>
            {
                { 100, new List<string> { "PanelSwitcher" } },
            },
        };

        var rule = new ScriptAttachedRule("MonitoringCanvas", "PanelSwitcher");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Passed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void ScriptAttached_WrongScriptName_Fails()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "MainScene",
                        FilePath = "/test/Scenes/MainScene.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new()
                            {
                                FileId = 100,
                                Name = "MonitoringCanvas",
                                Components = new List<ComponentInfo>
                                {
                                    new() { Type = "MonoBehaviour", FileId = 101, ScriptGuid = "guid_panel" },
                                },
                            },
                        },
                    },
                },
            },
            GameObjectScriptNames = new Dictionary<long, List<string>>
            {
                { 100, new List<string> { "PanelSwitcher" } },
            },
        };

        var rule = new ScriptAttachedRule("MonitoringCanvas", "DeviceController");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void ScriptAttached_GameObjectNotFound_Fails()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "MainScene",
                        FilePath = "/test/Scenes/MainScene.unity",
                        GameObjects = new List<GameObjectInfo>(),
                    },
                },
            },
        };

        var rule = new ScriptAttachedRule("NonExistent", "SomeScript");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    [Fact]
    public void ScriptAttached_NoScriptMap_NotEvaluated()
    {
        // GameObjectScriptNames is empty — the rule falls back to NotEvaluated
        // because it can't verify the specific script class name
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "MainScene",
                        FilePath = "/test/Scenes/MainScene.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new()
                            {
                                FileId = 100,
                                Name = "MonitoringCanvas",
                                Components = new List<ComponentInfo>
                                {
                                    new() { Type = "MonoBehaviour", FileId = 101, ScriptGuid = "some_guid" },
                                },
                            },
                        },
                    },
                },
            },
        };

        var rule = new ScriptAttachedRule("MonitoringCanvas", "PanelSwitcher");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.NotEvaluated, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void ScriptAttached_NoMonoBehaviour_Fails()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test",
                IsValid = true,
                Scenes = new List<SceneInfo>
                {
                    new()
                    {
                        Name = "MainScene",
                        FilePath = "/test/Scenes/MainScene.unity",
                        GameObjects = new List<GameObjectInfo>
                        {
                            new()
                            {
                                FileId = 100,
                                Name = "PlainObject",
                                Components = new List<ComponentInfo>
                                {
                                    new() { Type = "Transform", FileId = 101 },
                                    new() { Type = "MeshRenderer", FileId = 102 },
                                },
                            },
                        },
                    },
                },
            },
        };

        var rule = new ScriptAttachedRule("PlainObject", "SomeScript");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. FileExistsRule
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FileExists_FileExists_Passes()
    {
        using var tempDir = new TempDirectory();
        var testFilePath = Path.Combine(tempDir.Path, "test.txt");
        File.WriteAllText(testFilePath, "content");

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = tempDir.Path,
                IsValid = true,
            },
        };

        var rule = new FileExistsRule("test.txt");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Passed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void FileExists_FileNotFound_Fails()
    {
        using var tempDir = new TempDirectory();

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = tempDir.Path,
                IsValid = true,
            },
        };

        var rule = new FileExistsRule("nonexistent.txt");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
    }

    [Fact]
    public void FileExists_AbsolutePath_Rejected()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test/project",
                IsValid = true,
            },
        };

        var rule = new FileExistsRule("/etc/passwd");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void FileExists_PathTraversal_Rejected()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/test/project",
                IsValid = true,
            },
        };

        var rule = new FileExistsRule("../../etc/passwd");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Failed, result.Status);
        _output.WriteLine(result.Message);
    }

    [Fact]
    public void FileExists_Subdirectory_Passes()
    {
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets/Scenes"));
        var scenePath = Path.Combine(tempDir.Path, "Assets/Scenes/Test.unity");
        File.WriteAllText(scenePath, "scene data");

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = tempDir.Path,
                IsValid = true,
            },
        };

        var rule = new FileExistsRule("Assets/Scenes/Test.unity");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    [Fact]
    public void FileExists_Directory_Passes()
    {
        using var tempDir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir.Path, "Assets/Scripts"));

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = tempDir.Path,
                IsValid = true,
            },
        };

        var rule = new FileExistsRule("Assets/Scripts");

        var result = rule.Evaluate(context);

        Assert.Equal(RuleStatus.Passed, result.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. RuleFactory: New types
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Factory_CreatesGameObjectHierarchy()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "GameObjectHierarchy",
            Target = "Btn_DeviceList", ExpectedParent = "BottomNavBar"
        };

        var rule = RuleFactory.Create(def);

        Assert.IsType<GameObjectHierarchyRule>(rule);
    }

    [Fact]
    public void Factory_GameObjectHierarchyMissingParent_Throws()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "GameObjectHierarchy", Target = "Btn_DeviceList"
        };

        var ex = Assert.Throws<ArgumentException>(() => RuleFactory.Create(def));
        Assert.Contains("expectedparent", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Factory_CreatesScriptAttached()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "ScriptAttached",
            Target = "MonitoringCanvas", ExpectedClass = "PanelSwitcher"
        };

        var rule = RuleFactory.Create(def);

        Assert.IsType<ScriptAttachedRule>(rule);
    }

    [Fact]
    public void Factory_ScriptAttachedMissingClass_Throws()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test",
            Type = "ScriptAttached", Target = "MonitoringCanvas"
        };

        var ex = Assert.Throws<ArgumentException>(() => RuleFactory.Create(def));
        Assert.Contains("expectedclass", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Factory_CreatesFileExists()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "FileExists",
            Target = "Assets/Scenes/SampleScene.unity"
        };

        var rule = RuleFactory.Create(def);

        Assert.IsType<FileExistsRule>(rule);
    }

    [Fact]
    public void Factory_FileExistsMissingTarget_Throws()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "FileExists"
        };

        var ex = Assert.Throws<ArgumentException>(() => RuleFactory.Create(def));
        Assert.Contains("target", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void Factory_UnknownType_StillThrowsWithUpdatedMessage()
    {
        var def = new RuleDefinition
        {
            Id = "test", Name = "Test", Type = "TotallyUnknown"
        };

        var ex = Assert.Throws<ArgumentException>(() => RuleFactory.Create(def));
        Assert.Contains("TotallyUnknown", ex.Message);
        Assert.Contains("GameObjectHierarchy", ex.Message);
        Assert.Contains("ScriptAttached", ex.Message);
        Assert.Contains("FileExists", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. RuleDefinition Deserialization — New Fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuleDefinition_DeserializesExpectedParent()
    {
        var json = """
            {
                "id": "hierarchy.test",
                "name": "Test hierarchy",
                "type": "GameObjectHierarchy",
                "target": "Child",
                "expectedParent": "Parent",
                "severity": "Error"
            }
            """;

        var def = System.Text.Json.JsonSerializer.Deserialize<RuleDefinition>(json);

        Assert.NotNull(def);
        Assert.Equal("Child", def.Target);
        Assert.Equal("Parent", def.ExpectedParent);
    }
}

/// <summary>
/// Temp directory that auto-cleans up via IDisposable.
/// Copied from existing test patterns.
/// </summary>
internal class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch { /* best-effort cleanup */ }
    }
}