# Assignment JSON Format

An **assignment** is a JSON file that tells UnityProjectInspector what to check in a Unity project. This document describes every field, type, and constraint, based on the actual Core code.

---

## 1. AssignmentDefinition (top level)

```json
{
  "id": "my-assignment",
  "name": "My Assignment",
  "description": "Optional description of what this assignment verifies.",
  "requirements": [ ]
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | **Yes** | Unique identifier, e.g. `"maze-2d-basic"`. |
| `name` | string | **Yes** | Human-readable name, e.g. `"2D迷宫基础作业"`. |
| `description` | string | No | Free-text description of the assignment goal. |
| `requirements` | array | **Yes** | List of [RequirementDefinition](#2-requirementdefinition). Must contain **at least one** entry; an empty array is rejected with exit code 2. |

---

## 2. RequirementDefinition

Each requirement defines one or more checks to run against a Unity project.

```json
{
  "id": "scene-exists",
  "name": "Required scene exists",
  "description": "Optional explanation of what this requirement verifies.",
  "evidenceRequirement": "StaticOnly",
  "staticRules": [ ],
  "runtimeTest": null
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | **Yes** | Unique within the assignment. Duplicate IDs produce a validation error. |
| `name` | string | **Yes** | Human-readable name. Shown in the inspection result output. |
| `description` | string | No | Optional explanation. |
| `evidenceRequirement` | string | No | **Default: `"StaticOnly"`**. Valid values: `"StaticOnly"` or `"RuntimeRequired"`. Null/absent defaults to `StaticOnly`. |
| `staticRules` | array | No | List of [RuleDefinition](#3-ruledefinition--static-rule). May be empty. |
| `runtimeTest` | object | No | A [RuntimeTestScript](#7-runtimetestscript) object. Required when `evidenceRequirement` is `"RuntimeRequired"`. Ignored (with a warning) when `evidenceRequirement` is `"StaticOnly"`. |

### evidenceRequirement rules

| Value | Behavior |
|---|---|
| `"StaticOnly"` (default) | Static analysis only. No Unity Editor needed. |
| `"RuntimeRequired"` | Requires a Unity Editor / build pipeline. `runtimeTest` must be set and must contain at least one action; otherwise a validation error is produced. |
| Missing / null | Treated as `StaticOnly` (backward compatible). |

### Validation errors

| Condition | Severity |
|---|---|
| `RuntimeRequired` + `runtimeTest` is null | Error |
| `RuntimeRequired` + `runtimeTest.actions` is empty | Error |
| `StaticOnly` + `runtimeTest` is present | Warning (runtimeTest is skipped) |
| Empty `requirements` array (top level) | Error — rejected by CLI with exit code 2 |

---

## 3. RuleDefinition — Static Rule

Each static rule is a step in the static analysis pipeline. Different rule types use different fields.

```json
{
  "id": "scene.sample.exists",
  "name": "SampleScene exists",
  "type": "SceneExists",
  "target": "SampleScene",
  "severity": "Error"
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | **Yes** | Unique identifier for this rule within the requirement. |
| `name` | string | **Yes** | Human-readable name. |
| `type` | string | **Yes** | Rule type discriminator. Must be one of the 8 supported types (see table below). |
| `target` | string | Depends | Interpretation depends on rule type (see table). Required for most rules. |
| `expectedClass` | string | Depends | Class/component type name. |
| `expectedParent` | string | Depends | Parent GameObject name (for hierarchy rules). |
| `expectedMethod` | string | Depends | Method name (for UnityEvent / CodeEvidence rules). |
| `severity` | string | No | `"Info"`, `"Warning"`, or `"Error"`. Default: `"Info"`. |
| `message` | string | No | Optional custom message override. |
| `evidenceRequirement` | string | No | Same semantics as Requirement-level. Default: `"StaticOnly"`. |

### 8 supported rule types

#### SceneExists
Checks that a `.unity` scene file exists in `Assets/`.

```json
{ "id": "r", "name": "R", "type": "SceneExists", "target": "SampleScene" }
```
- `target` **required** — scene name (file name without `.unity`)

#### GameObjectExists
Checks that a `GameObject` with the given name exists in the scene hierarchy.

```json
{ "id": "r", "name": "R", "type": "GameObjectExists", "target": "Player" }
```
- `target` **required** — GameObject name

#### ComponentExists
Checks that a component of the given type is attached to a GameObject.

```json
{ "id": "r", "name": "R", "type": "ComponentExists", "target": "Canvas", "expectedClass": "Canvas" }
```
- `target` **required** — GameObject name
- `expectedClass` **required** — component type name (e.g. `"Canvas"`, `"Rigidbody"`)

#### GameObjectHierarchy
Checks that a child GameObject has the expected parent.

```json
{ "id": "r", "name": "R", "type": "GameObjectHierarchy", "target": "Button", "expectedParent": "Canvas" }
```
- `target` **required** — child GameObject name
- `expectedParent` **required** — parent GameObject name

#### ScriptAttached
Checks that a C# script component (by class name) is attached to a GameObject.

```json
{ "id": "r", "name": "R", "type": "ScriptAttached", "target": "GameManager", "expectedClass": "GameManager" }
```
- `target` **required** — GameObject name
- `expectedClass` **required** — script class name

#### FileExists
Checks that a file (or directory) exists relative to the Unity project root.

```json
{ "id": "r", "name": "R", "type": "FileExists", "target": "ProjectSettings/ProjectVersion.txt" }
```
- `target` **required** — relative path under project root
- Path must use forward slashes `/`
- **Absolute paths are rejected** (e.g. `/Users/alice/...` → rule fails)
- **Path traversal (`..`) is rejected** (e.g. `../outside.txt` → rule fails)
- Valid examples: `"Assets/Scenes/SampleScene.unity"`, `"ProjectSettings/ProjectVersion.txt"`
- Invalid: `"/absolute/path"`, `"../outside"`, `"C:\\Windows\\win.ini"`

#### UnityEventBinding
Checks that a `UnityEvent` on a component calls a specific method.

```json
{ "id": "r", "name": "R", "type": "UnityEventBinding", "target": "StartButton", "expectedClass": "PanelSwitcher", "expectedMethod": "OnStartClicked" }
```
- `target` **required** — source GameObject name
- `expectedClass` — target script class name (may be omitted in some contexts)
- `expectedMethod` **required** — method name

#### CodeEvidence
Checks that a C# script contains a specific method (by AST analysis).

```json
{ "id": "r", "name": "R", "type": "CodeEvidence", "target": "GameManager", "expectedMethod": "StartGame" }
```
- `target` **required** — source GameObject name
- `expectedMethod` **required** — method name to look for

---

## 4. RuntimeAction

Actions are executed in sequence against a running Unity Player process. Each action uses `$type` as the discriminator.

All actions share these common fields:

| Field | Type | Required | Notes |
|---|---|---|---|
| `actionId` | string | **Yes** | Unique identifier across all actions in the test script. |
| `description` | string | No | Optional human-readable description. |

### Wait

Pauses execution for a specified duration (e.g. between a click and a scene check).

```json
{ "$type": "Wait", "actionId": "wait_load", "milliseconds": 500 }
```
- `milliseconds` — int, **optional**, default `200`

### ClickButton

Simulates a click on a UI `Button` component by GameObject name.

```json
{ "$type": "ClickButton", "actionId": "click_start", "gameObjectName": "StartButton" }
```
- `gameObjectName` — string, **required**

### ObserveActiveScene

Reads the currently active scene name and stores it as evidence (consumed by `AssertActiveScene`).

```json
{ "$type": "ObserveActiveScene", "actionId": "observe_scene" }
```
- No extra fields.

### ReadLogs

Captures runtime log entries (errors, exceptions) accumulated so far (consumed by `AssertNoExceptions`).

```json
{ "$type": "ReadLogs", "actionId": "read_logs" }
```
- No extra fields.

---

## 5. RuntimeAssertion

Assertions are evaluated **after all actions complete**, against the collected evidence.

All assertions share these common fields:

| Field | Type | Required | Notes |
|---|---|---|---|
| `assertionId` | string | **Yes** | Unique identifier across all assertions. |
| `description` | string | No | Optional description. |

### AssertActiveScene

Asserts that the active scene name (observed by `ObserveActiveScene`) matches an expected value.

```json
{ "$type": "AssertActiveScene", "assertionId": "assert_scene", "expectedSceneName": "TargetScene" }
```
- `expectedSceneName` — string, **required**

### AssertNoExceptions

Asserts that no runtime exceptions occurred during the observation window.

```json
{ "$type": "AssertNoExceptions", "assertionId": "assert_no_errors" }
```
- `ignoredPatterns` — array of strings, **optional**, default `[]`. Exception messages matching these patterns are not considered failures.

```json
{ "$type": "AssertNoExceptions", "assertionId": "assert_no_errors", "ignoredPatterns": ["Texture creation is not supported"] }
```

---

## 6. RuntimeTestScript

A complete runtime test script.

```json
{
  "name": "Click Start → Load TargetScene",
  "actions": [
    { "$type": "ClickButton", "actionId": "click_start", "gameObjectName": "StartButton" },
    { "$type": "Wait", "actionId": "wait_load", "milliseconds": 500 },
    { "$type": "ObserveActiveScene", "actionId": "observe_scene" },
    { "$type": "ReadLogs", "actionId": "read_logs" }
  ],
  "assertions": [
    { "$type": "AssertActiveScene", "assertionId": "assert_scene", "expectedSceneName": "TargetScene" },
    { "$type": "AssertNoExceptions", "assertionId": "assert_no_errors" }
  ]
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | **Yes** | Human-readable test scenario name. |
| `actions` | array | No | List of [RuntimeAction](#4-runtimeaction). Must be non-empty when `evidenceRequirement` is `"RuntimeRequired"`. |
| `assertions` | array | No | List of [RuntimeAssertion](#5-runtimeassertion). |

### Minimal RuntimeRequired requirement (with runtimeTest)

```json
{
  "id": "scene-navigate",
  "name": "Navigate to TargetScene at runtime",
  "evidenceRequirement": "RuntimeRequired",
  "runtimeTest": {
    "name": "Click → Observe",
    "actions": [
      { "$type": "ClickButton", "actionId": "click_go", "gameObjectName": "GoButton" },
      { "$type": "Wait", "actionId": "wait", "milliseconds": 500 },
      { "$type": "ObserveActiveScene", "actionId": "observe" },
      { "$type": "ReadLogs", "actionId": "logs" }
    ],
    "assertions": [
      { "$type": "AssertActiveScene", "assertionId": "check_scene", "expectedSceneName": "TargetScene" },
      { "$type": "AssertNoExceptions", "assertionId": "check_no_errors" }
    ]
  }
}
```

---

## 7. StaticOnly vs RuntimeRequired

| Aspect | StaticOnly | RuntimeRequired |
|---|---|---|
| Needs Unity Editor | **No** — pure file / YAML / AST analysis | **Yes** — requires Unity Hub or explicit `--unity` path |
| Needs build pipeline | No | Yes — tool builds and runs a standalone Player |
| Speed | Fast (milliseconds to seconds) | Slow (build + launch + execute) |
| Verified behavior | Static intent only (file exists, component attached, hierarchy correct) | Actual runtime behavior (button click → scene loads, no exceptions) |
| Exit code on failure | `1` (Failed) | `1` (Failed) |
| Exit code on setup error | `2` (InvalidInput) | `2` (InvalidInput) |
| Exit code on runtime failure | N/A | `3` (RuntimeError — e.g. build fails, process crashes) |
| Exit code on internal error | `4` (InternalError) | `4` (InternalError) |

**Important limitations:**
- `RuntimeRequired` inspection requires the CLI to locate a Unity Editor on your machine. On macOS this happens automatically via Unity Hub, or you can specify `--unity /path/to/Unity`.
- The tool builds and deploys a Runtime Harness into your project during runtime inspection, and cleans it up afterwards.
- Runtime inspection is only supported on macOS at this time.

---

## 8. Complete example: writing an assignment from scratch

Below is a **minimal valid assignment** that you can save as a JSON file and run immediately (it uses `YourSceneName` as a placeholder — replace it with a real scene in your project, or try it against the bundled `MinimalUnityProject` fixture using `TrainingScene`):

```json
{
  "id": "my-first-assignment",
  "name": "My First Assignment",
  "description": "A minimal static-only assignment.",
  "requirements": [
    {
      "id": "scene-exists",
      "name": "Required scene exists",
      "evidenceRequirement": "StaticOnly",
      "staticRules": [
        {
          "id": "scene.main.exists",
          "name": "Main scene must exist",
          "type": "SceneExists",
          "target": "YourSceneName",
          "severity": "Error"
        }
      ]
    }
  ]
}
```

Run it against your Unity project:

```bash
dotnet run --project src/UnityProjectInspector.Cli -- inspect \
  --project /path/to/YourUnityProject \
  --assignment /path/to/your-assignment.json
```

This assignment uses `"StaticOnly"`, so it will **succeed without Unity**. The `YourSceneName` placeholder is not a real scene — the inspection will return exit code `1` (Failed) with a `SceneExists` failure if the scene does not exist. That is intentional: it shows you the expected exit code for unmet requirements.

To pass, replace `"YourSceneName"` with a scene name that exists under your project's `Assets/` directory.

---

## 9. Common mistakes

| Mistake | JSON | Result |
|---|---|---|
| Empty requirements | `"requirements": []` | CLI rejects with exit code **2** (`InvalidInput`): *"Assignment must contain at least one requirement."* |
| Missing `id` | `{ "name": "X", "requirements": [...] }` | JSON parse fails; exit code **2** |
| Missing `name` | `{ "id": "x", "requirements": [{ "id": "r", ... }] }` | Validation error; exit code **2** |
| Unknown `$type` in rule | `{ "type": "BogusRule", ... }` | Validation error; exit code **2** |
| Unknown `$type` in action | `{ "$type": "BogusAction", ... }` | JSON deserialization fails; exit code **2** |
| Unknown `$type` in assertion | `{ "$type": "BogusAssert", ... }` | JSON deserialization fails; exit code **2** |
| `RuntimeRequired` without `runtimeTest` | `"evidenceRequirement": "RuntimeRequired"` (no `runtimeTest`) | Validation error; exit code **2** |
| `RuntimeRequired` with empty actions | `"runtimeTest": { "name": "t", "actions": [], "assertions": [...] }` | Validation error; exit code **2** |
| `FileExists` with absolute path | `"target": "/Users/alice/secret.txt"` | Rule **fails** at runtime (security rejection), exit code **1** |
| `FileExists` with `..` traversal | `"target": "../outside.txt"` | Rule **fails** at runtime (security rejection), exit code **1** |
| `StaticOnly` with `runtimeTest` present | Both fields set | **Warning** — runtimeTest is ignored. Inspection still runs static analysis. Exit code depends on static results (0 or 1). |

---

## 10. Exit code reference

| Code | Name | Meaning |
|---|---|---|
| 0 | Passed | All requirements satisfied. |
| 1 | Failed | Inspection completed but some requirements were not met. |
| 2 | InvalidInput | JSON parse error, missing required fields, unknown `$type`, etc. |
| 3 | RuntimeError | Runtime could not complete (Unity not found, build failed, process crash). |
| 4 | InternalError | Unexpected internal exception. Add `--debug` for technical details. |

---

## Reference: existing examples in this repository

| File | Description |
|---|---|
| `examples/assignments/static-only.json` | Minimal static-only pass case |
| `examples/assignments/static-fail.json` | Static-only with intentionally missing scene (exit 1) |
| `examples/assignments/scene-check.json` | SceneExists + FileExists against `MinimalUnityProject` fixture (exit 0) |
| `examples/assignments/template.json` | Copy-ready template with placeholder |
| `examples/assignments/runtime-required.json` | Full RuntimeRequired example (needs Unity) |