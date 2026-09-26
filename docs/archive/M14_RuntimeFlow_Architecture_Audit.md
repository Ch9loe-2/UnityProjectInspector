# M14 — Runtime Flow Architecture Audit

> **Type:** Pure Architecture Audit — No Code Modifications
> **Baseline:** e858cbf (main, clean working tree)
> **Date:** 2026-09-19

---

## Table of Contents

1. [Current Runtime Architecture](#1-current-runtime-architecture)
2. [RuntimeRunner Boundary (Question 1)](#2-runtimerunner-boundary-question-1)
3. [RuntimeEvidence Assessment (Question 2)](#3-runtimeevidence-assessment-question-2)
4. [Proposed Runtime Actions (Question 3)](#4-proposed-runtime-actions-question-3)
5. [Proposed Runtime Assertions (Question 4)](#5-proposed-runtime-assertions-question-4)
6. [Runtime Flow Model](#6-runtime-flow-model)
7. [Rule Engine Integration (Question 5)](#7-rule-engine-integration-question-5)
8. [Static vs Runtime Boundary](#8-static-vs-runtime-boundary)
9. [Example: 2D Maze](#9-example-2d-maze)
10. [Example: 3D Industrial Monitor](#10-example-3d-industrial-monitor)
11. [MVP Recommendation](#11-mvp-recommendation)
12. [Deferred Features](#12-deferred-features)
13. [Out of Scope](#13-out-of-scope)
14. [Final Architecture Decision](#14-final-architecture-decision)

---

## 1. Current Runtime Architecture

### 1.1 Files in `src/UnityProjectInspector.Core/Runtime/`

| File | Lines | Purpose | Status |
|------|-------|---------|--------|
| `IRuntimeRunner.cs` | 24 | Interface: `Task<RuntimeSession> RunAsync(RuntimeRunOptions, CancellationToken)` | ✅ Stable |
| `RuntimeRunner.cs` | 447 | Orchestrator: Build → Launch → Poll → Classify | ✅ Stable |
| `RuntimeRunOptions.cs` | 79 | Configuration: Unity path, project path, timeouts, evidence paths | ✅ Stable |
| `RuntimeResultStatus.cs` | 38 | Enum: Passed/Failed/NotEvaluated/Timeout/ProcessExited/BuildFailed | ✅ Stable |
| `PlayerEvidence.cs` | 44 | JSON model for standalone Player evidence | ✅ Stable |
| `RuntimeEvidence.cs` | 33 | Generic evidence record: Type/Expected/Observed/Obtained/Message | ⚠️ Generic |
| `RuntimeSession.cs` | 33 | Session record: project, version, PID, timestamps, evidence, result | ⚠️ Generic |
| `UnityRuntimeAdapter.cs` | 314 | **Editor PlayMode route** (legacy, M10) using harness project | ⏳ Unstable |

### 1.2 Two Divergent Approaches

The codebase currently has **two separate runtime approaches** with no unification:

**A) UnityRuntimeAdapter (Legacy, M10) — Editor PlayMode**
- Launches Unity Editor in `-batchmode -noGraphics`
- Uses a separate harness project (`RuntimeTestHarness`)
- Passes target scene path via a JSON config file
- Polls for output file written by a harness Editor script
- Timeout: 240s (cold start can be 90–180s)
- **Status:** Stable but isolated. Never integrated with IRuntimeRunner.

**B) RuntimeRunner (M13) — Player Build**
- Launches Unity Editor to build a standalone Player via `-executeMethod`
- Launches the standalone Player as a separate process
- Polls for evidence JSON + marker file (200ms intervals)
- Uses environment variable (`M13_RESULT_DIR`) to pass evidence directory
- Timeouts: Build 180s, Player 60s
- **Status:** Stable, test-covered (11 unit + 1 integration), shared interface `IRuntimeRunner`

### 1.3 Key Design Decisions (M13)

1. **Player Build is the primary route** — not Editor PlayMode. Rationale: `isEditor=false` is the only reliable discriminator between true standalone Runtime and Editor PlayMode.
2. **Environment variable guard** — The Player-side `[RuntimeInitializeOnLoadMethod]` guards on an env var, so it activates only during proper inspection runs, never during normal gameplay.
3. **PID-level process management** — Only kills tracked child processes, never uses `killall Unity`.
4. **Evidence JSON protocol** — Minimal 5-field payload: `executionMode`, `activeScene`, `isEditor`, `isPlaying`, `success`.
5. **Marker file + evidence file** — Redundant signaling for robustness.
6. **Self-quitting Player** — The probe script calls `Application.Quit()` after writing evidence, so the process terminates naturally.

### 1.4 Current Integration with Rule Engine

**None.** The Runtime namespace (`src/UnityProjectInspector.Core/Runtime/`) is completely isolated from:
- `Models/Rules/` — IRule, RuleResult, InspectionContext, RuleDefinition
- `Rules/` — RuleEngine, RuleFactory, all 8 rule types
- `Parsing/` — SceneParser, CSharpAnalyzer, UnityEventCodeLinker

The `InspectionContext` carries only static data: `ProjectInfo` (parsed scenes + YAML), `EventBindings`, `CodeLinks`, `GameObjectScriptNames`.

### 1.5 Current Test Coverage

| Test File | # Tests | Category | Scope |
|-----------|---------|----------|-------|
| `RuntimeRunnerTests.cs` | 11 | Unit | Evidence classification, JSON deserialization, options defaults |
| `RuntimeIntegrationTests.cs` | 1 | Integration | Full pipeline: Unity Build → Player → evidence (real M12 project) |
| `RuntimePocTests.cs` | (M10) | Integration | Editor PlayMode via UnityRuntimeAdapter |

**Total: 204 tests pass (all categories).** 0 errors, 0 warnings.

---

## 2. RuntimeRunner Boundary (Question 1)

> **Q1: Can RuntimeRunner serve as the base for the full Runtime Flow?**

**Answer: Yes, with three non-breaking architectural extensions.**

### 2.1 What RuntimeRunner Already Provides

```
BuildPlayerAsync()       → BuildFailed | Passed (with Player path)
    ↓
LaunchAndPollAsync()     → Passed | Failed | Timeout | ProcessExited | NotEvaluated
    ↓
ClassifyEvidence()       → RuntimeSession with result + evidence
```

This pipeline is the correct **infrastructure layer** for Runtime Flow. It handles:
- Process lifecycle (start, monitor, kill, PID tracking)
- Timeout management (build timeout, player timeout)
- Evidence protocol (JSON + marker file, polling)
- Result classification (5-status enum)
- Error recovery (build failed → no launch attempted, process exited → no wait)

### 2.2 Three Extensions Required

#### Extension 1: Runtime Action Pipeline (not single evidence)

Current `RuntimeRunner` runs exactly **one** probe (one `[RuntimeInitializeOnLoadMethod]` check) and returns. The full Runtime Flow needs to orchestrate **multiple sequential actions**: launch → wait → click → assert scene → click → assert panel → observe logs → quit.

```
Current:   Build → [Launch + Poll] → Classify
Future:    Build → Launch → Action_1 → Action_2 → ... → Action_N → Classify → Quit
```

This means extending the poll phase from "wait for one evidence file" to "execute a sequence of Runtime Actions."

#### Extension 2: Two-mode support (Editor PlayMode + Player Build)

The architecture must support **two launch modes**:

| Mode | Command | Evidence Type | Use Case |
|------|---------|--------------|----------|
| Player Build | `Unity -executeMethod Build → Launch Player` | `PlayerEvidence` (true Runtime) | **Primary: Real verification** |
| Editor PlayMode | `Unity -projectPath -executeMethod` | Unity log / `RuntimeEvidence` | **Secondary: Fast dev iteration** |

Design principle: One `IRuntimeRunner` interface, two implementations (or one with a mode flag). The interface already supports this — just extend the pipeline.

#### Extension 3: Configurable Action/Assertion list (not hardcoded evidence shape)

Current `RuntimeRunner.ClassifyEvidence()` has hardcoded logic:
```csharp
if (!evidence.IsValidRuntimeEvidence) → Failed
if (!evidence.Success) → Failed
→ Passed
```

The future runner should accept a list of `RuntimeAction`/`RuntimeAssertion` objects and execute them against the running process.

### 2.3 Non-Breaking Evolution Path

```
M13 (now):     IRuntimeRunner, RuntimeRunner (Player Build only), hardcoded classify
               ↓
M14 MVP:       IRuntimeRunner, RuntimeRunner (Player Build + Editor PlayMode),
               RuntimeAction list, RuntimeAssertion list
               ↓
M15+:          Rule Engine integration (RuntimeAction as IRule or parallel track),
               richer evidence types, action chaining
```

**Key constraint:** RuntimeRunner's current `RunAsync()` signature must remain valid. New functionality is added as overloads or optional parameters.

---

## 3. RuntimeEvidence Assessment (Question 2)

> **Q2: Does RuntimeEvidence need to be extended?**

**Answer: Yes — the current model is too generic. Discriminated evidence types are required.**

### 3.1 Current RuntimeEvidence

```csharp
public class RuntimeEvidence
{
    public required string Type { get; init; }       // e.g. "RuntimeEvidence"
    public required string Expected { get; init; }   // e.g. "Runtime"
    public string? Observed { get; init; }            // e.g. "Runtime"
    public bool Obtained { get; init; }
    public string? Message { get; init; }
}
```

This is a **universal key-value evidence** model. It works for flat comparisons but breaks down for structured evidence.

### 3.2 Evidence Type Taxonomy

Each runtime assertion produces a specific evidence type with its own structure:

| Evidence Type | Fields | Source API |
|--------------|--------|-----------|
| `ActiveScene` | `sceneName: string` | `SceneManager.GetActiveScene().name` |
| `GameObjectActive` | `gameObjectName, isActive: bool` | `GameObject.activeInHierarchy` |
| `ComponentProperty` | `gameObjectName, componentType, propertyName, actualValue` | Reflection / known API |
| `ButtonClick` | `buttonGameObjectName, success: bool` | `Button.onClick.Invoke()` |
| `SceneTransition` | `fromScene, toScene, success: bool` | `SceneManager.activeSceneChanged` |
| `AnimatorState` | `gameObjectName, layer, expectedState, actualState` | `Animator.GetCurrentAnimatorStateInfo` |
| `LogEvidence` | `logType, messagePattern, count` | `Application.logMessageReceived` |
| `RuntimeException` | `exceptionType, message, stackTrace` | Unity error log |
| `EvidenceChain` (M13 current) | `executionMode, isEditor, isPlaying, activeScene, success` | Combined probe |

### 3.3 Proposed Evidence Hierarchy

```csharp
// ─── Base ───
public abstract record RuntimeEvidence
{
    public required string EvidenceType { get; init; }
    public bool Obtained { get; init; }
    public string? Message { get; init; }
}

// ─── Discriminated types ───
public sealed record ActiveSceneEvidence : RuntimeEvidence
{
    public required string SceneName { get; init; }
}

public sealed record GameObjectActiveEvidence : RuntimeEvidence
{
    public required string GameObjectName { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record ComponentPropertyEvidence : RuntimeEvidence
{
    public required string GameObjectName { get; init; }
    public required string ComponentType { get; init; }
    public required string PropertyName { get; init; }
    public string? ActualValue { get; init; }
    public string? ExpectedValue { get; init; }
}

// ⚠️ Design note: record inheritance with JSON polymorphism is
// complex (discriminator field, custom converter). Alternative:
// keep RuntimeEvidence as a flat model with optional typed fields
// (variant pattern). Decision deferred to M14 implementation.
```

### 3.4 Impact on RuntimeSession

`RuntimeSession.Evidence` currently is `List<RuntimeEvidence>`. This container is correct — just the items need richer structure.

### 3.5 Impact on PlayerEvidence

`PlayerEvidence` (M13 JSON model) does not need to change. It is specifically the **probe JSON protocol** for the `[RuntimeInitializeOnLoadMethod]` mechanism. Future runtime actions will use different JSON shapes or direct API calls (in Editor PlayMode mode).

---

## 4. Proposed Runtime Actions (Question 3)

> **Q3: What is the minimum set of Runtime Actions?**

**Answer: 6 core actions form the minimum viable set.**

### 4.1 Action Model

```csharp
/// <summary>
/// A single operation to execute against a running Unity process.
/// Actions are synchronous or short-lived polling operations.
/// </summary>
public abstract record RuntimeAction
{
    public required string ActionId { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Launches the Unity process (Editor or Player).
/// </summary>
public sealed record LaunchAction : RuntimeAction
{
    public string ProjectPath { get; init; } = string.Empty;
    public string? SceneToLoad { get; init; }
}

/// <summary>
/// Waits for a condition or time duration.
/// </summary>
public sealed record WaitAction : RuntimeAction
{
    public int Milliseconds { get; init; } = 200;
    public int Frames { get; init; } = 0;
}

/// <summary>
/// Clicks a UI Button by GameObject name.
/// Implementation: GameObject.Find() → GetComponent&lt;Button&gt;() → onClick.Invoke()
/// </summary>
public sealed record ClickButtonAction : RuntimeAction
{
    public required string GameObjectName { get; init; }
}
```

### 4.2 Action Sequencing Model

```csharp
/// <summary>
/// A runtime test is a chain of actions.
/// Each action produces zero or more RuntimeEvidence items.
/// </summary>
public record RuntimeTestScript
{
    public required string Name { get; init; }
    public List<RuntimeAction> Actions { get; init; } = new();
    public List<RuntimeAssertion> Assertions { get; init; } = new();
}
```

### 4.3 MVP Action Set (6 actions)

| # | Action | Purpose | Implementation Complexity | Evidence Produced |
|---|--------|---------|--------------------------|-------------------|
| 1 | `Launch` | Start Unity (Editor PlayMode or Player Build) | High (process mgmt) | Session start |
| 2 | `Wait` | Wait N ms or N frames | Low | None |
| 3 | `ClickButton` | Click UI Button by GameObject name | Low-Medium | ButtonClickEvidence |
| 4 | `ObserveActiveScene` | Read current active scene | Low | ActiveSceneEvidence |
| 5 | `ObserveGameObjectActive` | Read `activeInHierarchy` | Low | GameObjectActiveEvidence |
| 6 | `ReadLogs` | Check for runtime errors | Low-Medium | LogEvidence, RuntimeExceptionEvidence |

### 4.4 Action Execution Contract

```
For each action in script:
  1. Execute action (synchronous or short poll)
  2. Collect evidence (if any)
  3. On failure: log error, continue (do NOT abort the script)
  4. On exception: wrap in NotEvaluated evidence, continue
After all actions:
  5. Run assertions against collected evidence
  6. Classify: Passed if all assertions satisfied, Failed otherwise
```

### 4.5 Implementability Assessment

Each action maps to Unity API calls that can be made from:
- **Editor PlayMode:** Direct C# calls in an Editor script (`-executeMethod`)
- **Player Build:** Injected probe GameObject with a component that listens for commands

Both modes are implementable. Editor PlayMode is simpler (direct API access). Player Build requires a communication mechanism (file-based or environment-variable-based).

---

## 5. Proposed Runtime Assertions (Question 4)

> **Q4: What is the minimum set of Runtime Assertions?**

**Answer: 5 core assertions form the minimum viable set.**

### 5.1 Assertion Model

```csharp
/// <summary>
/// A condition to verify against collected RuntimeEvidence.
/// Assertions are evaluated AFTER all actions in the script complete.
/// </summary>
public abstract record RuntimeAssertion
{
    public required string AssertionId { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Asserts that the active scene name matches.
/// </summary>
public sealed record AssertActiveScene : RuntimeAssertion
{
    public required string ExpectedSceneName { get; init; }
}

/// <summary>
/// Asserts that a GameObject's activeInHierarchy matches.
/// </summary>
public sealed record AssertGameObjectActive : RuntimeAssertion
{
    public required string GameObjectName { get; init; }
    public bool ExpectedActive { get; init; } = true;
}

/// <summary>
/// Asserts that no runtime exceptions occurred during the observation window.
/// </summary>
public sealed record AssertNoExceptions : RuntimeAssertion
{
    // If patterns is non-empty, only these exception types are checked
    public List<string> IgnoredPatterns { get; init; } = new();
}

/// <summary>
/// Asserts a component property equals an expected value.
/// </summary>
public sealed record AssertComponentProperty : RuntimeAssertion
{
    public required string GameObjectName { get; init; }
    public required string ComponentType { get; init; }
    public required string PropertyName { get; init; }
    public string? ExpectedValue { get; init; }
}

/// <summary>
/// Asserts that clicking a button resulted in a scene transition.
/// Combines ClickButton action + AssertActiveScene into one assertion
/// for common button→scene patterns.
/// </summary>
public sealed record AssertButtonTransitionsScene : RuntimeAssertion
{
    public required string ButtonGameObjectName { get; init; }
    public required string ExpectedSceneName { get; init; }
}
```

### 5.2 MVP Assertion Set (5 assertions)

| # | Assertion | Use Case | Implementation |
|---|-----------|----------|---------------|
| 1 | `AssertActiveScene` | Verify scene loaded correctly | Compare evidence `activeScene` |
| 2 | `AssertGameObjectActive` | Verify panel/UI visibility | Compare evidence `isActive` |
| 3 | `AssertNoExceptions` | Verify no runtime errors | Scan log evidence collection |
| 4 | `AssertComponentProperty` | Verify specific value (e.g. score=0) | Compare evidence property value |
| 5 | `AssertButtonTransitionsScene` | Common click→scene pattern | Composite: click + wait + assert scene |

### 5.3 Assertion Evaluation Rules

```
For each assertion:
  1. Find matching evidence by type + target
  2. If no matching evidence → NotEvaluated (action may have failed)
  3. Compare evidence value against assertion expectation
  4. Match → Passed  |  Mismatch → Failed

Final result:
  ALL assertions Passed                      → RuntimeResultStatus.Passed
  ANY assertion Failed                       → RuntimeResultStatus.Failed
  ALL assertions NotEvaluated (no evidence)  → RuntimeResultStatus.NotEvaluated
  Mixed Passed + NotEvaluated                → RuntimeResultStatus.Passed (lenient)
```

### 5.4 Key Difference from M13 Classification

M13 classifies one `PlayerEvidence` against hardcoded rules. The future system classifies a collection of `RuntimeEvidence` items against a collection of `RuntimeAssertion` objects. This is more flexible but requires a **matching layer** between evidence and assertions.

---

## 6. Runtime Flow Model

### 6.1 Overall Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    IRuntimeRunner                             │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  Build Phase                                             │ │
│  │    Unity Editor → -executeMethod Build → Player Binaries  │ │
│  │    OR: Editor -projectPath -scene (for PlayMode)         │ │
│  └──────────────────────────────────────────────────────────┘ │
│                              ↓                                │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  Launch Phase                                            │ │
│  │    Start Player (or Editor PlayMode) with config         │ │
│  │    Inject RuntimeTestProbe (or use harness Editor script)│ │
│  └──────────────────────────────────────────────────────────┘ │
│                              ↓                                │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  Action Pipeline                                         │ │
│  │    Wait → Click(SceneButton) → ObserveActiveScene()     │ │
│  │    → Assert(Scene == "Maze2D") → ReadLogs()              │ │
│  │    → Wait → Click(BackButton) → ObserveActiveScene()     │ │
│  │    → Assert(Scene == "MainMenu") → ReadLogs()            │ │
│  └──────────────────────────────────────────────────────────┘ │
│                              ↓                                │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  Classification Phase                                    │ │
│  │    All assertions met? → Passed                          │ │
│  │    Any assertion failed? → Failed                        │ │
│  │    Precondition not met? → NotEvaluated                  │ │
│  └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 6.2 Two Launch Modes

```
                        ┌─────────────────────────────┐
                        │   IRuntimeRunner.RunAsync()  │
                        │   (accepts RuntimeTestScript)│
                        └──────────┬──────────────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    ▼                             ▼
        ┌───────────────────┐          ┌──────────────────────┐
        │ Editor PlayMode   │          │ Player Build Mode    │
        │ (Fast iteration)  │          │ (True Runtime)       │
        ├───────────────────┤          ├──────────────────────┤
        │ Unity -batchmode  │          │ Unity -executeMethod │
        │   -noGraphics     │          │   Build → Launch     │
        │   -executeMethod  │          │   Player executable  │
        │   -projectPath    │          │                      │
        ├───────────────────┤          ├──────────────────────┤
        │ + Direct C# API   │          │ + Env var evidence   │
        │ + Fast (~30-60s)  │          │ + isEditor=false     │
        │ + isEditor=true   │          │ + Slower (~120-240s) │
        │                   │          │ + Definitively Runtime│
        ├───────────────────┤          ├──────────────────────┤
        │ Risk: Editor bugs │          │ Risk: Build failures  │
        │ mask Runtime bugs │          │ but results are real  │
        └───────────────────┘          └──────────────────────┘
```

**Design decision:** Player Build is the **authoritative** mode (truth). Editor PlayMode is **development-only** (fast feedback). The runtime test runner should default to Player Build and fall back to Editor PlayMode only when explicitly configured.

### 6.3 Process Timeline

```
T=0s     T=~30s      T=~35s      T=~36s     T=~38s     T=~40s
  │         │           │           │          │          │
  │  Build  │  Launch   │  Wait     │  Click   │  Observe │
  │  Unity  │  Player   │  0.5s     │  Button  │  Scene   │
  │  Editor │  Process  │           │          │          │
  │         │           │           │          │          │
  └─────────┴───────────┴───────────┴──────────┴──────────┴──▶ Time
       ↑                      ↑                      ↑
  BUILD PHASE            ACTION PHASE          CLASSIFY PHASE
```

The critical insight: **total runtime testing time is dominated by Unity startup and Player Build** (~30-180s). The actual action execution (click → observe → assert) is sub-second. This means:
- Each runtime test session incurs a ~2-3 minute fixed cost
- Within a session, many actions can be chained cheaply
- Design tests as "one session, many actions" rather than "many sessions"

---

## 7. Rule Engine Integration (Question 5)

> **Q5: How does Runtime Flow integrate with the Rule Engine?**

**Answer: Two parallel pipelines, merged at the report level.**

### 7.1 The Integration Problem

The existing Rule Engine has these architectural properties:

| Property | Static Rule Engine | Runtime Flow |
|----------|------------------|-------------|
| Execution | Synchronous (`RuleResult Evaluate()`) | Asynchronous (`Task<RuntimeSession> RunAsync()`) |
| Context | `InspectionContext` (parsed structures) | `RuntimeRunOptions` (process config) |
| State | Stateless | Stateful (running Player process) |
| Evidence | `RuntimeEvidence` (post-hoc) | `PlayerEvidence` + `RuntimeEvidence` (live) |
| Scope | One evaluation per rule | Multiple actions per session |
| Time | Milliseconds | Minutes |

These differences mean Runtime Flow cannot simply be "a new IRule type." The async nature and stateful process lifecycle are fundamentally incompatible with `IRule.Evaluate(InspectionContext)`.

### 7.2 Recommended Integration Model: Parallel Pipeline + Result Merge

```
┌──────────────────────────────┐    ┌──────────────────────────────┐
│      Static Pipeline         │    │      Runtime Pipeline        │
│                              │    │                              │
│  RuleEngine.Run(context)     │    │  RuntimeRunner.RunAsync()   │
│       ↓                      │    │       ↓                      │
│  Rule 1 (SceneExists)        │    │  Build Phase                 │
│  Rule 2 (GameObjectExists)   │    │  Action Pipeline             │
│  Rule 3 (UnityEventBinding)  │    │    Click → Observe → Assert  │
│  Rule 4 (CodeEvidence)       │    │  Classification Phase        │
│       ↓                      │    │       ↓                      │
│  List<RuleResult>            │    │  RuntimeSession              │
│       ↓                      │    │       ↓                      │
│       └──────────┬───────────┘    └──────────┬──────────────────┘
│                  ▼                           ▼
│      ┌───────────────────────────────────────────┐
│      │         Result Merger                     │
│      │  Takes StaticResults + RuntimeSession     │
│      │  Produces CompositeInspectionResult       │
│      └───────────────────────────────────────────┘
```

### 7.3 Merged Result Model

```csharp
public record CompositeInspectionResult
{
    // Static rules (always present)
    public List<RuleResult> StaticResults { get; init; } = new();

    // Runtime session (may be NotEvaluated if build failed)
    public RuntimeSession? RuntimeSession { get; init; }

    // Convenience: is runtime evidence available?
    public bool HasRuntimeResults =>
        RuntimeSession?.Result != RuntimeResultStatus.NotEvaluated
        && RuntimeSession?.Result != RuntimeResultStatus.BuildFailed;
}
```

### 7.4 Why NOT RuntimeAction as IRule

Considered and rejected:

| Approach | Problem |
|----------|---------|
| `IRuntimeRule : IRule` with async `EvaluateAsync()` | Breaks `RuleEngine.Run()` which calls sync `Evaluate()`. Would need a separate async engine. |
| RuntimeAction as RuleDefinition type (e.g. `"RuntimeCheck"`) | `InspectionContext` has no process reference. Rule would need to spawn its own Unity process — violates stateless rule design. |
| RuntimeRunner wraps RuleEngine | Introduces circular dependency. Runtime depends on Rules, Rules depend on static context, Runtime has a process. |

### 7.5 Communication Contract

The Static Pipeline and Runtime Pipeline share **no internal state**. Their only contract is:

```
Static Pipeline Output (InspectionContext):
  - Which scenes/GameObjects/components exist (structural truth)
  - Which UnityEvent bindings are configured
  - Which code evidence chains are complete
  - Used by: grading rubric, structural validation

Runtime Pipeline Output (RuntimeSession):
  - Whether the Player builds and launches
  - Whether runtime actions execute successfully
  - Whether runtime assertions pass
  - Used by: behavioral validation, build validation
```

A rule definition like the following connects the two conceptually (but not architecturally):

```json
{
  "id": "scene.maze2d.transitions",
  "type": "UnityEventBinding",     // Static: check binding exists
  "target": "StartButton",
  "expectedMethod": "Start2D",
  "expectedClass": "GameManager"
},
{
  "id": "scene.maze2d.runtime",
  "type": "RuntimeAssertion",      // NEW type — interpreted by RuntimeRunner
  "target": "StartButton",
  "expectedMethod": "AssertButtonTransitionsScene",
  "expectedValue": "Maze2D"
}
```

The `CompositeInspectionResult` associates these by convention (same semantic ID) but keeps their execution separate.

---

## 8. Static vs Runtime Boundary

### 8.1 Boundary Table (Updated from M9 Audit)

| Category | # | Check | Static | Runtime | Decision |
|----------|---|-------|--------|---------|----------|
| **S** | 1 | Scene `.unity` file exists | ✅ `FileExistsRule` | ❌ | Static |
| **S** | 2 | Scene in Build Settings | ⚠️ Manual/not parsed | ✅ | Extend Static (parse EditorBuildSettings) |
| **S** | 3 | GameObject exists | ✅ `GameObjectExistsRule` | ❌ | Static |
| **S** | 4 | Component type on GO | ✅ `ComponentExistsRule` | ❌ | Static |
| **S** | 5 | MonoBehaviour script attached | ✅ `ScriptAttachedRule` | ❌ | Static |
| **S** | 6 | Parent-child hierarchy | ✅ `GameObjectHierarchyRule` | ❌ | Static |
| **S** | 7 | UnityEvent binding | ✅ `UnityEventBindingRule` | ❌ | Static |
| **S** | 8 | Method exists in C# | ✅ `CodeEvidenceRule` | ❌ | Static |
| **S** | 9 | Call expression in method | ✅ `CodeEvidenceRule` | ❌ | Static |
| **S+** | 10 | Button click → LoadScene | ✅ Static chain | ✅ | **Both** (Static proves setup, Runtime proves execution) |
| **S+** | 11 | Scene actually loads at runtime | ❌ Intent only | ✅ | **Runtime required** |
| **S+** | 12 | Panel visibility toggles | ❌ Intent only | ✅ | **Runtime required** |
| **S+** | 13 | Button click → side effect | ❌ Cannot prove dispatch | ✅ | **Runtime required** |
| **R** | 14 | No runtime exceptions | ❌ Impossible | ✅ | **Runtime required** |
| **R** | 15 | Project builds successfully | ❌ No Unity env | ✅ | **Runtime required** |
| **R** | 16 | Animation state transitions | ❌ Config unknown | ✅ | **Runtime required** |
| **R** | 17 | Physics collisions fire | ❌ Non-deterministic | ✅ | **Runtime required** |
| **R** | 18 | Coroutines complete | ❌ Indefinite yield | ✅ | **Runtime required** |
| **R** | 19 | Audio plays | ❌ Mute/volume unknown | ✅ | **Runtime required** |
| **R** | 20 | UI is actually visible (not occluded) | ❌ Rendering unknown | ⚠️ Partial | Deferred |

### 8.2 Clear Decision Rules

**Static-only (Level S) — 9 categories:**
Structural assertions. File/GameObject/Component/Script existence. These are cheap, fast, and reliable. **Do not test at runtime** (waste of build time).

**Both useful (Level S+) — 3 categories:**
Button→LoadScene, Scene transitions, Panel visibility. Static proves **intent** (setup is correct). Runtime proves **outcome** (it actually works). Both are meaningful — design the rubric to distinguish:
- "Did the student set up the button correctly?" → Static check
- "Does the scene actually change when you click?" → Runtime check

**Runtime-only (Level R) — 9 categories:**
These cannot be statically verified at all. The student's code may be syntactically perfect but the project crashes on launch, or a NullReferenceException prevents the click handler from executing. These are the strongest arguments for Runtime Flow.

### 8.3 Grading Strategy Recommendation

```
For each requirement:
  1. Run Static Rules (always — 0-2 seconds)
  2. If all prerequisite Static Rules pass:
       Run Runtime Flow (conditionally — 2-5 minutes)
     Else:
       Skip Runtime Flow (can't test runtime on a broken setup)
  3. Compose result:
     - Static: ALL S-level rules must Pass
     - Runtime: ALL executed R-level assertions must Pass
     - If Build Failed: mark all Runtime assertions as NotEvaluated
```

---

## 9. Example: 2D Maze

### 9.1 Scenario

A student creates a 2D Maze game with:
- MainMenu scene with a StartButton
- Clicking StartButton calls `GameManager.Start2D()` → `SceneManager.LoadScene("Maze2D")`
- Maze2D scene has a Player character with WASD movement

### 9.2 Static Analysis (Already Implemented)

| Rule | Input | Expected | Status |
|------|-------|----------|--------|
| `SceneExistsRule` | "MainMenu" | Pass | ✅ |
| `SceneExistsRule` | "Maze2D" | Pass | ✅ |
| `GameObjectExistsRule` | "StartButton" (in MainMenu) | Pass | ✅ |
| `ComponentExistsRule` | "StartButton" → "Button" | Pass | ✅ |
| `ScriptAttachedRule` | "GameManager" → "GameManager" | Pass | ✅ |
| `UnityEventBindingRule` | "StartButton" → "Start2D" → "GameManager" | Pass | ✅ |
| `CodeEvidenceRule` | "StartButton" → "Start2D" → `SceneManager.LoadScene("Maze2D")` | Pass | ✅ |

**Static verdict:** All structural checks pass. The button is wired to a method that calls LoadScene("Maze2D").

### 9.3 Static ↔ Runtime Boundary

| Aspect | Static Says | Runtime Measure | Gap |
|--------|-------------|----------------|-----|
| Button exists | ✅ Yes | N/A | None |
| Button → GameManager.Start2D | ✅ Binding | N/A | None |
| Start2D → LoadScene("Maze2D") | ✅ Code call | N/A | None |
| Maze2D scene exists | ✅ Yes | N/A | None |
| LoadScene("Maze2D") actually happens at runtime | ❌ Cannot prove | ✅ Click → scene == "Maze2D" | **Gap: Runtime required** |
| Player moves with WASD | ⚠️ Update() has Input.GetKey | ❌ Complex | **Gap: Runtime difficult** |
| No runtime exceptions | ❌ Impossible | ✅ Log check | **Gap: Runtime required** |

### 9.4 Runtime Test Script (Proposed)

```csharp
new RuntimeTestScript
{
    Name = "2DMaze_ClickStart_TransitionsToMaze2D",
    Actions =
    {
        new LaunchAction { ProjectPath = "...", SceneToLoad = "MainMenu" },
        new WaitAction { Milliseconds = 500 },
        new ClickButtonAction { GameObjectName = "StartButton" },
        new WaitAction { Milliseconds = 1000 },  // Wait for scene load
        new ObserveActiveSceneAction(),
        new ReadLogsAction(),
    },
    Assertions =
    {
        new AssertActiveScene { ExpectedSceneName = "Maze2D" },
        new AssertNoExceptions(),
    },
};
```

### 9.5 Expected Evidence Chain

```
1. [ProbeEvidence]  executionMode="Runtime", isEditor=false, isPlaying=true
2. [ActiveScene]    sceneName="MainMenu"          ← Before click
3. [ButtonClick]    buttonName="StartButton", success=true
4. [ActiveScene]    sceneName="Maze2D"             ← After click (observed)
5. [LogEvidence]    no errors/warnings             ← ReadLogs still clean
```

### 9.6 Key Insight

The 2D Maze example demonstrates the **critical gap** that Runtime Flow fills: the static pipeline can prove `LoadScene("Maze2D")` is *called*, but cannot prove the scene actually *loaded*. A missing entry in Build Settings (scene not added to Scenes In Build) would pass all static checks but fail at runtime.

---

## 10. Example: 3D Industrial Monitor

### 10.1 Scenario

The 3DIndustrialMonitor project has:
- MonitoringCanvas with PanelSwitcher script
- Btn_DeviceList → onClick → PanelSwitcher.ShowOverview() → SetActive(overviewPanel, true)
- OverviewPanel starts inactive (activeSelf = false)
- Clicking the button should make it visible

### 10.2 Static Analysis (Already Implemented)

| Rule | Input | Expected | Status |
|------|-------|----------|--------|
| `SceneExistsRule` | "SampleScene" | Pass | ✅ |
| `GameObjectExistsRule` | "Btn_DeviceList" | Pass | ✅ |
| `GameObjectExistsRule` | "OverviewPanel" | Pass | ✅ |
| `ComponentExistsRule` | "Btn_DeviceList" → "Button" | Pass | ✅ |
| `ScriptAttachedRule` | "MonitoringCanvas" → "PanelSwitcher" | Pass | ✅ |
| `UnityEventBindingRule` | "Btn_DeviceList" → "ShowOverview" → "PanelSwitcher" | Pass | ✅ |
| `CodeEvidenceRule` | "Btn_DeviceList" → "ShowOverview" → SetActive | PartiallyResolved | ⚠️ |

**Static verdict:** Binding existence confirmed. Code evidence partially resolved (ShowOverview method found, calls detected, but SetActive argument depends on which overload/variable).

### 10.3 Static ↔ Runtime Boundary

| Aspect | Static Says | Runtime Measure | Gap |
|--------|-------------|----------------|-----|
| Btn_DeviceList exists | ✅ Yes | N/A | None |
| Btn → PanelSwitcher.ShowOverview | ✅ Binding | N/A | None |
| ShowOverview exists | ✅ Yes | N/A | None |
| ShowOverview calls SetActive | ⚠️ Yes (PartiallyResolved) | N/A | Partial (args unclear) |
| OverviewPanel exists | ✅ Yes | N/A | None |
| OverviewPanel starts inactive | ⚠️ activeSelf=false in YAML | ✅ `activeInHierarchy==false` | Small |
| Click → Panel visible | ❌ Cannot prove | ✅ Click → activeHierarchy==true | **Gap: Runtime required** |
| No runtime exceptions | ❌ Impossible | ✅ Log check | **Gap: Runtime required** |

### 10.4 Runtime Test Script (Proposed)

```csharp
new RuntimeTestScript
{
    Name = "Monitor_BtnDeviceList_ShowsOverviewPanel",
    Actions =
    {
        new LaunchAction { ProjectPath = "..." },
        new WaitAction { Milliseconds = 500 },
        new ObserveGameObjectActiveAction { GameObjectName = "OverviewPanel" },
        new ClickButtonAction { GameObjectName = "Btn_DeviceList" },
        new WaitAction { Milliseconds = 500 },
        new ObserveGameObjectActiveAction { GameObjectName = "OverviewPanel" },
        new ReadLogsAction(),
    },
    Assertions =
    {
        new AssertGameObjectActive { GameObjectName = "OverviewPanel", ExpectedActive = false },
        new AssertGameObjectActive { GameObjectName = "OverviewPanel", ExpectedActive = true },
        new AssertNoExceptions(),
    },
};
```

### 10.5 Static vs Runtime: Which Is Better for Grading?

**Key question for M14 implementation:**

> If the static pipeline proves that the UnityEvent binding exists, the method exists, and the method calls `SetActive` — is the runtime test worth the 2-3 minute build cost?

**Answer depends on the grading context:**

| Grading Context | Static Enough? | Runtime Worth It? |
|----------------|---------------|-------------------|
| "Did you set up the button correctly?" | ✅ Yes | ❌ Not needed |
| "Does your UI actually work?" | ❌ No | ✅ Yes |
| "Is your code well-structured?" | ✅ Yes (static code analysis) | ❌ Not needed |
| "Does the game run without errors?" | ❌ No | ✅ Yes |
| "Does the panel appear when clicked?" | ⚠️ Partial (strong intent) | ✅ **Yes, if behavioral proof matters** |

**Recommendation:** For the M14 MVP, Runtime Flow is **worth it** for:
1. **Build verification** — Does the project actually compile and run?
2. **Runtime exception detection** — Are there crashes?
3. **Scene transitions** — Does clicking a button actually change the scene?

For UI panel visibility (like OverviewPanel), runtime is desirable but can be deferred to M15.

---

## 11. MVP Recommendation

### 11.1 Minimal Viable Runtime Flow

```
Phase 1: Unify Launch Modes (M14.x)
  Extend RuntimeRunner to support both:
    - Player Build Mode (existing)
    - Editor PlayMode (migrate UnityRuntimeAdapter into IRuntimeRunner)
  Outcome: Single RunAsync() with a mode parameter

Phase 2: Runtime Action Pipeline (M14.x)
  Add RuntimeTestScript, RuntimeAction, RuntimeAssertion models
  Replace hardcoded ClassifyEvidence() with assertion-based classification
  Outcome: Configurable test scripts instead of fixed evidence shape

Phase 3: Static + Runtime Merge (M15)
  Add CompositeInspectionResult
  Add ResultMerger to combine RuleEngine output + RuntimeSession
  Add rule definition support for RuntimeAssertion types
  Outcome: Single inspection call produces both static and runtime results
```

### 11.2 Minimum Code Changes for MVP

| File | Change | Impact |
|------|--------|--------|
| `Runtime/RuntimeRunner.cs` | Add mode parameter (Player/Editor) | Non-breaking (default = Player) |
| `Runtime/IRuntimeRunner.cs` | No change | Interface stays stable |
| `Runtime/UnityRuntimeAdapter.cs` | Refactor into RuntimeRunner or deprecate | Delete after migration |
| `Runtime/RuntimeRunOptions.cs` | Add `LaunchMode` enum | Non-breaking (default = PlayerBuild) |
| `Runtime/RuntimeEvidence.cs` | Add discriminated subclasses | Non-breaking (add new types) |
| `Runtime/RuntimeSession.cs` | No change | Container stays stable |
| **New: `Runtime/RuntimeAction.cs`** | Action models | New file |
| **New: `Runtime/RuntimeAssertion.cs`** | Assertion models | New file |
| **New: `Runtime/RuntimeTestScript.cs`** | Script model | New file |
| **New: `Runtime/ResultMerger.cs`** | Static + Runtime merge | New file |

**Total: ~5 new model files, ~3 file modifications, 0 breaking changes.**

### 11.3 Recommended M14 Implementation Scope (Next Milestone)

| Task | Effort | Priority |
|------|--------|----------|
| Extend RuntimeRunner to support Editor PlayMode | Medium | P0 — Required for fast iteration |
| Add RuntimeAction models (Launch, Wait, ClickButton, ObserveActiveScene, ReadLogs) | Low | P0 — Required |
| Add RuntimeAssertion models (AssertActiveScene, AssertNoExceptions) | Low | P0 — Required |
| Replace hardcoded ClassifyEvidence with assertion-driven classification | Medium | P0 — Required |
| Add Integration tests: Editor PlayMode mode | Medium | P1 |
| Add Integration tests: Action pipeline with multiple actions | Medium | P1 |
| Add Unit tests: All new models (serialization, defaults) | Low | P1 |
| UnityRuntimeAdapter deprecation notice | Low | P2 |

**Not in M14 scope:** ResultMerger, CompositeInspectionResult, RuntimeAssertion as RuleDefinition type. These are M15.

---

## 12. Deferred Features

### 12.1 Animator State Verification
**Defer to M16+**
- Requires polling `Animator.GetCurrentAnimatorStateInfo()` at the right frame
- Animator transitions are frame-dependent and non-deterministic
- Use case: verifying "Jump" animation plays on space
- Fallback: static rule can verify `Animator.SetTrigger("Jump")` call exists

### 12.2 Physics Collision Verification
**Defer to M17+**
- Requires controlled placement of objects and waiting for FixedUpdate ticks
- Non-deterministic across platforms and frame rates
- Use case: verifying OnCollisionEnter fires when Player hits Enemy
- Fallback: static rule can verify OnCollisionEnter method exists

### 12.3 Coroutine / Async Completion
**Defer indefinitely (may never implement)**
- Coroutine completion is time-sensitive and flaky
- Async operations (Addressables, UnityWebRequest) add network dependency
- Better suited for project-specific tests, not generic Inspector
- Fallback: none — this is inherently hard to automate

### 12.4 Input System Simulation
**Defer to M16+**
- Keyboard/mouse input requires system-level simulation (not just UI.Button.click)
- New Input System package has different APIs than legacy Input Manager
- Use case: player movement (WASD), mouse look
- Better approach: direct method invocation rather than input simulation
- Fallback: `ClickButtonAction` for UI buttons only (no raw keyboard)

### 12.5 UI Visibility (Occlusion Detection)
**Deferred indefinitely**
- Whether a UI element is visible on screen depends on camera, sorting order, and overlap
- Requires raycasting or screenshot analysis — both complex and fragile
- M9 audit correctly classified this as "partial" at runtime
- Fallback: `activeInHierarchy` check is a reasonable (imperfect) proxy

### 12.6 Multiple Scenes / Scene Management
**Defer to M15+**
- `SceneManager.LoadScene` with additive vs single mode
- DontDestroyOnLoad objects crossing scene boundaries
- Scene loading progress (async vs sync)
- M14 scope: single scene transitions only (`LoadScene` mode=single)

---

## 13. Out of Scope

The following are explicitly **not** part of the Runtime Flow architecture:

1. **Screenshot capture / visual analysis** — No image processing, no OCR, no visual regression testing
2. **AI-driven exploration** — No agent that autonomously explores the scene looking for bugs
3. **Network / multiplayer testing** — No Photon, Mirror, Netcode, or custom networking
4. **Performance profiling** — No FPS, memory, draw call, or GPU metrics
5. **Mobile / console platform testing** — macOS Standalone only (via 2022.3.62f3c1)
6. **WebGL / IL2CPP builds** — Standalone macOS only (Mono)
7. **Asset bundle / Addressables verification** — No dynamic asset loading
8. **GUI / visual editor** — All configuration is code-first or JSON
9. **User interaction recording / playback** — No Input playback system
10. **Custom Unity package dependencies** — Only built-in Unity APIs

These are ruled out by the project scope (homework/assignment grading) and technical constraints (no Unity license, batchmode only, macOS arm64 host).

---

## 14. Final Architecture Decision

> **Recommended Architecture: Two-Pipeline Model with Merge Point**

```
┌─────────────────────────────────────────────────────────────────┐
│                     InspectionPipeline                           │
│                                                                  │
│  ┌─────────────────────┐    ┌──────────────────────────────┐    │
│  │   Static Pipeline   │    │      Runtime Pipeline        │    │
│  │                     │    │                              │    │
│  │  RuleEngine.Run()   │    │  RuntimeRunner.RunAsync()   │    │
│  │  (synchronous)      │    │  (Task-based, async)        │    │
│  │                     │    │                              │    │
│  │  8 existing rules   │    │  RuntimeTestScript           │    │
│  │  + future rules     │    │    → List<RuntimeAction>     │    │
│  │                     │    │    → List<RuntimeAssertion>  │    │
│  │  Uses:              │    │                              │    │
│  │   InspectionContext │    │  Two launch modes:           │    │
│  │   (parsed data)     │    │    • Player Build (default)  │    │
│  │                     │    │    • Editor PlayMode (dev)   │    │
│  │  Output:            │    │                              │    │
│  │   List<RuleResult>  │    │  Output:                     │    │
│  │                     │    │   RuntimeSession             │    │
│  └─────────┬───────────┘    └──────────┬───────────────────┘    │
│            │                           │                        │
│            └──────────┬────────────────┘                        │
│                       ▼                                         │
│            ┌──────────────────────┐                             │
│            │   CompositeResult    │                             │
│            │   (static + runtime) │                             │
│            └──────────────────────┘                             │
│                                                                  │
│  Key principle: Independent execution, merged at report level.  │
│  Static and Runtime DO NOT share internal state.                 │
└─────────────────────────────────────────────────────────────────┘
```

### Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Launch mode | **Player Build primary**, Editor PlayMode secondary | `isEditor=false` is only reliable discriminator |
| RuntimeRunner as base | **Yes**, with extensions | Pipeline (Build→Launch→Poll→Classify) is correct |
| Action model | **New RuntimeAction/ActionScript types** | Not IRule — async, stateful pipeline incompatible |
| Assertion model | **New RuntimeAssertion types** | Evaluated post-hoc against collected evidence |
| Integration with Rule Engine | **Parallel pipelines + CompositeResult** | Minimal coupling, clean separation of concerns |
| Evidence types | **Discriminated subclasses** | Flat key-value is too generic for structured data |
| UnityRuntimeAdapter | **Deprecate, merge into RuntimeRunner** | Two separate approaches is tech debt |
| Build verification | **Always run before Runtime Flow** | Prerequisite: project must compile |
| Runtime exception detection | **Include in MVP** | Strongest argument for runtime testing |
| Input simulation | **Defer to M16+** | Complex, limited value for homework grading |

### Result Flow Summary

```
User submits Unity project
         │
         ▼
┌──────────────────────┐
│  1. Scan & Parse      │  UnityProjectScanner, SceneParser
│  2. Build Context     │  InspectionContext with all static data
│  3. Run Static Rules  │  RuleEngine — 8+ rules, 0-2 seconds
└──────────────────────┘
         │
         ├── All prereq rules pass? ──no──▶ Skip Runtime, report static-only
         │
         ▼ yes
┌──────────────────────┐
│  4. Run Runtime Flow  │  RuntimeRunner — 2-5 minutes
│     a. Build Player   │  Unity CLI build (or skip if cached)
│     b. Launch Player  │  Standalone Player process
│     c. Execute Script │  List<RuntimeAction>
│     d. Classify       │  List<RuntimeAssertion>
└──────────────────────┘
         │
         ▼
┌──────────────────────┐
│  5. Merge Results     │  CompositeInspectionResult
│  6. Generate Report   │  Static + Runtime evidence combined
└──────────────────────┘
```

---

## Audit Summary

| Check | Status |
|-------|--------|
| Baseline: `e858cbf`, main, clean | ✅ |
| Runtime/ directory read (8 files) | ✅ |
| Models/Rules/ read (6 files) | ✅ |
| Rules/ read (11 files) | ✅ |
| Parsing/ read (5 files) | ✅ |
| Tests/ read (2 test files) | ✅ |
| M9 audit reviewed | ✅ |
| M12 experiment data reviewed | ✅ |
| Question 1 (RuntimeRunner as base) answered | ✅ |
| Question 2 (RuntimeEvidence extension) answered | ✅ |
| Question 3 (Runtime Action minimum set) answered | ✅ |
| Question 4 (Runtime Assertion minimum set) answered | ✅ |
| Question 5 (Rule Engine integration) answered | ✅ |
| Example: 2D Maze analyzed | ✅ |
| Example: 3D Industrial Monitor analyzed | ✅ |
| Single recommended architecture | ✅ Two-Pipeline Model with Merge Point |
| MVP scope defined | ✅ 3 phases, ~8 files |
| Core modified (src/) | **NO** |
| Tests modified | **NO** |
| Experiments modified | **NO** |
| 3DIndustrialMonitor modified | **NO** |
| Git commit | **NO — read-only audit** |
| Git push | **NO** |

---

*End of M14 Runtime Flow Architecture Audit — Pure Architecture Analysis, No Code Modifications.*