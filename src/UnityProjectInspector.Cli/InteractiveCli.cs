namespace UnityProjectInspector.Cli;

/// <summary>
/// Pure Console-based interactive CLI mode for UnityProjectInspector.
///
/// Triggered when the user runs "inspect" without sufficient arguments.
/// Provides four modes:
///   1. Quick Check — Scans Assets/Scenes/*.unity, builds a temp assignment, runs inspection.
///   2. Use Existing Assignment — Prompts for a JSON file path, validates, runs inspection.
///   3. Create Assignment — Launches AssignmentCreator to build a StaticOnly assignment interactively.
///   4. Exit
///
/// No third-party dependencies. Pure System.Console + System.IO.
/// </summary>
public static class InteractiveCli
{
    private const int MaxRetries = 3;

    /// <summary>
    /// Runs the interactive CLI loop. Returns the final exit code from the last inspection,
    /// or 0 if the user exits without running an inspection.
    /// </summary>
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("UnityProjectInspector 交互模式");
        Console.WriteLine("═══════════════════════════════");
        Console.WriteLine();

        // Step 1: Get a Unity project path
        var projectPath = await PromptProjectPathAsync();
        if (projectPath == null)
        {
            return (int)CliExitCode.InvalidInput;
        }

        // Step 2: Main menu loop
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("请选择操作:");
            Console.WriteLine("  1. 快速检查 (扫描场景并自动检查)");
            Console.WriteLine("  2. 使用已有检查方案");
            Console.WriteLine("  3. 创建检查方案");
            Console.WriteLine("  4. 退出");
            Console.WriteLine();
            Console.Write("请输入选项 (1-4): ");

            var choice = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

            switch (choice)
            {
                case "1":
                    return await QuickCheckAsync(projectPath);

                case "2":
                    return await RunExistingAssignmentAsync(projectPath);

                case "3":
                    return await CreateAndRunAssignmentAsync(projectPath);

                case "4":
                case "q":
                case "quit":
                case "exit":
                    Console.WriteLine("再见！");
                    return (int)CliExitCode.Passed;

                default:
                    Console.WriteLine($"未知选项: '{choice}'。请输入 1-4。");
                    break;
            }
        }
    }

    /// <summary>
    /// Prompts the user for a Unity project directory path.
    /// Returns null if the user cancels (empty input after max retries).
    /// </summary>
    private static async Task<string?> PromptProjectPathAsync()
    {
        Console.WriteLine("请输入 Unity 项目目录路径。");
        Console.WriteLine("提示: 项目目录应包含 Assets/, ProjectSettings/, Packages/ 子目录。");
        Console.WriteLine();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            Console.Write("项目路径: ");
            var path = (Console.ReadLine() ?? "").Trim();

            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("输入为空，请重新输入。");
                continue;
            }

            // Resolve tilde
            if (path.StartsWith("~"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                path = home + path[1..];
            }

            // Resolve relative paths
            path = Path.GetFullPath(path);

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"目录不存在: {path}");
                continue;
            }

            // Quick Unity project check
            var assetsDir = Path.Combine(path, "Assets");
            var psDir = Path.Combine(path, "ProjectSettings");
            var pkDir = Path.Combine(path, "Packages");

            if (!Directory.Exists(assetsDir) || !Directory.Exists(psDir) || !Directory.Exists(pkDir))
            {
                Console.WriteLine($"'{path}' 不是有效的 Unity 项目目录（缺少 Assets/, ProjectSettings/, Packages/）。");
                continue;
            }

            return path;
        }

        Console.WriteLine("已达到最大重试次数。退出交互模式。");
        return null;
    }

    /// <summary>
    /// Quick Check: scans Assets/Scenes/*.unity, lets user select scenes,
    /// builds a temp assignment, runs inspection with Chinese output.
    /// </summary>
    private static async Task<int> QuickCheckAsync(string projectPath)
    {
        var scenesDir = Path.Combine(projectPath, "Assets", "Scenes");

        if (!Directory.Exists(scenesDir))
        {
            Console.Error.WriteLine($"错误: 未找到场景目录 Assets/Scenes/ ({scenesDir})");
            return (int)CliExitCode.InvalidInput;
        }

        var sceneFiles = Directory.GetFiles(scenesDir, "*.unity")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(s => s)
            .ToList();

        if (sceneFiles.Count == 0)
        {
            Console.Error.WriteLine("错误: Assets/Scenes/ 目录下未发现任何 .unity 场景文件。");
            return (int)CliExitCode.InvalidInput;
        }

        Console.WriteLine();
        Console.WriteLine($"在 Assets/Scenes/ 中发现 {sceneFiles.Count} 个场景:");
        for (int i = 0; i < sceneFiles.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {sceneFiles[i]}");
        }
        Console.WriteLine();

        Console.Write("请选择要检查的场景序号（逗号分隔，例如 1,2，或输入 all 选择全部）: ");
        var input = (Console.ReadLine() ?? "").Trim();

        List<string> selectedScenes;
        if (string.IsNullOrEmpty(input) || input.ToLowerInvariant() == "all")
        {
            selectedScenes = sceneFiles;
        }
        else
        {
            selectedScenes = new List<string>();
            var parts = input.Split(',', StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part, out var idx) && idx >= 1 && idx <= sceneFiles.Count)
                {
                    selectedScenes.Add(sceneFiles[idx - 1]);
                }
                else
                {
                    Console.WriteLine($"跳过无效序号: '{part}'");
                }
            }
        }

        if (selectedScenes.Count == 0)
        {
            Console.Error.WriteLine("错误: 未选择任何场景。");
            return (int)CliExitCode.InvalidInput;
        }

        // Build a temp assignment with SceneExists rules
        var assignmentId = "quick-check-" + Guid.NewGuid().ToString("N")[..8];
        var json = SerializeQuickCheckJson(assignmentId, selectedScenes);

        // Write to temp file
        var tempDir = Path.Combine(Path.GetTempPath(), "upi-quickcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, "assignment.json");
        await File.WriteAllTextAsync(tempPath, json);

        Console.WriteLine();
        Console.WriteLine($"快速检查方案已创建: {selectedScenes.Count} 个场景规则");
        Console.WriteLine();

        // Run the existing inspection pipeline with Chinese output
        var cliArgs = new[]
        {
            "inspect",
            "--project", projectPath,
            "--assignment", tempPath,
            "--format", "chinese",
        };

        Console.WriteLine("开始执行检查...");
        Console.WriteLine();

        return await Program.Main(cliArgs);
    }

    /// <summary>
    /// Prompts for an existing assignment file path, validates it, then runs inspection.
    /// </summary>
    private static async Task<int> RunExistingAssignmentAsync(string projectPath)
    {
        Console.WriteLine();
        Console.WriteLine("请输入检查方案 JSON 文件路径:");
        Console.Write("文件路径: ");
        var assignmentPath = (Console.ReadLine() ?? "").Trim();

        if (string.IsNullOrEmpty(assignmentPath))
        {
            Console.Error.WriteLine("错误: 文件路径不能为空。");
            return (int)CliExitCode.InvalidInput;
        }

        // Resolve tilde
        if (assignmentPath.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            assignmentPath = home + assignmentPath[1..];
        }

        assignmentPath = Path.GetFullPath(assignmentPath);

        if (!File.Exists(assignmentPath))
        {
            Console.Error.WriteLine($"错误: 文件不存在: {assignmentPath}");
            return (int)CliExitCode.InvalidInput;
        }

        Console.WriteLine();
        Console.WriteLine($"使用检查方案: {assignmentPath}");
        Console.WriteLine();

        var cliArgs = new[]
        {
            "inspect",
            "--project", projectPath,
            "--assignment", assignmentPath,
            "--format", "chinese",
        };

        return await Program.Main(cliArgs);
    }

    /// <summary>
    /// Launches the interactive assignment creator, then runs the created assignment.
    /// </summary>
    private static async Task<int> CreateAndRunAssignmentAsync(string projectPath)
    {
        Console.WriteLine();
        Console.WriteLine("启动检查方案创建器...");
        Console.WriteLine();

        var creator = new AssignmentCreator();
        var assignmentPath = await creator.RunAsync();

        if (assignmentPath == null)
        {
            Console.WriteLine("检查方案创建已取消。");
            return (int)CliExitCode.Passed;
        }

        Console.WriteLine();
        Console.WriteLine($"检查方案已保存: {assignmentPath}");
        Console.WriteLine();

        var cliArgs = new[]
        {
            "inspect",
            "--project", projectPath,
            "--assignment", assignmentPath,
            "--format", "chinese",
        };

        return await Program.Main(cliArgs);
    }

    /// <summary>
    /// Sanitizes a scene/object name for use as a JSON rule ID.
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

    /// <summary>
    /// Serializes a quick-check assignment to a compact JSON string.
    /// </summary>
    private static string SerializeQuickCheckJson(string assignmentId, List<string> scenes)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"id\": \"{assignmentId}\",");
        sb.AppendLine("  \"name\": \"快速检查\",");
        sb.AppendLine("  \"requirements\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"id\": \"scene-check\",");

        sb.AppendLine("      \"name\": \"场景存在性检查\",");
        sb.AppendLine("      \"evidenceRequirement\": \"StaticOnly\",");
        sb.AppendLine("      \"staticRules\": [");

        for (int i = 0; i < scenes.Count; i++)
        {
            var name = scenes[i];
            var id = $"scene.{SanitizeId(name)}";
            sb.AppendLine("        {");
            sb.AppendLine($"          \"id\": \"{id}\",");
            sb.AppendLine($"          \"name\": \"{name} 场景存在\",");
            sb.AppendLine($"          \"type\": \"SceneExists\",");
            sb.AppendLine($"          \"target\": \"{name}\",");
            sb.AppendLine($"          \"severity\": \"Error\"");
            sb.Append(i < scenes.Count - 1 ? "        }," : "        }");
            sb.AppendLine();
        }

        sb.AppendLine("      ]");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.Append("}");

        return sb.ToString();
    }
}