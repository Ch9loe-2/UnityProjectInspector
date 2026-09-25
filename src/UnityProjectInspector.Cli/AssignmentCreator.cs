using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Interactive assignment creator for UnityProjectInspector.
///
/// Guides the user through building a StaticOnly assignment definition.
/// Prompts for assignment metadata, then lets the user add rules
/// interactively (SceneExists, GameObjectExists, ComponentExists, FileExists).
/// Saves the result as a valid JSON file that passes CliInputValidator
/// and InspectionWorkflowValidator.
///
/// No third-party dependencies. Pure System.Console + System.IO.
/// </summary>
public class AssignmentCreator
{
    private readonly List<RuleDefinition> _rules = new();

    private string _assignmentId = "my-assignment";
    private string _assignmentName = "My Assignment";
    private string? _assignmentDescription;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Runs the interactive assignment creation process.
    /// Returns the path to the saved JSON file, or null if cancelled.
    /// </summary>
    public async Task<string?> RunAsync()
    {
        Console.WriteLine("═══════════════════════════════");
        Console.WriteLine("  检查方案创建器");
        Console.WriteLine("═══════════════════════════════");
        Console.WriteLine();

        // ── Step 1: Assignment metadata ──
        Console.WriteLine("【步骤 1】检查方案基本信息");
        Console.WriteLine();

        _assignmentId = Prompt("检查方案 ID（英文标识符，例如 my-assignment）",
            defaultValue: _assignmentId);
        _assignmentName = Prompt("检查方案名称（例如 2D迷宫基础作业）",
            defaultValue: _assignmentName);
        _assignmentDescription = Prompt("检查方案描述（可选，直接回车跳过）",
            optional: true);

        if (string.IsNullOrWhiteSpace(_assignmentDescription))
        {
            _assignmentDescription = null;
        }

        Console.WriteLine();

        // ── Step 2: Add rules ──
        Console.WriteLine("【步骤 2】添加检查规则");
        Console.WriteLine("当前支持的规则类型:");
        Console.WriteLine("  1. 场景存在 (SceneExists)");
        Console.WriteLine("  2. 游戏对象存在 (GameObjectExists)");
        Console.WriteLine("  3. 组件存在 (ComponentExists)");
        Console.WriteLine("  4. 文件存在 (FileExists)");
        Console.WriteLine();

        while (true)
        {
            Console.WriteLine($"当前规则数: {_rules.Count}");
            Console.WriteLine();
            Console.WriteLine("请选择操作:");
            Console.WriteLine("  1. 添加场景规则");
            Console.WriteLine("  2. 添加游戏对象规则");
            Console.WriteLine("  3. 添加组件规则");
            Console.WriteLine("  4. 添加文件规则");
            Console.WriteLine("  5. 查看当前规则");
            Console.WriteLine("  6. 删除上一条规则");
            Console.WriteLine("  7. 完成并保存");
            Console.WriteLine();

            var choice = Prompt("请输入选项 (1-7)", defaultValue: "7");

            switch (choice)
            {
                case "1":
                case "scene":
                    AddSceneRule();
                    break;
                case "2":
                case "gameobject":
                    AddGameObjectRule();
                    break;
                case "3":
                case "component":
                    AddComponentRule();
                    break;
                case "4":
                case "file":
                    AddFileRule();
                    break;
                case "5":
                case "view":
                    ViewRules();
                    break;
                case "6":
                case "delete":
                    RemoveLastRule();
                    break;
                case "7":
                case "done":
                case "save":
                case "":
                    if (_rules.Count == 0)
                    {
                        Console.WriteLine("错误: 至少需要添加一条规则才能保存。");
                        Console.WriteLine();
                        continue;
                    }
                    return await SaveToFileAsync();
                default:
                    Console.WriteLine($"未知选项: '{choice}'。请输入 1-7。");
                    break;
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// Adds a SceneExists rule.
    /// </summary>
    private void AddSceneRule()
    {
        var sceneName = Prompt("场景名称（例如 TrainingScene，不需要 .unity 后缀）");

        var rule = new RuleDefinition
        {
            Id = $"scene.{SanitizeId(sceneName)}",
            Name = $"{sceneName} 场景存在",
            Type = "SceneExists",
            Target = sceneName,
            Severity = "Error",
        };

        _rules.Add(rule);
        Console.WriteLine($"  ✓ 已添加: 场景 {sceneName}");
    }

    /// <summary>
    /// Adds a GameObjectExists rule.
    /// </summary>
    private void AddGameObjectRule()
    {
        var sceneName = Prompt("所属场景名称（可选，直接回车跳过）", optional: true);
        var goName = Prompt("游戏对象名称（例如 Player）");

        var rule = new RuleDefinition
        {
            Id = $"go.{SanitizeId(goName)}",
            Name = string.IsNullOrEmpty(sceneName)
                ? $"{goName} 游戏对象存在"
                : $"{sceneName}/{goName} 游戏对象存在",
            Type = "GameObjectExists",
            Target = goName,
            Severity = "Error",
        };

        _rules.Add(rule);
        Console.WriteLine($"  ✓ 已添加: 游戏对象 {goName}");
    }

    /// <summary>
    /// Adds a ComponentExists rule.
    /// </summary>
    private void AddComponentRule()
    {
        var goName = Prompt("游戏对象名称（例如 Main Camera）");
        var compType = Prompt("组件类型（例如 Camera、Canvas、BoxCollider）");

        var rule = new RuleDefinition
        {
            Id = $"comp.{SanitizeId(goName)}.{SanitizeId(compType)}",
            Name = $"{goName}/{compType}",
            Type = "ComponentExists",
            Target = goName,
            ExpectedClass = compType,
            Severity = "Error",
        };

        _rules.Add(rule);
        Console.WriteLine($"  ✓ 已添加: {goName} 上的 {compType} 组件");
    }

    /// <summary>
    /// Adds a FileExists rule with relative path under project root.
    /// </summary>
    private void AddFileRule()
    {
        Console.WriteLine("文件路径应是相对于项目根目录的路径，例如:");
        Console.WriteLine("  Assets/Scripts/PlayerMove.cs");
        Console.WriteLine("  Assets/Scenes/Main.unity");
        Console.WriteLine();

        var filePath = Prompt("文件路径（例如 Assets/Scripts/PlayerController.cs）");

        var rule = new RuleDefinition
        {
            Id = $"file.{SanitizeId(filePath)}",
            Name = $"{filePath} 文件存在",
            Type = "FileExists",
            Target = filePath,
            Severity = "Error",
        };

        _rules.Add(rule);
        Console.WriteLine($"  ✓ 已添加: 文件 {filePath}");
    }

    /// <summary>
    /// Displays the current list of rules.
    /// </summary>
    private void ViewRules()
    {
        if (_rules.Count == 0)
        {
            Console.WriteLine("当前没有规则。");
            return;
        }

        Console.WriteLine($"当前规则 ({_rules.Count} 条):");
        Console.WriteLine();

        for (int i = 0; i < _rules.Count; i++)
        {
            var r = _rules[i];
            Console.WriteLine($"  [{i + 1}] {ChineseResultFormatter.RuleTypeToChineseName(r.Type)}: {r.Name}");
        }
    }

    /// <summary>
    /// Removes the most recently added rule.
    /// </summary>
    private void RemoveLastRule()
    {
        if (_rules.Count == 0)
        {
            Console.WriteLine("没有可删除的规则。");
            return;
        }

        var removed = _rules[^1];
        _rules.RemoveAt(_rules.Count - 1);
        Console.WriteLine($"  ✗ 已删除: {ChineseResultFormatter.RuleTypeToChineseName(removed.Type)}: {removed.Name}");
    }

    /// <summary>
    /// Saves the assignment definition as a JSON file.
    /// Returns the file path, or null on failure.
    /// </summary>
    private async Task<string?> SaveToFileAsync()
    {
        var assignment = new AssignmentDefinition
        {
            Id = _assignmentId,
            Name = _assignmentName,
            Description = _assignmentDescription,
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "custom-rules",
                    Name = _assignmentName,
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new List<RuleDefinition>(_rules),
                },
            },
        };

        // Validate before saving
        var cliIssues = CliInputValidator.ValidateAssignment(assignment);
        if (cliIssues.Count > 0)
        {
            Console.Error.WriteLine("错误: 检查方案验证失败:");
            foreach (var issue in cliIssues)
            {
                Console.Error.WriteLine($"  - {issue}");
            }
            return null;
        }

        var validator = new InspectionWorkflowValidator();
        var issues = validator.Validate(assignment);
        var errors = issues.Where(i => i.Severity == WorkflowIssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            Console.Error.WriteLine("错误: 检查方案验证失败:");
            foreach (var err in errors)
            {
                Console.Error.WriteLine($"  - {err.Message}");
            }
            return null;
        }

        Console.WriteLine();
        Console.WriteLine("【步骤 3】保存检查方案");
        Console.WriteLine();

        var defaultName = $"{_assignmentId}.json";
        var savePath = Prompt("保存路径（直接回车使用默认值）",
            defaultValue: defaultName);

        // Resolve tilde
        if (savePath.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            savePath = home + savePath[1..];
        }

        savePath = Path.GetFullPath(savePath);

        try
        {
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(assignment, JsonOptions);
            await File.WriteAllTextAsync(savePath, json);

            Console.WriteLine($"  ✓ 已保存: {savePath}");
            return savePath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: 保存失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Prompts the user for input with an optional default value.
    /// </summary>
    private static string Prompt(string label, string? defaultValue = null, bool optional = false)
    {
        while (true)
        {
            var prompt = defaultValue != null
                ? $"{label} [{defaultValue}]: "
                : $"{label}: ";

            Console.Write(prompt);
            var input = (Console.ReadLine() ?? "").Trim();

            if (!string.IsNullOrEmpty(input))
            {
                return input;
            }

            if (defaultValue != null)
            {
                return defaultValue;
            }

            if (optional)
            {
                return "";
            }

            Console.WriteLine("输入不能为空，请重新输入。");
        }
    }

    /// <summary>
    /// Sanitizes a string for use as a JSON rule ID.
    /// Replaces non-alphanumeric characters with hyphens.
    /// </summary>
    internal static string SanitizeId(string name)
    {
        var chars = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            chars[i] = char.IsLetterOrDigit(name[i]) ? name[i] : '-';
        }
        return new string(chars).Trim('-').ToLowerInvariant();
    }
}