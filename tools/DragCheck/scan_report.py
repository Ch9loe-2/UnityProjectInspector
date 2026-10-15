#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
UnityProjectInspector 一键检查辅助脚本
用法:
  scan_report.py gen-assignment <工程目录> <输出assignment.json>
  scan_report.py render-report <工程目录> <cli结果.json> <输出报告.html>
"""
import json
import os
import re
import sys
import datetime


def find_scenes(project):
    """扫描 Assets 下所有 .unity 场景，返回 [(场景名, 相对路径)]"""
    scenes = []
    assets_dir = os.path.join(project, "Assets")
    if not os.path.isdir(assets_dir):
        return scenes
    for root, _dirs, files in os.walk(assets_dir):
        for f in sorted(files):
            if f.lower().endswith(".unity"):
                full = os.path.join(root, f)
                rel = os.path.relpath(full, project)
                scenes.append((f[:-6], rel))
    return scenes


def gen_assignment(project, out_path):
    """自动生成检查方案：工程结构 + 全部场景"""
    scenes = find_scenes(project)
    structure_rules = [
        {"id": "file.projectversion", "name": "ProjectSettings/ProjectVersion.txt 存在",
         "type": "FileExists", "target": "ProjectSettings/ProjectVersion.txt", "severity": "Error"},
        {"id": "file.manifest", "name": "Packages/manifest.json 存在",
         "type": "FileExists", "target": "Packages/manifest.json", "severity": "Error"},
    ]
    requirements = [{
        "id": "project-structure",
        "name": "Unity 工程结构完整",
        "evidenceRequirement": "StaticOnly",
        "staticRules": structure_rules,
    }]
    if scenes:
        scene_rules = []
        for name, rel in scenes:
            scene_rules.append({"id": "scene.%s.exists" % name, "name": "场景「%s」存在" % name,
                                "type": "SceneExists", "target": name, "severity": "Error"})
            scene_rules.append({"id": "file.%s.exists" % name, "name": "场景文件 %s 存在" % rel,
                                "type": "FileExists", "target": rel, "severity": "Error"})
        requirements.append({
            "id": "scenes",
            "name": "场景文件（共 %d 个）" % len(scenes),
            "evidenceRequirement": "StaticOnly",
            "staticRules": scene_rules,
        })
    assignment = {
        "id": "auto-check",
        "name": "自动生成的一键检查方案",
        "description": "由一键检查工具自动生成：校验工程结构与场景完整性",
        "requirements": requirements,
    }
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump(assignment, fh, ensure_ascii=False, indent=2)
    print("OK assignment generated: %d scenes" % len(scenes))


def scan_object_names(project, scene_rel):
    """解析 .unity 场景中的 GameObject 对象名列表"""
    path = os.path.join(project, scene_rel)
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            text = fh.read()
    except Exception:
        return []
    names = []
    for block in re.split(r"--- !u!(\d+) &", text):
        body = block.split("\n", 1)[1] if "\n" in block else ""
        # 只处理 GameObject 块（!u!1）中带 m_Name 的
        if block and block[0].strip().isdigit() and "GameObject:" in body.split("\n")[0][:40]:
            m = re.search(r"m_Name:\s*(.+)", body)
            if m:
                nm = m.group(1).strip().strip('"')
                if nm and nm not in names:
                    names.append(nm)
    return names


def render_report(project, result_json_path, out_path):
    """把 CLI 结果 + 场景对象清单渲染成中文 HTML 体检报告"""
    with open(result_json_path, "r", encoding="utf-8") as fh:
        result = json.load(fh)

    status = result.get("status", "UNKNOWN")
    message = result.get("message", "")
    passed = status == "PASSED"
    reqs = result.get("requirements", [])
    scenes = find_scenes(project)
    scene_objs = []
    total_objs = 0
    for name, rel in scenes:
        objs = scan_object_names(project, rel)
        total_objs += len(objs)
        scene_objs.append((name, rel, objs))

    proj_name = os.path.basename(os.path.normpath(project))
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    # 检查项行
    req_rows = []
    for r in reqs:
        st = r.get("status", "UNKNOWN")
        if st == "PASSED":
            badge = '<span class="badge ok">✓ 通过</span>'
        elif st == "FAILED":
            badge = '<span class="badge bad">✗ 未通过</span>'
        else:
            badge = '<span class="badge warn">— 未评估</span>'
        req_rows.append(
            '<div class="req"><div class="req-head"><span class="req-name">%s</span>%s</div>'
            '<div class="req-msg">%s</div></div>'
            % (r.get("name", r.get("id", "?")), badge, r.get("message", ""))
        )
    req_html = "\n".join(req_rows) if req_rows else '<div class="req">无检查项</div>'

    # 场景对象清单
    scene_html = []
    if scene_objs:
        for name, rel, objs in scene_objs:
            if objs:
                chips = "".join('<span class="chip">%s</span>' % o for o in objs)
            else:
                chips = '<span class="muted">（未解析到对象，或场景为空）</span>'
            scene_html.append(
                '<div class="scene-card"><div class="scene-title">%s</div>'
                '<div class="scene-path">%s</div><div class="chips">%s</div></div>'
                % (name, rel, chips)
            )
    else:
        scene_html.append('<div class="scene-card"><div class="muted">未在 Assets 下找到任何 .unity 场景</div></div>')
    scene_html_str = "\n".join(scene_html)

    banner = "✅ 全部通过" if passed else "❌ 存在未通过项"
    banner_cls = "banner ok" if passed else "banner bad"

    html = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>Unity 工程体检报告 - %s</title>
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
  .badge { font-size: 12px; padding: 3px 10px; border-radius: 999px; font-weight: 600; }
  .badge.ok { background: #dcfce7; color: #15803d; }
  .badge.bad { background: #fee2e2; color: #b91c1c; }
  .badge.warn { background: #fef9c3; color: #a16207; }
  .scene-card { border: 1px solid #e5e7eb; border-radius: 8px; padding: 12px 14px; margin-bottom: 8px; }
  .scene-title { font-weight: 600; font-size: 14px; color: #111827; }
  .scene-path { color: #9ca3af; font-size: 12px; font-family: Menlo, monospace; margin: 2px 0 8px; }
  .chips { display: flex; flex-wrap: wrap; gap: 6px; }
  .chip { background: #eff6ff; color: #1d4ed8; border: 1px solid #bfdbfe;
          font-size: 12px; padding: 3px 10px; border-radius: 999px; }
  .muted { color: #9ca3af; font-size: 13px; }
  .foot { text-align: center; color: #9ca3af; font-size: 12px; margin-top: 20px; }
  .summary { display: flex; gap: 12px; margin-bottom: 16px; flex-wrap: wrap; }
  .stat { flex: 1; min-width: 120px; background: #fff; border-radius: 12px; padding: 16px;
          text-align: center; box-shadow: 0 1px 3px rgba(0,0,0,.06); }
  .stat .num { font-size: 26px; font-weight: 700; color: #111827; }
  .stat .lbl { font-size: 12px; color: #6b7280; margin-top: 2px; }
</style>
</head>
<body>
<div class="wrap">
  <div class="banner %s">
    <h1>%s</h1>
    <p>工程：%s ｜ 检查时间：%s ｜ %s</p>
  </div>

  <div class="summary">
    <div class="stat"><div class="num">%d</div><div class="lbl">场景数量</div></div>
    <div class="stat"><div class="num">%d</div><div class="lbl">场景对象</div></div>
    <div class="stat"><div class="num">%d</div><div class="lbl">检查项</div></div>
  </div>

  <div class="card">
    <h2>检查结果</h2>
    %s
  </div>

  <div class="card">
    <h2>场景与对象清单（工具扫描发现）</h2>
    %s
  </div>

  <div class="foot">由 UnityProjectInspector 一键检查工具生成 ｜ 本报告为自动检查结果，仅供参考</div>
</div>
</body>
</html>""" % (
        proj_name, banner_cls, banner, proj_name, now,
        ("通过" if passed else "未通过") + " ｜ " + (message if message else ""),
        len(scenes), total_objs, len(reqs), req_html, scene_html_str
    )

    with open(out_path, "w", encoding="utf-8") as fh:
        fh.write(html)
    print("OK report written: %s" % out_path)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "gen-assignment" and len(sys.argv) >= 4:
        gen_assignment(sys.argv[2], sys.argv[3])
    elif cmd == "render-report" and len(sys.argv) >= 5:
        render_report(sys.argv[2], sys.argv[3], sys.argv[4])
    else:
        print(__doc__)
        sys.exit(2)
