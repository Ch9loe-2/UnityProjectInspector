#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
UnityProjectInspector 一键检查辅助脚本
用法:
  scan_report.py gen-assignment <工程目录> <输出assignment.json>
  scan_report.py render-report <工程目录> <cli结果.json> <输出报告.html>
"""
import html as html_mod
import json
import os
import re
import sys
import datetime

RULE_TYPE_CN = {
    "SceneExists": "场景存在",
    "GameObjectExists": "游戏对象存在",
    "ComponentExists": "组件存在",
    "GameObjectHierarchy": "游戏对象层级",
    "ScriptAttached": "脚本挂载",
    "FileExists": "文件存在",
    "UnityEventBinding": "UnityEvent 绑定",
    "CodeEvidence": "代码证据",
}


def suggest_fix(ev):
    """小白向修复建议：哪错了 + 怎么改（与 CLI 中文输出文案一致）。

    ev 来自 CLI --format json 的 ruleEvidence 项：
    {ruleType, target, expected, actual, reason, ...}
    """
    rtype = ev.get("ruleType", "")
    actual = ev.get("actual", "")
    target = ev.get("target", "") or ""
    expected = ev.get("expected", "") or ""
    reason = ev.get("reason", "") or ""

    if rtype == "SceneExists" and actual == "not found":
        if "no scenes" in reason:
            return "工程里一个场景都没有。检查：场景是否已保存（Ctrl+S）到 Assets/Scenes 文件夹下。"
        return "没找到叫「%s」的场景。检查：场景文件名是否完全一致（含大小写）、是否在 Assets/Scenes 文件夹下。" % target

    if rtype == "GameObjectExists" and actual == "not found":
        return "没找到叫「%s」的对象。检查：对象名是否完全一致（注意大小写和空格）、是否真的创建了、是否在别的场景里。" % target

    if rtype == "ComponentExists" and actual == "not found":
        return "「%s」对象上没找到「%s」组件。检查：选中该对象后，在 Inspector 里用 Add Component 添加「%s」。" % (target, expected, expected)

    if rtype == "FileExists":
        if actual == "not found":
            return "文件「%s」不存在。检查：路径和文件名是否完全一致（含大小写）、文件是否真的在工程里。" % target
        if actual == "invalid":
            return "文件路径写法不对：%s。检查：assignment 里填的是工程内相对路径（如 Assets/Scripts/X.cs）。" % reason

    if rtype == "ScriptAttached":
        if actual == "not found":
            return "没找到对象「%s」。先检查对象是否存在、名字是否正确。" % target
        if actual == "wrong script":
            return "「%s」上挂的脚本不是「%s」。检查：把「%s」脚本拖到该对象的 Inspector 上（脚本文件名要和类名一致）。" % (target, expected, expected)
        if actual == "no scripts":
            return "「%s」上没挂任何脚本。检查：把「%s」脚本拖到该对象的 Inspector 上。" % (target, expected)

    if rtype == "GameObjectHierarchy":
        if actual == "not found":
            return "没找到子对象「%s」。检查子对象名字是否正确、是否真的创建了。" % target
        if actual == "no parent":
            return "「%s」现在是根对象（没有父级）。检查：在 Hierarchy 面板里把它拖到「%s」下面。" % (target, expected)
        if actual == "wrong parent":
            return "「%s」的父级不对。检查：在 Hierarchy 面板里把它拖到「%s」下面，确保层级正确。" % (target, expected)

    if rtype == "UnityEventBinding":
        if actual == "no bindings":
            return "「%s」上没有绑定任何事件。检查：在 Inspector 的 OnClick 列表里点 + 添加事件。" % target
        if actual == "method not found":
            return "「%s」的事件绑定到方法「%s」，但代码里没找到。检查：方法名是否一致、方法是否写成了 public。" % (target, expected)
        if actual == "wrong class":
            return "事件绑定的脚本类不对：%s。检查：事件是否绑定到了正确的脚本和对象上。" % reason

    if rtype == "CodeEvidence":
        if actual == "no chain":
            return "代码里没找到从「%s」到「%s」的调用。检查：脚本里是否真的调用了「%s」、方法名是否一致。" % (target, expected, expected)
        if actual == "broken":
            return "调用链断了：%s。检查：调用的目标对象/方法是否真实存在、名字是否正确。" % reason
        if actual == "call not found":
            return "代码里没找到调用「%s」。检查：方法名、调用语句是否真的写了。" % expected

    if reason:
        return "对照规则要求检查「%s」（期望：%s）：核对名称拼写和工程里的实际情况。" % (target, expected)
    return "对照规则要求检查名称、拼写和工程里的实际情况。"


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


def scan_scene_hierarchy(project, scene_rel):
    """解析 .unity 场景中的 GameObject 层级树（类似 Unity Hierarchy 面板）。

    读取 GameObject 块（!u!1）的对象名与组件列表、Transform 块（!u!4）的
    m_Father 父子指针，构建按 YAML 顺序排列的树：

      [ {"name": str, "children": [...]}, ... ]   （顶层为根对象列表，递归）

    父指针缺失 / 指向外部 Prefab 的对象按根处理，保持与 Unity 层级一致。
    """
    path = os.path.join(project, scene_rel)
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            text = fh.read()
    except Exception:
        return []
    if not text:
        return []

    go_by_id = {}      # goId -> {"name": str, "comp_ids": [..], "transform": tid 或 None}
    transforms = {}    # tid -> father tid
    parts = re.split(r"--- !u!(\d+) &(\d+)\n", text)
    # parts = [pre, type, fid, body, type, fid, body, ...]
    it = iter(parts)
    next(it, None)  # skip pre
    for type_s, fid_s, body in zip(it, it, it):
        try:
            type_no, fid = int(type_s), int(fid_s)
        except ValueError:
            continue
        if type_no == 1:  # GameObject
            m = re.search(r"m_Name:\s*(.+)", body)
            name = m.group(1).strip().strip('"') if m else ""
            comp_ids = [int(x) for x in re.findall(r"component:\s*\{fileID:\s*(\d+)\}", body)]
            go_by_id[fid] = {"name": name, "comp_ids": comp_ids, "transform": None}
        elif type_no in (4, 224):  # Transform / RectTransform（UI 对象用 224）
            m = re.search(r"m_Father:\s*\{fileID:\s*(\d+)\}", body)
            transforms[fid] = int(m.group(1)) if m else 0

    # 每个 GameObject 从组件列表里找到自己的 Transform 块 id
    for go in go_by_id.values():
        for cid in go["comp_ids"]:
            if cid in transforms:
                go["transform"] = cid
                break

    # transform id -> 拥有它的 GameObject id
    owner = {go["transform"]: gid for gid, go in go_by_id.items() if go["transform"] is not None}

    children = {gid: [] for gid in go_by_id}
    roots = []
    for gid, go in go_by_id.items():
        t = go["transform"]
        father_t = transforms.get(t, 0) if t is not None else 0
        parent_go = owner.get(father_t)
        if parent_go is not None and parent_go != gid:
            children[parent_go].append(gid)
        else:
            roots.append(gid)

    def build(gid, _seen):
        if gid in _seen:  # 防御异常循环引用
            return {"name": go_by_id[gid]["name"] or "GameObject", "children": []}
        _seen = _seen | {gid}
        node = {"name": go_by_id[gid]["name"] or "GameObject", "children": []}
        for c in children[gid]:
            node["children"].append(build(c, _seen))
        return node

    return [build(g, frozenset()) for g in roots]


def count_tree_nodes(tree):
    """统计一棵树里的节点总数（含所有层级）。"""
    return sum(1 + count_tree_nodes(n["children"]) for n in tree)


def render_tree_html(tree):
    """把层级树渲染成 Unity 风格的可折叠嵌套列表（父对象可展开/收起）。"""
    if not tree:
        return '<span class="muted">（未解析到对象，或场景为空）</span>'
    items = []
    for node in tree:
        name = html_mod.escape(node["name"])
        kids = node["children"]
        if kids:
            inner = '<details open><summary>%s</summary>%s</details>' % (
                name, render_tree_html(kids))
            items.append('<li class="has-children">%s</li>' % inner)
        else:
            items.append('<li>%s</li>' % name)
    return '<ul class="hier">%s</ul>' % "".join(items)


def render_report(project, result_json_path, out_path):
    """把 CLI 结果 + 场景对象清单渲染成中文 HTML 体检报告"""
    with open(result_json_path, "r", encoding="utf-8") as fh:
        result = json.load(fh)

    status = result.get("status", "UNKNOWN")
    message = result.get("message", "")
    passed = status == "PASSED"
    reqs = result.get("requirements", [])
    scenes = find_scenes(project)
    scene_trees = []
    total_objs = 0
    for name, rel in scenes:
        tree = scan_scene_hierarchy(project, rel)
        total_objs += count_tree_nodes(tree)
        scene_trees.append((name, rel, tree))

    proj_name = os.path.basename(os.path.normpath(project))
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    # 检查项行（失败项附带规则证据 + 小白修复建议）
    req_rows = []
    for r in reqs:
        st = r.get("status", "UNKNOWN")
        if st == "PASSED":
            badge = '<span class="badge ok">✓ 通过</span>'
        elif st == "FAILED":
            badge = '<span class="badge bad">✗ 未通过</span>'
        else:
            badge = '<span class="badge warn">— 未评估</span>'

        # 失败项：列出失败规则证据 + 修复建议
        fail_items = ""
        if st == "FAILED":
            for ev in r.get("ruleEvidence", []):
                if ev.get("status") != "FAILED":
                    continue
                rtype_cn = RULE_TYPE_CN.get(ev.get("ruleType", ""), ev.get("ruleType", "规则"))
                tgt = ev.get("target") or ""
                tgt_html = " - " + html_mod.escape(tgt) if tgt else ""
                fix = html_mod.escape(suggest_fix(ev))
                fail_items += (
                    '<div class="rule-ev"><div class="rule-ev-name">✗ %s%s</div>'
                    '<div class="rule-ev-fix">建议：%s</div></div>'
                    % (html_mod.escape(rtype_cn), tgt_html, fix)
                )
        if fail_items:
            fail_html = '<div class="rule-fails">%s</div>' % fail_items
        else:
            fail_html = ""

        req_rows.append(
            '<div class="req"><div class="req-head"><span class="req-name">%s</span>%s</div>'
            '<div class="req-msg">%s</div>%s</div>'
            % (html_mod.escape(r.get("name", r.get("id", "?"))), badge,
               html_mod.escape(r.get("message", "")), fail_html)
        )
    req_html = "\n".join(req_rows) if req_rows else '<div class="req">无检查项</div>'

    # 场景层级树（Unity Hierarchy 风格）
    scene_html = []
    if scene_trees:
        for name, rel, tree in scene_trees:
            scene_html.append(
                '<div class="scene-card"><div class="scene-title">%s</div>'
                '<div class="scene-path">%s</div>%s</div>'
                % (name, rel, render_tree_html(tree))
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
  .chips { display: flex; flex-wrap: wrap; gap: 6px; }
  .chip { background: #eff6ff; color: #1d4ed8; border: 1px solid #bfdbfe;
          font-size: 12px; padding: 3px 10px; border-radius: 999px; }
  .muted { color: #9ca3af; font-size: 13px; }
  /* 场景层级树（Unity Hierarchy 风格） */
  ul.hier { list-style: none; margin: 4px 0 4px 6px; padding-left: 16px;
            border-left: 1px dashed #d1d5db; }
  ul.hier ul.hier { margin-top: 2px; margin-bottom: 2px; }
  ul.hier li { font-size: 13px; line-height: 1.9; color: #374151;
               padding-left: 2px; }
  ul.hier li::before { content: "▪ "; color: #60a5fa; font-size: 10px; }
  ul.hier li.has-children > details > summary { font-weight: 600;
             color: #111827; cursor: pointer; }
  ul.hier summary { list-style: none; }
  ul.hier summary::-webkit-details-marker { display: none; }
  ul.hier summary::before { content: "▸ "; color: #9ca3af; }
  ul.hier details[open] > summary::before { content: "▾ "; color: #60a5fa; }
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
    <h2>场景与对象层级（类似 Unity Hierarchy）</h2>
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
