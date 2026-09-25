# UnityProjectInspector

Unity 项目静态分析与运行时检测工具。给定一个 Unity 项目和一个 **Assignment**（一组检查需求），
它对项目执行检查并输出通过 / 失败结果，支持纯静态分析和"启动 Unity Player 运行时验证"两种模式。

## What it does

教师或评审者可以用统一的 JSON 描述"一个 Unity 项目应当满足哪些要求"（例如"必须存在
`TrainingScene` 场景"、"运行时启动后当前激活场景应为 `TrainingScene`"），然后用本工具对学生的
Unity 项目自动核验，而不需要人工打开 Unity 逐一检查。

CLI 是面向使用者的唯一入口，底层检测引擎（`UnityProjectInspector.Core`）负责规则评估、
临时 Harness 部署、Unity Player 执行、证据收集与结果合并。

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

## Quick Start

无需安装 Unity、无需任何真实 Unity 项目，即可在本地复现完整的**静态检测**流程。

### 1. Build

```bash
dotnet build
```

### 2. Run the static demo (passes → exit 0)

针对仓库内置的最小 Unity 工程 `tests/fixtures/MinimalUnityProject` 运行纯静态 assignment：

```bash
dotnet run --no-build --project src/UnityProjectInspector.Cli -- inspect \
  --project tests/fixtures/MinimalUnityProject \
  --assignment examples/assignments/static-only.json
```

真实输出：

```
UnityProjectInspector
────────────────────────────

Result: ✓ PASSED

Requirements:
  scene-exists         ✓ PASSED       StaticOnly: static analysis passed. Runtime not required.

Assignment passed: all requirements satisfied.
```

> 第一次运行 `dotnet run` 会先编译并打印编译信息；上面的 `dotnet run --no-build` 已假定你执行过
> `dotnet build`。若跳过 Build 步骤，直接用 `dotnet run --project ...` 也可，只会多出编译行。

### 3. Run JSON output

加上 `--format json` 即可得到机器可读结果（stdout 始终是可重新解析的有效 JSON）：

```bash
dotnet run --no-build --project src/UnityProjectInspector.Cli -- inspect \
  --project tests/fixtures/MinimalUnityProject \
  --assignment examples/assignments/static-only.json \
  --format json
```

真实输出：

```json
{
  "status": "PASSED",
  "message": "Assignment passed: all requirements satisfied.",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "Required scene exists",
      "status": "PASSED",
      "message": "StaticOnly: static analysis passed. Runtime not required.",
      "hasRuntime": false
    }
  ]
}
```

### 4. Run the failure demo (fails → exit 1)

`examples/assignments/static-fail.json` 故意要求一个不存在的场景 `MissingScene`，用于验证失败路径：

```bash
dotnet run --no-build --project src/UnityProjectInspector.Cli -- inspect \
  --project tests/fixtures/MinimalUnityProject \
  --assignment examples/assignments/static-fail.json
```

真实输出：

```
UnityProjectInspector
────────────────────────────

Result: ✗ FAILED

Requirements:
  scene-missing        ✗ FAILED       StaticOnly: static analysis failed. Runtime not required.

Assignment failed: some requirements did not pass.
```

退出码：`0` = 通过，`1` = 失败。上述三个流程均**不需要 Unity Editor**，可在 CI / 任意干净环境中复现。

## Assignment format

> 完整的字段、类型、可选性与所有 8 种静态规则 / 4 种 Runtime Action / 2 种 Runtime Assertion 的
> 最小 JSON 示例，见 [docs/assignment-format.md](docs/assignment-format.md)（以 Core 代码为准的权威说明）。

Assignment 是一个 JSON 文件，描述一组 `requirements`，每个 requirement 包含若干 `staticRules`
（以及可选的 `runtimeTest`）。最小结构：

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

- `evidenceRequirement`：`StaticOnly`（只需静态分析）或 `RuntimeRequired`（需要启动 Unity Player）。
- `staticRules[].type`：当前支持 `SceneExists`（按场景文件名匹配，区分大小写）。
- 运行时需求额外包含 `runtimeTest`（`actions` 与 `assertions`，使用 `$type` 多态）。

仓库内置示例：

- [`examples/assignments/static-only.json`](examples/assignments/static-only.json) — 纯静态检测，与 `tests/fixtures/MinimalUnityProject` 中的 `TrainingScene` 对应。
- [`examples/assignments/static-fail.json`](examples/assignments/static-fail.json) — 故意失败的静态检测示例（要求不存在的 `MissingScene`）。
- [`examples/assignments/runtime-required.json`](examples/assignments/runtime-required.json) — 静态 + 运行时检测（需要 Unity Editor）。

支持的运行 Action `$type`：`ObserveActiveScene`、`ReadLogs`、`Wait`、`ClickButton`。
支持的 Assertion `$type`：`AssertActiveScene`、`AssertNoExceptions`。

## Static vs Runtime inspection

| 维度 | 静态检测 (Static) | 运行时检测 (Runtime) |
|---|---|---|
| 是否需要 Unity Editor | **否** | **是** |
| 是否需要构建 / 启动 Player | 否 | 是 |
| 验证内容 | 场景 / 脚本等工程静态结构 | 运行时行为（如启动后激活场景） |
| 退出码 | 0（通过）/ 1（失败）/ 2（输入错误） | 同上，外加 3（运行时失败）/ 4（内部错误） |
| 是否可用于无 Unity 环境 | 是（本仓库 Quick Start 即为此） | 否 |

> 静态检测只需要一个标准的 Unity 工程目录（`Assets/`、`ProjectSettings/`、`Packages/`）和一个
> 能被识别的 `.unity` 场景文件；它**不会**读取 `Library/` 或运行任何 Unity 进程。

## Using it with a real Unity project

```bash
dotnet run --project src/UnityProjectInspector.Cli -- inspect \
  --project "/path/to/YourUnityProject" \
  --assignment "/path/to/your-assignment.json"
```

- `--project` 指向任意真实 Unity 工程（需含 `Assets/`、`ProjectSettings/`、`Packages/`）。
- 纯静态 assignment 不依赖 Unity 安装；含 `RuntimeRequired` 的 assignment 需要提供可用的
  Unity Editor（macOS 下由 Unity Hub 自动发现，或用 `--unity` 显式指定）。

## Inspect your own Unity project

下面是一个**完整、真实可复现**的流程，目标读者是**第一次接触本工具、不读 Core 源码**的开发者。
这里用变量 `YourUnityProject` 代表你本地的真实 Unity 工程目录。

### 步骤

1. **构建工具**

   ```bash
   dotnet build
   ```

2. **找到你的 Unity 工程目录**

   记下它的绝对路径，例如 `/Users/you/Projects/MyGame`。确认它至少包含：

   ```
   Assets/
   ProjectSettings/
   Packages/
   ```

   （`Library/`、`obj/` 等由 Unity 生成，不必提交，也不会被本工具读取。）

3. **确认工程里有哪些 Scene**

   打开 `Assets/` 翻找 `.unity` 文件，记下其中一个场景名，例如 `MainScene`。
   静态规则 `SceneExists` 按 `.unity` **文件名**匹配（区分大小写）。

4. **创建你的 assignment JSON**

   复制 [`examples/assignments/template.json`](examples/assignments/template.json)，把
   `"YourSceneName"` 改成你工程里真实存在的场景名：

   ```json
   {
     "id": "my-assignment",
     "name": "My Assignment",
     "requirements": [
       {
         "id": "scene-exists",
         "name": "Required scene exists",
         "evidenceRequirement": "StaticOnly",
         "staticRules": [
           {
             "id": "scene.required.exists",
             "name": "Required scene exists",
             "type": "SceneExists",
             "target": "MainScene",
             "severity": "Error"
           }
         ]
       }
     ]
   }
   ```

   保存为 `/Users/you/Projects/my-assignment.json`。（其它字段含义与更多规则类型见
   [docs/assignment-format.md](docs/assignment-format.md)。）

5. **运行静态检测**

   ```bash
   dotnet run --project src/UnityProjectInspector.Cli -- inspect \
     --project "/Users/you/Projects/MyGame" \
     --assignment "/Users/you/Projects/my-assignment.json"
   ```

   把上面两处路径替换成你自己的。若场景确实存在，结果为 `Result: ✓ PASSED`，退出码 `0`。

6. **阅读结果**

   - 文本模式：直接看 `Result:` 与每个 requirement 的 `✓ PASSED / ✗ FAILED`。
   - 机器可读：加 `--format json`，stdout 是有效 JSON。

7. **如果失败**

   结果为 `Result: ✗ FAILED`、退出码 `1` 表示**检测已正常执行，但某条 requirement 没通过**。
   例：`SceneExists` 失败时，说明：

   - 场景文件不在 `Assets/` 下，或
   - `target` 里的场景名与 `.unity` 文件名**大小写/拼写不一致**。

   按 requirement 的 `id`（如 `scene-exists`）定位是哪一条，修正 assignment 或工程后重跑。

8. **如果要做运行时检测**

   把对应 requirement 的 `evidenceRequirement` 改为 `"RuntimeRequired"`，并补充 `runtimeTest`
   （actions + assertions）。这需要本机安装 Unity Editor，且 CLI 能发现 Unity 可执行文件
   （macOS 下走 Unity Hub，或用 `--unity <path>` 显式指定）。完整示例见
   [`examples/assignments/runtime-required.json`](examples/assignments/runtime-required.json)。

> 提示：`examples/assignments/` 下还有 `static-only.json`、`static-fail.json`、
> `scene-check.json` 等可直接运行的示例；其中 `scene-check.json` 配合仓库内置的
> `tests/fixtures/MinimalUnityProject` 即可在**不安装 Unity** 的情况下复现完整静态检测。

## Troubleshooting

### exit 2 — InvalidInput（输入 / 配置错误）

CLI 在真正检测之前就发现输入有问题。常见原因与排查：

| 现象 | 原因 | 处理 |
|---|---|---|
| `Project directory not found` | `--project` 路径不存在 | 检查路径拼写 |
| `does not appear to be a valid Unity project` | 工程缺少 `Assets/`/`ProjectSettings/`/`Packages/` | 指向正确的 Unity 工程根目录 |
| `Assignment file not found` | `--assignment` 路径不存在 | 检查 JSON 文件路径 |
| `Assignment JSON is not valid: ...` | JSON 语法错误 / 未知 `$type` | 用 JSON 校验器检查；`$type` 必须是文档列出的确切值 |
| `Assignment must contain at least one requirement` | `requirements` 为空数组 | 至少加一条 requirement |
| `RuntimeRequired but has no RuntimeTest` | `evidenceRequirement: RuntimeRequired` 却没给 `runtimeTest` | 补 `runtimeTest`，或改回 `StaticOnly` |
| `no Unity executable found` | 需要 Runtime 但找不到 Unity | 安装 Unity Hub，或用 `--unity <path>` 指定 |

### exit 1 — Failed（检测通过，但需求未满足）

CLI **正常执行完**了 inspection，只是有 requirement 没通过。看每个 requirement 的
`✗ FAILED` 与消息：

- `SceneExists` 失败 → 场景不在 `Assets/` 下，或文件名与 `target` 不一致（区分大小写）。
- 其它静态规则失败 → 检查对应 GameObject / 组件 / 文件是否真实存在。

### exit 3 — RuntimeError（运行时无法完成）

Runtime inspection 启动 / 执行过程中失败（对应 `RuleStatus.NotEvaluated`）：

- 本机是否安装了 Unity Editor？
- Unity 可执行文件是否可用（Unity Hub 自动发现，或 `--unity` 指定）？
- 工程能否正常 build？
- Runtime Harness 是否部署成功（工具会自动部署并在结束后清理）？
- 加 `--debug` 查看更详细的技术信息。

### exit 4 — InternalError（工具内部未预期错误）

发生了工具内部的未预期异常：

- 默认输出只给一句提示 + 异常消息摘要，**不会**泄漏原始堆栈。
- 加 `--debug`（或 `-v` / `--verbose`）可看到完整技术细节，便于反馈问题。
- 保留 `--debug` 下的输出信息再上报。

## Requirements

| 项目 | 版本 / 说明 |
|---|---|
| .NET SDK | 10.0（CLI 与 Core 均以此目标框架构建） |
| Unity Editor | 2022.3.62f3c1（**仅运行时检测**需要；静态检测不需要） |
| 操作系统 | macOS（Apple Silicon）已验证；Unity Hub 自动检测路径 `/Applications/Unity/Hub/Editor` |
| 静态检测 | 不需要 Unity Editor，只需一个标准的 Unity 项目目录 |

> 未验证 Windows / Linux 支持，也未发布到 NuGet 或作为 `dotnet tool` 发布。跨平台支持以实际验证为准。

## Architecture

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

运行时检测的真实链路：

```
CLI
  ↓ Core
    ↓ HarnessDeployer（临时部署测试代码到用户工程）
      ↓ Unity 构建
        ↓ Player 启动
          ↓ GenericRuntimeBridge（IPC）
            ↓ Evidence（动作证据）
              ↓ ResultMerger
```

该链路需要 Unity Editor 与构建环境，且会向用户工程写入临时 Harness（检测结束后自动清理）。

## Exit codes

| 码 | 含义 | 触发条件 |
|---|---|---|
| 0 | Passed | 所有需求满足 |
| 1 | Failed | 一个或多个需求失败（含运行时断言失败） |
| 2 | InvalidInput | 参数错误、文件缺失、JSON 非法、Assignment 配置非法 |
| 3 | RuntimeError | 运行时无法完成（Unity 构建 / 启动 / IPC 失败） |
| 4 | InternalError | 检测管线内部未预期异常 |

所有输入类错误均返回 **2** 并给出人类可读信息，不会向普通用户暴露原始堆栈或 Core 内部细节
（`--debug` 可开启完整技术信息用于诊断）。

## Development / Tests

```bash
dotnet build
dotnet test
```

CLI 测试覆盖：参数解析、输入校验、错误处理、退出码、示例 Assignment 可加载性，以及
`tests/fixtures/MinimalUnityProject` 的静态 demo（成功 / 失败 / JSON）复现。
