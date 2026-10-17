#!/bin/bash
# ============================================================
# UnityProjectInspector 一键检查脚本（小白版）
#
# 输入：Unity 工程文件夹 或 工程 zip 压缩包
# 流程：自动解压 → 自动校验 → 自动生成检查方案（CLI 内建）
#       → 自动检查（CLI 内建健康报告）→ 自动弹出 HTML 体检报告
# 说明：报告生成已移入 CLI 自身，不再依赖 python3。
# ============================================================

REPO_DIR="/Users/tangluyi/Documents/GitHub/UnityProjectInspector"
CLI_DIR="$REPO_DIR/src/UnityProjectInspector.Cli"
TOOL_DIR="$(cd "$(dirname "$0")" && pwd)"
DOTNET="/usr/local/share/dotnet/dotnet"
if [ ! -x "$DOTNET" ]; then DOTNET="$(command -v dotnet)"; fi

# 独立 CLI（发布产物）：别人拿到 .app 不需要安装 dotnet 也不需要仓库。
# 放在 Contents/Resources/cli-publish/，存在则优先使用。
EMBEDDED_CLI="$TOOL_DIR/cli-publish/unityprojectinspector"
if [ -x "$EMBEDDED_CLI" ]; then
  CLI_CMD=("$EMBEDDED_CLI")
else
  # 开发模式：使用仓库源码（需要本机 dotnet）
  CLI_CMD=("$DOTNET" run --no-build --project "$CLI_DIR")
fi

INPUT="$1"
if [ -z "$INPUT" ]; then
  echo "用法：把 Unity 工程文件夹或 .zip 压缩包拖到这个应用/脚本上"
  exit 2
fi

WORKDIR="$(mktemp -d)"
cleanup() { rm -rf "$WORKDIR"; }
trap cleanup EXIT

PROJECT=""

# ---------- 1. 解压 / 定位工程目录 ----------
if [ -f "$INPUT" ]; then
  case "$INPUT" in
    *.zip|*.ZIP)
      if ! unzip -q "$INPUT" -d "$WORKDIR" 2>/dev/null; then
        echo "❌ zip 解压失败：$INPUT"
        exit 2
      fi
      # 兼容 zip 内多套一层目录的情况
      for d in "$WORKDIR"/*/; do
        if [ -d "$d/Assets" ] && [ -d "$d/ProjectSettings" ] && [ -d "$d/Packages" ]; then
          PROJECT="${d%/}"
          break
        fi
      done
      if [ -z "$PROJECT" ] && [ -d "$WORKDIR/Assets" ]; then
        PROJECT="$WORKDIR"
      fi
      ;;
    *)
      echo "❌ 只支持 .zip 压缩包或 Unity 工程文件夹"
      exit 2
      ;;
  esac
else
  PROJECT="$INPUT"
fi

# ---------- 2. 校验是否是有效 Unity 工程 ----------
if [ -z "$PROJECT" ] || \
   [ ! -d "$PROJECT/Assets" ] || \
   [ ! -d "$PROJECT/ProjectSettings" ] || \
   [ ! -d "$PROJECT/Packages" ]; then
  echo "❌ 这不是一个有效的 Unity 工程（缺少 Assets / ProjectSettings / Packages）"
  exit 2
fi

# ---------- 3. CLI 自动生成检查方案 ----------
ASSIGNMENT="$WORKDIR/auto-assignment.json"
"${CLI_CMD[@]}" inspect \
  --project "$PROJECT" \
  --generate-assignment "$ASSIGNMENT" > "$WORKDIR/gen.out" 2>&1
GEN_EXIT=$?
if [ "$GEN_EXIT" -ne 0 ] || [ ! -s "$ASSIGNMENT" ]; then
  echo "❌ 自动生成检查方案失败"
  head -5 "$WORKDIR/gen.out" 2>/dev/null
  exit 2
fi

# ---------- 4. 运行 CLI 静态检查，内建生成健康报告 ----------
REPORT="/tmp/Unity工程体检报告_$(date +%H%M%S).html"
"${CLI_CMD[@]}" inspect \
  --project "$PROJECT" \
  --assignment "$ASSIGNMENT" \
  --health-report "$REPORT" \
  --format chinese > "$WORKDIR/cli.out" 2>"$WORKDIR/cli.err"
CLI_EXIT=$?
if [ "$CLI_EXIT" -ge 3 ]; then
  echo "❌ 检查工具运行失败（exit $CLI_EXIT）"
  head -5 "$WORKDIR/cli.err" 2>/dev/null
  exit "$CLI_EXIT"
fi

cat "$WORKDIR/cli.out"
if [ ! -s "$REPORT" ]; then
  echo "❌ 生成报告失败"
  exit 2
fi

open "$REPORT"
echo "✅ 检查完成，报告已打开：$REPORT"
exit "$CLI_EXIT"
