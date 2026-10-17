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
    /// Produces a beginner-friendly fix hint for a FAILED rule evidence.
    /// Plain Chinese: what is probably wrong + how to fix it in Unity.
    /// Falls back to a generic hint when the specific pattern is unknown.
    /// </summary>
    public static string SuggestFix(CliRuleEvidence ev)
    {
        var type = ev.RuleType ?? string.Empty;
        var actual = ev.Actual ?? string.Empty;
        var target = ev.Target ?? string.Empty;
        var expected = ev.Expected ?? string.Empty;

        switch (type)
        {
            case "SceneExists":
                if (actual == "not found")
                {
                    if (ev.Reason?.Contains("no scenes") == true)
                        return "工程里一个场景都没有。检查：场景是否已保存（Ctrl+S）到 Assets/Scenes 文件夹下。";
                    return $"没找到叫「{target}」的场景。检查：场景文件名是否完全一致（含大小写）、是否在 Assets/Scenes 文件夹下。";
                }
                break;

            case "GameObjectExists":
                if (actual == "not found")
                    return $"没找到叫「{target}」的对象。检查：对象名是否完全一致（注意大小写和空格）、是否真的创建了、是否在别的场景里。";
                break;

            case "ComponentExists":
                if (actual == "not found")
                    return $"「{target}」对象上没找到「{expected}」组件。检查：选中该对象后，在 Inspector 里用 Add Component 添加「{expected}」。";
                break;

            case "FileExists":
                if (actual == "not found")
                    return $"文件「{target}」不存在。检查：路径和文件名是否完全一致（含大小写）、文件是否真的在工程里。";
                if (actual == "invalid")
                    return $"文件路径写法不对：{ev.Reason}。检查：assignment 里填的是工程内相对路径（如 Assets/Scripts/X.cs）。";
                break;

            case "ScriptAttached":
                if (actual == "not found")
                    return $"没找到对象「{target}」。先检查对象是否存在、名字是否正确。";
                if (actual == "wrong script")
                    return $"「{target}」上挂的脚本不是「{expected}」。检查：把「{expected}」脚本拖到该对象的 Inspector 上（脚本文件名要和类名一致）。";
                if (actual == "no scripts")
                    return $"「{target}」上没挂任何脚本。检查：把「{expected}」脚本拖到该对象的 Inspector 上。";
                break;

            case "GameObjectHierarchy":
                if (actual == "not found")
                    return $"没找到子对象「{target}」。检查子对象名字是否正确、是否真的创建了。";
                if (actual == "no parent")
                    return $"「{target}」现在是根对象（没有父级）。检查：在 Hierarchy 面板里把它拖到「{expected}」下面。";
                if (actual == "wrong parent")
                    return $"「{target}」的父级不对。检查：在 Hierarchy 面板里把它拖到「{expected}」下面，确保层级正确。";
                break;

            case "UnityEventBinding":
                if (actual == "no bindings")
                    return $"「{target}」上没有绑定任何事件。检查：在 Inspector 的 OnClick 列表里点 + 添加事件。";
                if (actual == "method not found")
                    return $"「{target}」的事件绑定到方法「{expected}」，但代码里没找到。检查：方法名是否一致、方法是否写成了 public。";
                if (actual == "wrong class")
                    return $"事件绑定的脚本类不对：{ev.Reason}。检查：事件是否绑定到了正确的脚本和对象上。";
                break;

            case "CodeEvidence":
                if (actual == "no chain")
                    return $"代码里没找到从「{target}」到「{expected}」的调用。检查：脚本里是否真的调用了「{expected}」、方法名是否一致。";
                if (actual == "broken")
                    return $"调用链断了：{ev.Reason}。检查：调用的目标对象/方法是否真实存在、名字是否正确。";
                if (actual == "call not found")
                    return $"代码里没找到调用「{expected}」。检查：方法名、调用语句是否真的写了。";
                break;
        }

        if (!string.IsNullOrWhiteSpace(ev.Reason))
            return $"对照规则要求检查「{target}」（期望：{expected}）：核对名称拼写和工程里的实际情况。";
        return "对照规则要求检查名称、拼写和工程里的实际情况。";
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

                // Per-rule evidence (M33)
                if (req.RuleEvidence != null && req.RuleEvidence.Count > 0)
                {
                    foreach (var ev in req.RuleEvidence)
                    {
                        var evSym = ev.Status == "PASSED" ? "✓" : "✗";
                        var typeCn = RuleTypeToChineseName(ev.RuleType);
                        writer.WriteLine($"    {evSym} {typeCn}");
                        if (ev.Target != null)
                            writer.WriteLine($"      目标: {ev.Target}");
                        if (ev.Expected != null)
                            writer.WriteLine($"      期望: {ev.Expected}");
                        if (ev.Actual != null)
                        {
                            var actualCn = ev.Actual switch
                            {
                                "exists" => "存在",
                                "not found" => "不存在",
                                "not evaluated" => "未评估",
                                "script attached" => "已挂载",
                                "no scripts" => "无脚本",
                                "binding exists" => "绑定存在",
                                "evidence chain verified" => "证据链已确认",
                                _ => ev.Actual,
                            };
                            writer.WriteLine($"      结果: {actualCn}");
                        }
                        if (ev.Source != null)
                            writer.WriteLine($"      来源: {ev.Source}");
                        if (ev.Reason != null)
                            writer.WriteLine($"      原因: {ev.Reason}");
                        if (ev.Status == "FAILED")
                            writer.WriteLine($"      建议: {SuggestFix(ev)}");
                    }
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

        // Stage trace (M34)
        if (result.Stages != null && result.Stages.Count > 0)
        {
            writer.WriteLine("工作流程跟踪:");
            writer.WriteLine("──────────────");
            foreach (var stage in result.Stages)
            {
                var sym = stage.Status == "passed" ? "✓" : stage.Status == "failed" ? "✗" : "·";
                var statusCn = stage.Status switch
                {
                    "passed" => "通过",
                    "failed" => "失败",
                    "skipped" => "跳过",
                    _ => stage.Status,
                };
                writer.WriteLine($"  {sym} {stage.Name,-24} {statusCn,-6} {stage.DurationMs,6}ms");
                if (stage.Message != null)
                    writer.WriteLine($"      {stage.Message}");
            }
            writer.WriteLine();
        }

        writer.WriteLine(result.Message);
    }
}