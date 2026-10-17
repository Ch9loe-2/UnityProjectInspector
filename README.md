# UnityProjectInspector

一个检查 Unity 工程是否满足要求的命令行工具。你把"工程里该有什么"写成一份 JSON 检查方案，它逐条核对，输出通过/失败和带证据的报告，不用人工打开 Unity 翻。

两种检查模式：

- **静态检查**：翻工程文件，核对场景、脚本、对象、组件等是否存在。秒级，不需要装 Unity。
- **运行时检查**：临时往工程部署一段测试代码，启动 Unity 实际跑一遍，验证运行时行为（例如启动后激活场景）。分钟级，需要本机有 Unity。

## 快速开始

### 直接用成品（适合老师、同学）

两个平台都是零依赖的成品包，解压即用，把学生的工程 zip 拖上去，自动完成解压 → 生成检查方案 → 静态检查 → 打开中文体检报告。

- macOS：`Unity工程一键检查.app.zip`，拖到 App 图标上
- Windows：`Unity工程一键检查_Windows.zip`，拖到 `check_unity_project.bat` 上

体检报告含结论、统计、检查明细、场景层级树（Unity Hierarchy 风格）和失败项的修复建议。相关脚本在 `tools/DragCheck/`（macOS 的 `.app` 由 `osacompile` 生成，不入库）。

双击 `Demo.command` 可以看一个免命令的演示（成功 + 故意失败两个案例）。

### 开发者

```bash
dotnet build

# 用仓库自带的最小工程跑静态检查（通过，退出码 0）
dotnet run --no-build --project src/UnityProjectInspector.Cli -- inspect \
  --project tests/fixtures/MinimalUnityProject \
  --assignment examples/assignments/static-only.json

# 故意失败的例子（退出码 1）
dotnet run --no-build --project src/UnityProjectInspector.Cli -- inspect \
  --project tests/fixtures/MinimalUnityProject \
  --assignment examples/assignments/static-fail.json
```

常用选项：`--format json`（机器可读）、`--format chinese`（中文报告）、`--report <file>`（详细报告，按扩展名推断 json/md/html）、`--health-report <file>`（体检报告）、`--generate-assignment <path>`（自动生成检查方案后退出）、`--output <dir>`。

## 检查方案（Assignment）

一份 JSON，描述工程应当满足哪些要求。最小例子：

```json
{
  "id": "my-assignment",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "Required scene exists",
      "evidenceRequirement": "StaticOnly",
      "staticRules": [
        { "id": "scene.required.exists", "type": "SceneExists", "target": "MainScene", "severity": "Error" }
      ]
    }
  ]
}
```

`evidenceRequirement`：`StaticOnly` 只需静态分析；`RuntimeRequired` 需要启动 Unity Player。目前支持 8 种静态规则、4 种运行 Action、2 种运行断言。完整字段说明见 [docs/assignment-format.md](docs/assignment-format.md)，可直接运行的示例在 `examples/assignments/`（如 `static-only.json`、`static-fail.json`、`runtime-required.json`）。

## 交互模式

不带参数直接运行 CLI 即进入交互模式，所有输出为中文：快速检查（自动扫描场景）、使用已有方案、引导式创建方案。

## 静态 vs 运行时

| 维度 | 静态 | 运行时 |
|---|---|---|
| 需要 Unity Editor | 否 | 是 |
| 验证内容 | 工程静态结构（场景/脚本/对象） | 运行时行为（启动后激活场景等） |
| 用时 | 秒级 | 分钟级 |
| 退出码 | 0 / 1 / 2 | 0 / 1 / 2 / 3 / 4 |

## 退出码

| 码 | 含义 | 触发条件 |
|---|---|---|
| 0 | 通过 | 所有需求满足 |
| 1 | 未通过 | 有需求失败（含运行时断言失败） |
| 2 | 输入错误 | 参数错误、文件缺失、JSON 非法、方案配置非法 |
| 3 | 运行时失败 | Unity 构建 / 启动 / IPC 失败 |
| 4 | 内部错误 | 检测管线内部未预期异常 |

输入类错误都会给出人类可读的提示；加 `--debug` 可看到完整技术信息。

## 常见问题

- **exit 2**：`--project` 路径不存在、工程缺 `Assets/ProjectSettings/Packages`、assignment JSON 非法、`RuntimeRequired` 却缺 `runtimeTest`。按提示修正。
- **exit 1**：检查正常完成，只是有需求没满足。看具体 requirement 的失败信息，中文模式会带"建议"（哪错了、怎么改）。
- **exit 3**：Unity 未安装或找不到、工程构建失败、Harness 部署失败。
- **exit 4**：工具内部未预期异常，加 `--debug` 保留完整输出再反馈。

## 架构

CLI 负责参数解析、输入校验、结果格式化和退出码；`UnityProjectInspector.Core` 负责规则引擎、Harness 部署、Unity Player 执行、证据收集与结果合并。

运行时检查的链路：临时部署 Harness → 构建并启动 Unity Player → 通过 IPC 收集动作证据 → 判定断言 → 结束后自动清理，不污染用户工程。所有输出（文本 / JSON / 报告）都会带上每条规则的结构化证据：查了什么、期望什么、实际看到什么、为什么是这个结论。

## 环境要求

- .NET SDK 10
- Unity Editor 2022.3（仅运行时检查需要）
- 已验证：macOS（Apple Silicon）、Windows（win-x64）
- 静态检查不需要 Unity；报告生成在 CLI 内完成，不依赖 python

## 开发与测试

```bash
dotnet build
dotnet test
```

CLI 测试覆盖参数解析、输入校验、错误处理、退出码、示例 assignment 可加载性，以及最小工程 fixtures 的静态检查（通过 / 失败 / JSON 输出）。
