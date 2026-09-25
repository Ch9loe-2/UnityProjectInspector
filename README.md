# UnityProjectInspector

Unity 项目静态分析与运行时检测工具。给定一个 Unity 项目和一个 **Assignment**（一组检查需求），
它对项目执行检查并输出通过 / 失败结果，支持纯静态分析和"启动 Unity Player 运行时验证"两种模式。

## 这是什么 / 解决什么问题

教师或评审者可以用统一的 JSON 描述"一个 Unity 项目应当满足哪些要求"（例如"必须存在
TrainingScene 场景"、"运行时启动后当前激活场景应为 TrainingScene"），然后用本工具对学生的
Unity 项目自动核验，而不需要人工打开 Unity 逐一检查。

CLI 是面向使用者的唯一入口，底层检测引擎（`UnityProjectInspector.Core`）负责规则评估、
临时 Harness 部署、Unity Player 执行、证据收集与结果合并。

## 当前能力

| 能力 | 说明 |
|---|---|
| 静态检测 (Static inspection) | 解析场景 / 脚本，执行规则（如 `SceneExists`） |
| 运行时检测 (Runtime inspection) | 临时部署 Harness → 构建并启动 Unity Player → 通过 IPC 收集运行时证据 |
| 临时 Harness 部署 | 检测结束后自动清理，不污染用户项目 |
| Unity Player 执行 | 构建临时 Player 并启动，运行 RuntimeTest 脚本 |
| 证据 / 结果处理 | 收集 Action 证据，执行 Assertion 判定 |
| CLI 文本输出 | 人类可读的通过 / 失败报告 |
| CLI JSON 输出 | 机器可读结果，便于集成 |

> 当前不是商业产品，也不是完整 CI/CD 平台：它聚焦于"Assignment → 检测 → 结果"这一核心闭环。

## 环境要求（已验证）

| 项目 | 版本 / 说明 |
|---|---|
| .NET SDK | 10.0（CLI 与 Core 均以此目标框架构建） |
| Unity Editor | 2022.3.62f3c1（仅运行时检测需要；静态检测不需要） |
| 操作系统 | macOS（Apple Silicon）已验证；Unity Hub 自动检测路径 `/Applications/Unity/Hub/Editor` |
| 静态检测 | 不需要 Unity Editor，只需一个标准的 Unity 项目目录 |

> 未验证 Windows / Linux 支持，也未发布到 NuGet 或作为 `dotnet tool` 发布。跨平台支持以实际验证为准。

## 基础用法

```bash
dotnet run --project src/UnityProjectInspector.Cli -- inspect \
  --project "/path/to/UnityProject" \
  --assignment "/path/to/assignment.json"
```

### 选项

| 参数 | 说明 | 必填 |
|---|---|---|
| `--project <path>` | Unity 项目路径（需含 `Assets/`、`ProjectSettings/`、`Packages/`） | 是 |
| `--assignment <path>` | Assignment JSON 文件路径 | 是 |
| `--unity <path>` | Unity Editor 可执行文件路径 | 否（macOS 下自动检测 Unity Hub） |
| `--output <path>` | 结果输出目录（写入 `inspection-result.txt` / `.json`） | 否（默认当前目录，不写文件） |
| `--format <format>` | 输出格式：`text` 或 `json` | 否（默认 `text`） |
| `--debug` | 内部错误时打印完整堆栈（默认只打印简洁信息） | 否 |
| `--help` | 显示帮助 | 否 |

> 运行时检测（`evidenceRequirement: RuntimeRequired`）必须提供可用的 Unity Editor，
> 否则会返回退出码 2 并提示。`--unity` 可显式指定，macOS 下也可由 Unity Hub 自动发现。

## Assignment JSON 示例

仓库内提供了可直接参考的示例：

- [`examples/assignments/static-only.json`](examples/assignments/static-only.json) — 纯静态检测
- [`examples/assignments/runtime-required.json`](examples/assignments/runtime-required.json) — 静态 + 运行时检测

示例中的场景名（如 `TrainingScene`）是占位，请按你的项目实际场景名修改后再使用。
运行前用 `--project` 指向一个真实的 Unity 项目。

最小结构（节选）：

```json
{
  "id": "example-static-only",
  "name": "Static-Only Example Assignment",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "Required scene exists",
      "evidenceRequirement": "StaticOnly",
      "staticRules": [
        {
          "id": "scene.training.exists",
          "name": "TrainingScene exists",
          "type": "SceneExists",
          "target": "TrainingScene",
          "severity": "Error"
        }
      ]
    }
  ]
}
```

运行时需求额外包含 `runtimeTest`（含 `actions` 与 `assertions`，使用 `$type` 多态）：

```json
{
  "id": "runtime-active-scene",
  "name": "Active scene at runtime",
  "evidenceRequirement": "RuntimeRequired",
  "staticRules": [
    { "id": "s1", "name": "TrainingScene exists", "type": "SceneExists", "target": "TrainingScene", "severity": "Error" }
  ],
  "runtimeTest": {
    "name": "Verify active scene on Player start",
    "actions": [
      { "$type": "ObserveActiveScene", "actionId": "observe_scene", "description": "Observe active scene" }
    ],
    "assertions": [
      { "$type": "AssertActiveScene", "assertionId": "assert_scene", "expectedSceneName": "TrainingScene" }
    ]
  }
}
```

支持的运行 Action `$type`：`ObserveActiveScene`、`ReadLogs`、`Wait`、`ClickButton`。
支持的 Assertion `$type`：`AssertActiveScene`、`AssertNoExceptions`。

## 输出格式

**文本格式（默认）：**

```
UnityProjectInspector
────────────────────────────

Result: ✓ PASSED

Requirements:
  scene-exists          ✓ PASSED       ...
```

**JSON 格式（`--format json`）：**

```json
{
  "status": "PASSED",
  "message": "Assignment passed: all requirements satisfied.",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "Required scene exists",
      "status": "PASSED",
      "message": "Scene 'TrainingScene' exists",
      "hasRuntime": false
    }
  ]
}
```

JSON 输出写入标准输出；使用 `--output <dir>` 时会额外写入该目录下的 `inspection-result.json`。
stdout 始终是可被重新解析的有效 JSON（无调试日志污染）。

## 退出码

| 码 | 含义 | 触发条件 |
|---|---|---|
| 0 | Passed | 所有需求满足 |
| 1 | Failed | 一个或多个需求失败（含运行时断言失败） |
| 2 | InvalidInput | 参数错误、文件缺失、JSON 非法、Assignment 配置非法 |
| 3 | RuntimeError | 运行时无法完成（Unity 构建 / 启动 / IPC 失败） |
| 4 | InternalError | 检测管线内部未预期异常 |

所有输入类错误均返回 **2** 并给出人类可读信息，不会向普通用户暴露原始堆栈或 Core 内部细节
（`--debug` 可开启完整技术信息用于诊断）。

## 架构（简要）

```
CLI (参数解析 / 输入校验 / 结果格式化 / 退出码)
  ↓
AssignmentLoader  (JSON → AssignmentDefinition)
  ↓
InspectionWorkflowRunner (Core)
  ↓
Harness 部署 (仅运行时需求)
  ↓
Unity 构建 / Player 启动
  ↓
运行时 IPC (Actions → Evidence → Assertions)
  ↓
ResultMerger (Core)
  ↓
CLI Formatter (文本 / JSON)
```

CLI 不复制 Core 的业务逻辑（规则引擎、校验器、运行时运行器、Harness 部署、结果合并均由 Core 负责）。

## 构建

```bash
dotnet build
```

## 测试

```bash
dotnet test
```

CLI 测试覆盖参数解析、输入校验、错误处理、退出码与示例 Assignment 的可加载性。
