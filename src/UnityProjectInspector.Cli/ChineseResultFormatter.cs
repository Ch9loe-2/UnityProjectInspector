namespace UnityProjectInspector.Cli;

/// <summary>
/// Pure CLI-level Chinese result formatter.
///
/// Maps <see cref="CliInspectionResult"/> into a Chinese-language human-readable report.
/// This is a presentation-only layer — it does NOT modify any Core DTOs or internal models.
/// It only reads the CLI-level DTO (<see cref="CliInspectionResult"/>) and writes text.
/// </summary>
public static class ChineseResultFormatter
{
    private static readonly Dictionary<string, string> RuleTypeToChinese = new()
    {
        ["SceneExists"] = "场景存在",
        ["GameObjectExists"] = "游戏对象存在",
        ["ComponentExists"] = "组件存在",
        ["GameObjectHierarchy"] = "游戏对象层级",
        ["ScriptAttached"] = "脚本挂载",
        ["FileExists"] = "文件存在",
        ["UnityEventBinding"] = "UnityEvent 绑定",
        ["CodeEvidence"] = "代码证据",
    };

    /// <summary>
    /// Maps a requirement-level status string to Chinese text.
    /// </summary>
    public static string StatusToChinese(string status) => status switch
    {
        "PASSED" => "通过",
        "FAILED" => "未通过",
        "NOT_EVALUATED" => "未评估",
        _ => status,
    };

    /// <summary>
    /// Maps a rule type string to its Chinese display name.
    /// Returns the original name if no mapping exists.
    /// </summary>
    public static string RuleTypeToChineseName(string ruleType)
    {
        return RuleTypeToChinese.TryGetValue(ruleType, out var cn) ? cn : ruleType;
    }

    /// <summary>
    /// Writes a detailed Chinese-language inspection result to the specified TextWriter.
    /// Includes: overall result, per-requirement details with Chinese status, and pass/fail statistics.
    /// </summary>
    public static void WriteDetailed(CliInspectionResult result, TextWriter writer)
    {
        writer.WriteLine("UnityProjectInspector 检查报告");
        writer.WriteLine("═══════════════════════════");
        writer.WriteLine();

        // Overall status
        var statusChinese = StatusToChinese(result.Status);
        var symbol = result.Status == "PASSED" ? "✓" : "✗";
        writer.WriteLine($"结果: {symbol} {statusChinese}");
        writer.WriteLine();

        // Per-requirement details
        if (result.Requirements.Count > 0)
        {
            writer.WriteLine("检查项:");
            foreach (var req in result.Requirements)
            {
                var reqStatus = StatusToChinese(req.Status);
                var sym = req.Status == "PASSED" ? "✓" : req.Status == "FAILED" ? "✗" : "?";
                writer.WriteLine($"  [{req.Id}]");
                writer.WriteLine($"    名称: {req.Name}");
                writer.WriteLine($"    状态: {sym} {reqStatus}");
                writer.WriteLine($"    详情: {req.Message}");

                if (req.HasRuntime)
                {
                    writer.WriteLine("    类型: 运行时检测");
                }
                else
                {
                    writer.WriteLine("    类型: 静态检测");
                }

                writer.WriteLine();
            }

            // Statistics
            var passed = result.Requirements.Count(r => r.Status == "PASSED");
            var failed = result.Requirements.Count(r => r.Status == "FAILED");
            var notEval = result.Requirements.Count(r => r.Status == "NOT_EVALUATED");

            writer.WriteLine($"统计: 通过 {passed} / 失败 {failed} / 未评估 {notEval} / 总计 {result.Requirements.Count}");
            writer.WriteLine();
        }

        writer.WriteLine(result.Message);
    }
}