# Milestone 9 — Runtime Boundary Audit

## Baseline
- **Commit:** ca6127e
- **Branch:** main
- **Working Tree:** clean

## Audit Scope

**Projects inspected:**
- UnityProjectInspector (source code and full test suite)
- 3DIndustrialMonitor (real Unity project, read-only)

**Core components inspected:**
- UnityProjectScanner — project validity + scene discovery
- SceneParser — GoB/Component/hierarchy from YAML
- ScriptResolver — GUID → .cs file via .meta scanning
- CSharpAnalyzer — Roslyn syntax-only analysis
- UnityEventParser — m_PersistentCalls extraction
- UnityEventCodeLinker — full evidence chain linker
- 8 existing Rules — SceneExists, GameObjectExists, ComponentExists, GameObjectHierarchy, ScriptAttached, FileExists, UnityEventBinding, CodeEvidence
- RuleEngine — stateless rule executor
- 180 tests (155 existing + 25 Milestone 8)

---

## Static / Runtime Capability Matrix

| # | Requirement | Static Evidence | Static Sufficient | Runtime Required | Runtime Evidence | Automation Difficulty |
|---|---|---|---|---|---|---|
| 1 | Scene `.unity` file exists | File on disk | ✅ Yes | No | N/A | N/A |
| 2 | Scene is in Unity project | `ProjectSettings/EditorBuildSettings.asset` not parsed (manual) | ⚠️ No — we don't parse BuildSettings | ✅ Yes — or extend static | SceneManager.GetSceneByBuildIndex | Low |
| 3 | GameObject with name exists | SceneParser yields GameObjectInfo | ✅ Yes | No | N/A | N/A |
| 4 | Component type exists on GO | ComponentInfo in scene YAML | ✅ Yes | No | N/A | N/A |
| 5 | MonoBehaviour (specific script) attached | ScriptGuid + ScriptResolver | ✅ Yes (with ScriptMap) | No | N/A | N/A |
| 6 | GO is direct child of parent GO | ParentFileId from Transform | ✅ Yes | No | N/A | N/A |
| 7 | UnityEvent binding exists | YAML m_PersistentCalls parsed | ✅ Yes | No | N/A | N/A |
| 8 | Target method name exists in C# | CSharpAnalyzer extracts method list | ✅ Yes | No | N/A | N/A |
| 9 | Method calls target API (e.g. `LoadScene`) | CSharpAnalyzer invocation expression | ✅ Yes (syntax-level) | No | N/A | N/A |
| 10 | Scene loads on runtime | LoadScene call in source | ❌ No — intent only | ✅ Yes | SceneManager.activeScene | Low |
| 11 | Button click → method fires | UnityEvent + method + code evidence | ❌ No — cannot prove runtime dispatch | ✅ Yes | Method side-effect observable | Medium |
| 12 | UI panel becomes visible after click | Code shows SetActive(true) | ❌ No — conditional or disabled | ✅ Yes | GameObject.activeInHierarchy | Low |
| 13 | Animator state transitions | Animator.Play() call in source | ❌ No — state machine config unknown | ✅ Yes | Animator.GetCurrentAnimatorStateInfo | Medium |
| 14 | Rigidbody actually moves | AddForce/Velocity call in source | ❌ No — physics depends on FixedUpdate | ✅ Yes | Rigidbody.velocity magnitude | Medium |
| 15 | Collision/Trigger fires | OnCollisionEnter/OnTriggerEnter defined | ❌ No — may not actually collide | ✅ Yes | OnCollisionEnter observed / Debug log | Medium |
| 16 | Audio plays | AudioSource.Play() in source | ❌ No — mute, volume, listener | ✅ Yes | AudioSource.isPlaying | Medium |
| 17 | Timer/coroutine completes correctly | StartCoroutine call in source | ❌ No — may yield indefinitely | ✅ Yes | Observable state after expected duration | High |
| 18 | Runtime exception (NullReference, etc.) | Cannot detect statically | ❌ No — impossible | ✅ Yes | Unity Log Console or crash | Low |
| 19 | Game state changes correctly | State machine enum + switch in code | ❌ No — cannot trace runtime path | ✅ Yes | Observable state variable value | Medium |
| 20 | Project builds without error | No build validation (no Unity) | ❌ No — cannot compile | ✅ Yes | Build output / log | Medium |
| 21 | XR grab interaction config | XR components in scene YAML | ⚠️ Partial — config lacking semantic analysis | ✅ Partial — hard without runtime | XR Interaction Manager events | Very High |
| 22 | UI is visible (not occluded) | RectTransform + CanvasRenderer exist | ❌ No — rendering depends on camera, sorting, occlusion | ✅ Partial — raycast check possible | GraphicRaycaster / event system | High |
| 23 | Async operation (Addressables, etc.) | Async method call in source | ❌ No — cannot trace completion | ✅ Yes | Post-operation observable state | High |

---

## Key Boundary Findings

### Static-only (Level S)
*These conclusions can be drawn from static analysis alone with high confidence:*

1. **Scene file existence** — File on disk at expected path. The `FileExistsRule` already handles this.
2. **GameObject existence** — GameObject with exact name found in `.unity` YAML. The `GameObjectExistsRule` handles this.
3. **Component attachment** — Component type (Transform, Camera, Canvas, MonoBehaviour, etc.) present on GameObject in scene YAML. Handled by `ComponentExistsRule`.
4. **Script attachment by class name** — MonoBehaviour ComponentInfo has ScriptGuid, which ScriptResolver maps to ScriptInfo.ScriptName (file name without .cs). Handled by `ScriptAttachedRule`.
5. **Parent-child hierarchy** — ParentFileId from Transform.m_Father chain resolved to parent GameObject name. Handled by `GameObjectHierarchyRule`.
6. **UnityEvent binding** — YAML m_PersistentCalls → source component → target component → target method name + TargetAssemblyTypeName. Handled by `UnityEventBindingRule`.
7. **Method existence in C# source** — CSharpAnalyzer extracts method declarations from syntax tree. Handled by `UnityEventCodeLinker` → `CodeEvidenceRule`.
8. **Code call expression existence** — Invocation expressions extracted from method body. Handled by `CodeEvidenceRule` (with argument checking).
9. **File path existence under project** — Relative path resolved against project root, with path traversal protection. Handled by `FileExistsRule`.

**Total static-only categories: 9.**
These are the current 8 rules (plus FileExists which overlaps with rule 1).

### Static evidence but not behavioral proof (Level S+)
*These have strong static indicators but DO NOT prove runtime behavior:*

1. **Button → UnityEvent → Method → LoadScene("Maze2D")**
   - Static: Full evidence chain exists in code and scene YAML
   - Cannot prove: Button click actually triggers LoadScene at runtime
   - Gap sources: Script not attached at runtime, GameObject inactive, runtime exception, scene not in Build Settings, conditional branch

2. **OnCollisionEnter method exists → Rigidbody + Collider components on GO**
   - Static: Method declared, required components present
   - Cannot prove: Another rigidbody actually collides, or collision callbacks fire
   - Gap sources: Layer collision matrix, incorrect tags, physics settings, kinematic vs dynamic mismatch

3. **Animator.Play("Run") called in Update/FixedUpdate**
   - Static: Call expression present in source
   - Cannot prove: Animator actually transitions to "Run" state
   - Gap sources: Animator Controller missing the state, transition conditions, parameter values, Animator disabled

4. **SetActive(false) called on panel**
   - Static: Call expression present with "false" argument
   - Cannot prove: Panel is actually hidden at runtime
   - Gap sources: Called in unreachable branch, exception before call, other code sets active back

**These are the most important cases for deciding "do we need Runtime Tester?"**

### Runtime-required (Level R)
*These can only be reliably verified at runtime:*

1. **Scene transition** — Active scene changes from A to B after interaction
2. **Button click → side effect** — Observable state change after button press
3. **GameObject runtime active state** — `activeInHierarchy` vs `activeSelf` (inactive due to parent inactive)
4. **Runtime exceptions** — NullReferenceException, MissingReferenceException, IndexOutOfRangeException, etc.
5. **Animation state** — Current state machine state after trigger
6. **Physics outcome** — Velocity, position change, collision/trigger event
7. **Audio playback** — AudioSource.isPlaying at expected time
8. **Coroutine completion** — State after coroutine yield completes
9. **Build success** — Project compiles without errors
10. **UI interaction chain** — Click → panel visible → content correct

### Manual / Not Automatically Verifiable
*These should never be claimed by an automated system:*

1. **Visual aesthetics** — Whether UI looks "beautiful", "professional", or "well-designed"
2. **Code quality** — Whether code is "clean", "well-structured", "maintainable" (subjective)
3. **Model/asset quality** — Whether 3D models are "detailed enough" or "good looking"
4. **Game feel** — Whether controls feel "responsive" or "smooth"
5. **Creative design** — Whether level design is "interesting" or "engaging"
6. **VR comfort** — Whether VR experience is comfortable (causes motion sickness)
7. **Spatial layout appropriateness** — Whether objects are placed "correctly" in 3D space (context-dependent)

---

## Real Project Findings (3DIndustrialMonitor)

**PanelSwitcher on MonitoringCanvas** (class name ≠ GO name):

| Aspect | Static Analysis | Runtime Analysis |
|--------|----------------|------------------|
| Script attached? | ✅ `ScriptAttachedRule` → PanelSwitcher script on MonitoringCanvas | ✅ Would confirm via GetComponent<PanelSwitcher>() |
| UnityEvent binding? | ✅ `UnityEventBindingRule` → Btn_DeviceList → ShowOverview | ✅ Would confirm via Button.onClick listener |
| Code evidence? | ✅ PartiallyResolved — method found, calls detected | ✅ Would confirm by invoking and observing panel state |
| Click works? | ❌ Cannot prove | ✅ Click Btn_DeviceList → MonitoringCanvas shows OverviewPanel |
| UI visible? | ❌ Cannot prove without scene activation context | ✅ activeInHierarchy check |

**Key finding:** The static pipeline correctly identifies all structural evidence. The open question is: does the teacher need to verify that clicking Btn_DeviceList *actually shows* the OverviewPanel, or is the static evidence that *the binding exists and the method is well-formed* sufficient?

For **homework/assignment grading**, the answer depends on the assignment's rubric:
- "Has the student wired up the button?" → Static sufficient
- "Does the button actually work at runtime?" → Runtime required

---

## 10+ Real-World Unity Assignment Verification Cases

### Case 1: Scene Existence
- **Requirement:** Project contains a scene named "MainMenu"
- **Static Evidence:** `SceneExistsRule` — checks scene list from UnityProjectScanner
- **Static Verdict:** Reliable
- **Runtime Needed?** No
- **Runtime Evidence:** N/A

### Case 2: GameObject Existence
- **Requirement:** Scene contains GameObject "Player"
- **Static Evidence:** `GameObjectExistsRule` — YAML has GameObject with m_Name: Player
- **Static Verdict:** Reliable
- **Runtime Needed?** No

### Case 3: Script Attachment
- **Requirement:** "Player" GameObject has "PlayerController" script
- **Static Evidence:** `ScriptAttachedRule` — ScriptGuid → ScriptResolver → ScriptName matches
- **Static Verdict:** Reliable (assuming .cs file name matches class name, which Unity convention enforces)
- **Runtime Needed?** No

### Case 4: UI Hierarchy
- **Requirement:** StartButton is child of Canvas
- **Static Evidence:** `GameObjectHierarchyRule` — ParentFileId chain
- **Static Verdict:** Reliable
- **Runtime Needed?** No

### Case 5: Scene Transition Setup
- **Requirement:** StartButton click loads "Maze2D"
- **Static Evidence:** `UnityEventBindingRule` + `CodeEvidenceRule` — binding exists, method contains LoadScene("Maze2D")
- **Static Verdict:** **Strong evidence, NOT behavioral proof**
- **Runtime Needed?** Yes, for actual verification that click → scene loads
- **Runtime Evidence:** `SceneManager.activeScene` after click
- **Difficulty:** Low (one click, one scene check)

### Case 6: Player Movement (wasd)
- **Requirement:** Player moves when WASD pressed
- **Static Evidence:** Update() contains Input.GetKey + Transform.Translate or Rigidbody.velocity
- **Static Verdict:** Can prove movement code exists, cannot prove it works
- **Runtime Needed?** ⚠️ **Debatable.** For homework grading, static evidence may satisfy "student implemented movement". But for functional validation, runtime is required.
- **Difficulty:** Medium (requires input simulation)

### Case 7: Button Behavior
- **Requirement:** Clicking "Settings" button opens SettingsPanel
- **Static Evidence:** UnityEvent binding + SetActive(true) in method body
- **Static Verdict:** Strong evidence of intent, not behavioral proof
- **Runtime Needed?** Yes for behavioral proof
- **Runtime Evidence:** SettingsPanel.activeInHierarchy after click
- **Difficulty:** Low (one click, one active-state check)

### Case 8: Collision Behavior
- **Requirement:** Player touches enemy → health decreases
- **Static Evidence:** OnCollisionEnter/OnTriggerEnter exists, health variable modified
- **Static Verdict:** Collision handler exists, cannot prove collision geometry actually intersects
- **Runtime Needed?** ⚠️ **Very debatable.** Static evidence may suffice for "did student write collision code?" but not for "does collision work?"
- **Runtime Evidence:** health value before/after controlled collision
- **Difficulty:** High (requires precise positioning, physics determinism)

### Case 9: Animator Behavior
- **Requirement:** Player press Space → character plays "Jump" animation
- **Static Evidence:** Animator.SetTrigger/"Play"("Jump") called in response to Input.GetKeyDown(KeyCode.Space)
- **Static Verdict:** Code intention clear, cannot prove state machine has "Jump" state or transitions
- **Runtime Needed?** Yes, if animation correctness matters
- **Runtime Evidence:** Animator.GetCurrentAnimatorStateInfo(0).IsName("Jump")
- **Difficulty:** Medium (input + state check)

### Case 10: Runtime Exception Check
- **Requirement:** Game runs without errors for 30 seconds
- **Static Evidence:** None — exceptions are runtime-only
- **Static Verdict:** Impossible
- **Runtime Needed?** ✅ **Yes, and this is one of the strongest arguments for Runtime Tester**
- **Runtime Evidence:** Unity log console — no errors/warnings
- **Difficulty:** Low (passive observation only)

### Case 11: UI Visibility (Bonus)
- **Requirement:** HealthBar UI is visible during gameplay
- **Static Evidence:** Canvas, Image/Slider components exist in scene
- **Static Verdict:** UI structure exists, but Canvas could be inactive, camera could be disabled
- **Runtime Needed?** Yes for actual visibility
- **Runtime Evidence:** Canvas.enabled && CanvasGroup.alpha > 0 && activeInHierarchy
- **Difficulty:** Low-Medium (read property values)

### Case 12: Async/Coroutine (Bonus)
- **Requirement:** After picking up item, 2-second delay then item reappears
- **Static Evidence:** StartCoroutine with WaitForSeconds(2f) then SetActive(true)
- **Static Verdict:** Code intention clear, cannot prove coroutine completes
- **Runtime Needed?** Yes, if timing correctness matters
- **Runtime Evidence:** Observable state at t=0, t=1, t=3
- **Difficulty:** High (time-sensitive, flaky in test framework)

---

## Runtime Tester MVP Proposal

### Must Support

1. **Launch** — Start Unity with the project, enter Play Mode
   - Input: Unity project path
   - Output: Process handle, scene state
   - Risk: Build errors, missing dependencies, platform mismatch

2. **Scene observation** — Read current active scene name
   - API: `SceneManager.GetActiveScene().name`
   - Evidence: Direct string comparison

3. **GameObject state observation** — Read GameObject active state, component property values
   - API: `GameObject.activeInHierarchy`, `Component.property`
   - Evidence: Property value comparison (equality, range, boolean)

4. **Limited input simulation** — Click Button by GameObject name (not screen position)
   - Approach: `Find("StartButton") → GetComponent<Button>() → onClick.Invoke()`
   - NOT: Screen-space mouse simulation (too fragile)

5. **Runtime exception observation** — Monitor Unity log for errors/exceptions
   - Approach: `Application.logMessageReceived` or log file tailing
   - Evidence: Error count, error message patterns

6. **Scene transition observation** — Detect active scene change
   - Approach: `SceneManager.activeSceneChanged` callback or polling
   - Evidence: Scene name before/after

7. **Simple timed waits** — Wait N seconds or wait for condition
   - Approach: Frame count or real-time wait
   - Needed for: Animation, physics settling, coroutines

### Explicitly Out of Scope

1. **Free-form exploration** — Agent that wanders the scene looking for issues
2. **Visual understanding / screenshot AI** — No screenshot capture, no image analysis, no OCR
3. **Complex VR interactions** — No controller tracking, no spatial interaction simulation
4. **Autonomous button finding** — No automatic scene exploration to discover UI elements
5. **Arbitrary assignment understanding** — No natural language to test generation
6. **Physics simulation verification** — No quantitative physics validation (velocity, force accuracy)
7. **Network / multiplayer testing** — No client-server simulation
8. **Performance profiling** — No FPS, memory, or GPU metrics
9. **Asset bundle / Addressables** — No dynamic asset loading verification
10. **Edit Mode / asset import** — No asset pipeline validation

---

## Runtime Evidence Proposal

| Evidence Type | Source API | Reliability | Notes |
|---|---|---|---|
| ActiveScene name | SceneManager.GetActiveScene() | ✅ High | Direct, deterministic |
| GameObject.activeInHierarchy | GameObject.activeInHierarchy | ✅ High | Reliable state read |
| Component property value | direct field/property access | ✅ High | Via reflection or known API |
| Button.onClick listeners | Button.onClick | ✅ High | Static list of listeners |
| Animator state | Animator.GetCurrentAnimatorStateInfo | ✅ Medium | Layer-dependent, transitions not instant |
| Rigidbody velocity | Rigidbody.velocity.magnitude | ✅ High | Frame-dependent, threshold needed |
| Transform position | Transform.position | ✅ High | Floating-point comparison |
| AudioSource.isPlaying | AudioSource.isPlaying | ✅ Medium | Frame-dependent |
| Log message | Application.logMessageReceived | ✅ High | Catch at subscription point |
| Scene list in BuildSettings | EditorBuildSettings.scenes | ✅ High | Static data, accessible in Editor |
| CanvasGroup.alpha | CanvasGroup.alpha | ✅ High | Direct read |
| Slider.value | Slider.value | ✅ High | Direct read |

---

## Result Classification Proposal

### Passed
- Static rule: All structural/static assertions confirmed
- Runtime rule: All observed evidence matches expected values exactly
- No unexpected runtime exceptions during observation window

### Failed
- Static rule: Required structure/evidence not found (or found with incorrect properties)
- Runtime rule: Observed evidence does NOT match expected value
- Critical runtime exception prevents expected behavior (e.g. NullReferenceException in button handler)

### NotEvaluated
- Prerequisite condition not met (e.g. Unity project doesn't build)
- Observable precondition prevents test (e.g. GameObject not found at runtime)
- Required evidence type is not supported by current tester
- Timeout while waiting for expected state (potential flaky test — human review needed)

**Key rule for NotEvaluated:**
If Unity project fails to build/launch, DO NOT mark individual rules as Failed — mark the entire runtime suite as NotEvaluated. The reason: the build failure is a project-level problem, not evidence that individual requirements are unmet.

---

## Major Risks

### 1. Unity startup time
- Cold start: 30–120 seconds depending on project size
- First-time import: potentially minutes (library cache warmup)
- **Impact:** Runtime tests cannot be "instant." A batch of 10 tests might take 3–5 minutes.

### 2. Build/compile errors
- If scripts have compilation errors, Unity will not enter Play Mode
- **Impact:** Runtime Tester must detect this and report `NotEvaluated` for all runtime rules (not Failed)
- **Mitigation:** Static analysis can detect some compilation errors via Roslyn before attempting runtime

### 3. Input system differences
- Old Input Manager (`Input.GetKey`) vs New Input System package
- Rewired, CInput, or custom input wrappers
- **Impact:** A generic "press key W" action won't work across all input setups
- **Mitigation:** Prefer direct method invocation over input simulation

### 4. Non-deterministic physics
- Physics simulation depends on frame rate, timestep, and random seed
- **Impact:** Collision/trigger tests may be flaky
- **Mitigation:** Design tests with generous tolerances; prefer trigger zones (easier) over precise collision

### 5. Platforms and build targets
- Standalone (Windows/Mac/Linux) vs WebGL vs mobile vs console
- **Impact:** Runtime behavior may differ per platform
- **Scope:** Start with Editor Play Mode only; platform-specific testing is out of scope

### 6. Time-dependent behavior
- Coroutines, async operations, animations, tweens
- **Impact:** Tests need timed waits, which adds flakiness
- **Mitigation:** Use frame-count waits (WaitForEndOfFrame) instead of real-time; add generous timeouts

### 7. Asynchronous loading
- SceneManager.LoadSceneAsync, Addressables, AssetBundle
- **Impact:** Scene/object may not be immediately available after trigger
- **Mitigation:** Poll with timeout instead of immediate assertion

### 8. Runtime randomness
- Random.Range, random spawn positions, random AI behavior
- **Impact:** Same test may produce different results
- **Mitigation:** Design tests around deterministic state; avoid random-dependent assertions

---

## Recommended Next Milestone Boundary

### Milestone 10 (Recommended)
**Runtime Tester Design and Proof of Concept**

- Design the RuntimeAction/Assertion data model (not JSON yet)
- Implement a **single, minimal** runtime test: Launch Unity Editor → enter Play Mode → read ActiveScene name
- No input simulation yet
- No full test runner yet
- Goal: Prove that the Unity Editor automation pathway works and we can reliably:
  - Detect Play Mode entry
  - Read scene state
  - Exit Play Mode
  - Handle failure cases (build errors, missing scenes)

### Milestone 11 (Future)
**Runtime Test Framework Core**

- RuntimeAction model (Action: Launch, Wait, Assert; Assert directly on GameObject state)
- RuntimeTest definition
- Minimal Runtime Evidence observation
- No input simulation
- No chain of multiple actions

### Milestone 12 (Future)
**Input Simulation and Complex Assertions**

- Click by game object name
- Scene transition detection
- Runtime exception monitoring
- Chain of: Launch → Click → Assert scene loaded

---

## Repository Changes

- **None.**

This milestone is a pure audit. No code was written, no files were modified.

## Git
- **Commit:** None
- **Push:** None
- **Final Status:** `ca6127e`, main, `nothing to commit, working tree clean`

## Milestone 9 Conclusion
**PASS** ✅

All success criteria met:
- ✅ Baseline correct (ca6127e, main, clean)
- ✅ Static / Runtime Capability Matrix completed (23 categories)
- ✅ 12 real-world Unity verification cases analyzed
- ✅ Static / Runtime / Manual boundary clearly defined
- ✅ 3DIndustrialMonitor real project findings included
- ✅ Runtime Tester MVP proposal documented
- ✅ Runtime Evidence types catalogued
- ✅ Result classification (Passed/Failed/NotEvaluated) defined
- ✅ Runtime risks analyzed (8 categories)
- ✅ No Runtime Tester code written
- ✅ No real Unity project modified
- ✅ No AI introduced
- ✅ No GUI
- ✅ No project scope expansion