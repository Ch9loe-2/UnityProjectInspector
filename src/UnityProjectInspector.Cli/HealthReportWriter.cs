using System.Text;
using UnityProjectInspector.Core.Models;

namespace UnityProjectInspector.Cli;/// <summary>
/// Writes a beginner-friendly HTML "health report" for a completed inspection.
///
/// Layout (plain Chinese, aimed at teachers/students who do not read code):
///   - status banner (all passed / has failures)
///   - stat cards (scene count, object count, check count)
///   - per-requirement details; each FAILED rule evidence gets a fix hint
///     (via <see cref="ChineseResultFormatter.SuggestFix"/>)
///   - per-scene object hierarchy trees (Unity Hierarchy style, collapsible)
///
/// Pure presentation layer: only reads the CLI result DTO and the already
/// scanned <see cref="ProjectInfo"/> — no Core DTOs are modified.
/// </summary>
public static class HealthReportWriter
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
    /// Writes the health report HTML to <paramref name="path"/>.
    /// </summary>
    public static void Write(CliInspectionResult result, UnityProjectInfo projectInfo, string path)
    {
        var parentDir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        File.WriteAllText(path, BuildHtml(result, projectInfo), new UTF8Encoding(false));
    }

    private static string BuildHtml(CliInspectionResult result, UnityProjectInfo projectInfo)
    {
        var status = result.Status;
        var passed = status == "PASSED";
        var projectName = Path.GetFileName(Path.GetDirectoryName(projectInfo.RootPath)) ?? "UnityProject";
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var scenes = projectInfo.Scenes;
        var totalObjects = scenes.Sum(s => CountTreeNodes(s.GameObjects));
        var reqCount = result.Requirements.Count;

        // Banner
        var bannerText = passed ? "✅ 全部通过" : "❌ 存在未通过项";
        var bannerCls = passed ? "ok" : "bad";

        // Requirements with evidence + hints
        var reqHtml = new StringBuilder();
        foreach (var req in result.Requirements)
        {
            var reqStatus = req.Status;
            var badge = reqStatus switch
            {
                "PASSED" => "<span class=\"badge ok\">✓ 通过</span>",
                "FAILED" => "<span class=\"badge bad\">✗ 未通过</span>",
                _ => "<span class=\"badge warn\">— 未评估</span>",
            };

            var failHtml = new StringBuilder();
            if (reqStatus == "FAILED" && req.RuleEvidence != null)
            {
                foreach (var ev in req.RuleEvidence)
                {
                    if (ev.Status != "FAILED") continue;
                    var typeCn = RuleTypeToChinese.TryGetValue(ev.RuleType ?? "", out var cn) ? cn : (ev.RuleType ?? "规则");
                    var tgt = string.IsNullOrEmpty(ev.Target) ? "" : $" - {Escape(ev.Target!)}";
                    var fix = Escape(ChineseResultFormatter.SuggestFix(ev));
                    failHtml.Append(
                        $"<div class=\"rule-ev\"><div class=\"rule-ev-name\">✗ {Escape(typeCn)}{tgt}</div>" +
                        $"<div class=\"rule-ev-fix\">建议：{fix}</div></div>");
                }
            }
            var failHtmlStr = failHtml.Length > 0
                ? $"<div class=\"rule-fails\">{failHtml}</div>"
                : "";

            reqHtml.Append(
                $"<div class=\"req\"><div class=\"req-head\"><span class=\"req-name\">{Escape(req.Name)}</span>{badge}</div>" +
                $"<div class=\"req-msg\">{Escape(req.Message)}</div>{failHtmlStr}</div>");
        }

        // Scene hierarchy trees
        var scenesHtml = new StringBuilder();
        if (scenes.Count == 0)
        {
            scenesHtml.Append("<div class=\"scene-card\"><div class=\"muted\">未在 Assets 下找到任何 .unity 场景</div></div>");
        }
        else
        {
            foreach (var scene in scenes)
            {
                var tree = RenderTree(scene.GameObjects);
                scenesHtml.Append(
                    $"<div class=\"scene-card\"><div class=\"scene-title\">{Escape(scene.FilePath)}</div>" +
                    $"<div class=\"scene-path\">{Escape(scene.FilePath)}</div>{tree}</div>");
            }
        }

        var summaryMsg = result.Message ?? "";
        var html = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>Unity 工程体检报告 - {{PROJECT_NAME}}</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif;
         background: #f4f6fa; color: #1f2937; padding: 32px 16px; }
  .wrap { max-width: 860px; margin: 0 auto; }
  .banner { border-radius: 16px; padding: 28px 32px; color: #fff; margin-bottom: 20px; }
  .banner.ok { background: linear-gradient(135deg, #16a34a, #22c55e); }
  .banner.bad { background: linear-gradient(135deg, #dc2626, #ef4444); }
  .banner h1 { font-size: 26px; margin-bottom: 6px; }
  .banner p { opacity: .92; font-size: 14px; }
  .card { background: #fff; border-radius: 12px; padding: 20px 24px; margin-bottom: 16px;
          box-shadow: 0 1px 3px rgba(0,0,0,.06); }
  .card h2 { font-size: 16px; margin-bottom: 14px; color: #111827;
             border-left: 4px solid #2563eb; padding-left: 10px; }
  .req { padding: 10px 12px; border: 1px solid #e5e7eb; border-radius: 8px; margin-bottom: 8px; }
  .req-head { display: flex; justify-content: space-between; align-items: center; }
  .req-name { font-weight: 600; font-size: 14px; }
  .req-msg { color: #6b7280; font-size: 12px; margin-top: 4px; }
  .rule-fails { margin-top: 6px; border-top: 1px dashed #e5e7eb; padding-top: 6px; }
  .rule-ev { margin: 4px 0; }
  .rule-ev-name { font-size: 12px; font-weight: 600; color: #b91c1c; }
  .rule-ev-fix { font-size: 12px; color: #374151; background: #fefce8;
                 border: 1px solid #fde68a; border-radius: 6px; padding: 5px 8px;
                 margin-top: 2px; }
  .badge { font-size: 12px; padding: 3px 10px; border-radius: 999px; font-weight: 600; }
  .badge.ok { background: #dcfce7; color: #15803d; }
  .badge.bad { background: #fee2e2; color: #b91c1c; }
  .badge.warn { background: #fef9c3; color: #a16207; }
  .scene-card { border: 1px solid #e5e7eb; border-radius: 8px; padding: 12px 14px; margin-bottom: 8px; }
  .scene-title { font-weight: 600; font-size: 14px; color: #111827; }
  .scene-path { color: #9ca3af; font-size: 12px; font-family: Menlo, monospace; margin: 2px 0 8px; }
  ul.hier { list-style: none; margin: 4px 0 4px 6px; padding-left: 16px;
            border-left: 1px dashed #d1d5db; }
  ul.hier ul.hier { margin-top: 2px; margin-bottom: 2px; }
  ul.hier li { font-size: 13px; line-height: 1.9; color: #374151; padding-left: 2px; }
  ul.hier li::before { content: "▪ "; color: #60a5fa; font-size: 10px; }
  ul.hier li.has-children > details > summary { font-weight: 600; color: #111827; cursor: pointer; }
  ul.hier summary { list-style: none; }
  ul.hier summary::-webkit-details-marker { display: none; }
  ul.hier summary::before { content: "▸ "; color: #9ca3af; }
  ul.hier details[open] > summary::before { content: "▾ "; color: #60a5fa; }
  .muted { color: #9ca3af; font-size: 13px; }
  .summary { display: flex; gap: 12px; margin-bottom: 16px; flex-wrap: wrap; }
  .stat { flex: 1; min-width: 120px; background: #fff; border-radius: 12px; padding: 16px;
          text-align: center; box-shadow: 0 1px 3px rgba(0,0,0,.06); }
  .stat .num { font-size: 26px; font-weight: 700; color: #111827; }
  .stat .lbl { font-size: 12px; color: #6b7280; margin-top: 2px; }
  .foot { text-align: center; color: #9ca3af; font-size: 12px; margin-top: 20px; }
</style>
</head>
<body>
<div class="wrap">
  <div class="banner {{BANNER_CLS}}">
    <h1>{{BANNER_TEXT}}</h1>
    <p>工程：{{PROJECT_NAME}} ｜ 检查时间：{{NOW}} ｜ {{SUMMARY}}</p>
  </div>

  <div class="summary">
    <div class="stat"><div class="num">{{SCENE_COUNT}}</div><div class="lbl">场景数量</div></div>
    <div class="stat"><div class="num">{{OBJECT_COUNT}}</div><div class="lbl">场景对象</div></div>
    <div class="stat"><div class="num">{{REQ_COUNT}}</div><div class="lbl">检查项</div></div>
  </div>

  <div class="card">
    <h2>检查结果</h2>
    {{REQ_HTML}}
  </div>

  <div class="card">
    <h2>场景与对象层级（类似 Unity Hierarchy）</h2>
    {{SCENES_HTML}}
  </div>

  <div class="foot">由 UnityProjectInspector 一键检查工具生成 ｜ 本报告为自动检查结果，仅供参考</div>
</div>
</body>
</html>
""";
        return html
            .Replace("{{BANNER_CLS}}", bannerCls)
            .Replace("{{BANNER_TEXT}}", bannerText)
            .Replace("{{PROJECT_NAME}}", Escape(projectName))
            .Replace("{{NOW}}", now)
            .Replace("{{SUMMARY}}", Escape(summaryMsg))
            .Replace("{{SCENE_COUNT}}", scenes.Count.ToString())
            .Replace("{{OBJECT_COUNT}}", totalObjects.ToString())
            .Replace("{{REQ_COUNT}}", reqCount.ToString())
            .Replace("{{REQ_HTML}}", reqHtml.ToString())
            .Replace("{{SCENES_HTML}}", scenesHtml.ToString());
    }

    private static string RenderTree(IReadOnlyList<GameObjectInfo> gameObjects)
    {
        if (gameObjects.Count == 0)
        {
            return "<span class=\"muted\">（未解析到对象，或场景为空）</span>";
        }

        // children[parentFileId] = list of GameObject FileIds in scene order
        var children = new Dictionary<long, List<long>>();
        var roots = new List<long>();
        foreach (var go in gameObjects)
        {
            if (go.ParentFileId == 0 || !gameObjects.Any(g => g.FileId == go.ParentFileId))
            {
                roots.Add(go.FileId);
            }
            else
            {
                if (!children.TryGetValue(go.ParentFileId, out var list))
                {
                    list = new List<long>();
                    children[go.ParentFileId] = list;
                }
                list.Add(go.FileId);
            }
        }

        var byId = gameObjects.ToDictionary(g => g.FileId);
        return BuildTreeHtml(roots, children, byId);
    }

    private static string BuildTreeHtml(List<long> nodeIds, Dictionary<long, List<long>> children,
        Dictionary<long, GameObjectInfo> byId)
    {
        var sb = new StringBuilder();
        sb.Append("<ul class=\"hier\">");
        foreach (var id in nodeIds)
        {
            var name = byId.TryGetValue(id, out var go) ? go.Name : "GameObject";
            var kids = children.TryGetValue(id, out var list) ? list : null;
            if (kids is { Count: > 0 })
            {
                sb.Append($"<li class=\"has-children\"><details open><summary>{Escape(name)}</summary>");
                sb.Append(BuildTreeHtml(kids, children, byId));
                sb.Append("</details></li>");
            }
            else
            {
                sb.Append($"<li>{Escape(name)}</li>");
            }
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    private static int CountTreeNodes(IReadOnlyList<GameObjectInfo> gameObjects)
    {
        // Every GameObject in the scene counts once (parent/child depth is irrelevant).
        return gameObjects.Count;
    }

    private static string Escape(string text) => System.Net.WebUtility.HtmlEncode(text);
}
