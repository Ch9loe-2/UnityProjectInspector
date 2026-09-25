# UnityProjectInspector

Unity 项目静态分析与运行时检测工具。

## 构建

```bash
dotnet build
```

## 使用方法

```bash
dotnet run --project src/UnityProjectInspector.Cli -- inspect \
  --project "/path/to/UnityProject" \
  --assignment "/path/to/assignment.json"
```

### 选项

| 参数 | 说明 | 必填 |
|---|---|---|
| `--project <path>` | Unity 项目路径 | 是 |
| `--assignment <path>` | Assignment JSON 文件路径 | 是 |
| `--unity <path>` | Unity Editor 可执行路径 | 否（自动检测） |
| `--output <path>` | 结果输出目录 | 否（默认当前目录） |
| `--format <format>` | 输出格式：`text` 或 `json` | 否（默认 `text`） |
| `--help` | 显示帮助信息 | 否 |

### Assignment JSON 示例

```json
{
  "id": "example-assignment",
  "name": "示例作业",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "场景存在检查",
      "evidenceRequirement": "StaticOnly",
      "staticRules": [
        {
          "id": "scene.sample.exists",
          "name": "SampleScene 存在",
          "type": "SceneExists",
          "target": "SampleScene",
          "severity": "Error"
        }
      ]
    },
    {
      "id": "runtime-scene",
      "name": "运行时场景验证",
      "evidenceRequirement": "RuntimeRequired",
      "staticRules": [
        {
          "id": "scene.training.exists",
          "name": "TrainingScene 存在",
          "type": "SceneExists",
          "target": "TrainingScene",
          "severity": "Error"
        }
      ],
      "runtimeTest": {
        "name": "验证当前场景",
        "actions": [
          {
            "$type": "ObserveActiveScene",
            "actionId": "observe_scene",
            "description": "观察当前激活场景"
          }
        ],
        "assertions": [
          {
            "$type": "AssertActiveScene",
            "assertionId": "assert_scene",
            "description": "当前场景应为 TrainingScene",
            "expectedSceneName": "TrainingScene"
          }
        ]
      }
    }
  ]
}
```

### 输出格式

**文本格式（默认）：**

```
UnityProjectInspector
────────────────────────────

Result: ✓ PASSED

Requirements:
  scene-exists          ✓ PASSED       场景存在

Assignment passed: all requirements satisfied.
```

**JSON 格式（`--format json`）：**

```json
{
  "status": "PASSED",
  "message": "Assignment passed: all requirements satisfied.",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "场景存在检查",
      "status": "PASSED",
      "message": "场景存在",
      "hasRuntime": false
    }
  ]
}
```

### 退出码

| 码 | 含义 | 说明 |
|---|---|---|
| 0 | Passed | 所有需求满足 |
| 1 | Failed | 一个或多个需求失败 |
| 2 | InvalidInput | 参数/文件/JSON 错误 |
| 3 | RuntimeError | Unity 运行时/构建/启动失败 |
| 4 | InternalError | 内部异常 |

## 已验证的平台

- macOS (Apple Silicon): Unity 2022.3.62f3c1
- .NET 10.0

## 项目结构

```
src/UnityProjectInspector.Cli/  — CLI 入口（参数解析/格式化/退出码）
src/UnityProjectInspector.Core/ — 检测引擎（规则/解析器/运行时/合并）
tests/                          — 测试
```