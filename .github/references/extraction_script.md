# HKBuild — Universal Behavior Extraction Script

## What It Is

`hkbuild/scripts/extract_behavior.ps1` converts any vanilla behavior XML into the YAML source tree format consumed by the HKBuild compiler. This eliminates manual extraction work — a new behavior goes from reference XML to compilable YAML in seconds.

**Location:** `hkbuild/scripts/extract_behavior.ps1`

---

## Usage

```bash
# Via HKBuild CLI (preferred):
hkbuild extract bashbehavior
hkbuild extract 1hm_locomotion --force
hkbuild extract 0_master --xml-dir ".reference\custom\behaviors" --out-base "behavior_src\custom"
```

```powershell
# Direct PowerShell invocation (from repo root):
powershell -ExecutionPolicy Bypass -File "hkbuild\scripts\extract_behavior.ps1" -Name "bashbehavior"
powershell -ExecutionPolicy Bypass -File "hkbuild\scripts\extract_behavior.ps1" -Name "sprint" -Force
```

The script searches hkx2e XMLs first (ground truth), then hkxcmd XMLs (legacy fallback).
Output: `behavior_src/vanilla/character/behaviors/{Name}.hkx/`.

---

## What It Extracts

### Per-directory output

| Directory | Class(es) | Notes |
|---|---|---|
| `behavior.yaml` | `hkbBehaviorGraph` | Root file, packfile metadata |
| `data/graphdata.yaml` | `hkbBehaviorGraphData` + `hkbBehaviorGraphStringData` + `hkbVariableValueSet` | Variables with types/roles/values, events with flags |
| `clips/` | `hkbClipGenerator` + `hkbClipTriggerArray` | Triggers with named events, string payloads |
| `generators/` | `hkbBlenderGenerator`, `BSCyclicBlendTransitionGenerator`, `BSBoneSwitchGenerator`, `BSSynchronizedClipGenerator`, `BSOffsetAnimationGenerator`, `hkbPoseMatchingGenerator` | Bone weights in named format |
| `selectors/` | `hkbManualSelectorGenerator` | Generator lists |
| `states/` | `hkbStateMachine` + `hkbStateMachineStateInfo` | SM fields, transitions, enter/exit notify events |
| `transitions/` | `hkbBlendingTransitionEffect` | Duration, blend curve, bindings |
| `modifiers/` | `hkbModifierGenerator`, `BSIsActiveModifier`, `hkbModifierList`, `hkbEventDrivenModifier`, + 20 other modifier types | Generic extraction for unsupported types |
| `tagging/` | `BSiStateTaggingGenerator` | State tagging |
| `references/` | `hkbBehaviorReferenceGenerator` | Sub-behavior references |
| `data/*.yaml` | `hkbExpressionDataArray`, `hkbEventRangeDataArray`, `hkbBoneIndexArray` | Auxiliary data arrays |

### Named format conversions

The script automatically converts raw numeric references to human-readable names:

| Raw format | Named format | Source |
|---|---|---|
| `eventId: 5` | `event: HitFrame` | Graph data events list |
| `variableIndex: 3` | `variable: bAllowRotation` | Graph data variables list |
| `toStateId: 2` | `toState: BashPower_State` | Parent state machine state list |
| bone weight float array | `named:` map with bone names | `skeleton.yaml` |
| `activateEventId: 7` | `activateEvent: SneakStart` | Event-driven modifier event IDs |
| `syncVariableIndex: 13` | `syncVariable: SpeedSampled` | State machine sync variable |

### Readability defaults omitted

Fields matching compiler defaults are not emitted, keeping YAML clean:

| Field | Default | Omit condition |
|---|---|---|
| `userData` | `0` | omit if 0 |
| `enable` | `true` | omit if true |
| `flags` (transition) | `FLAG_DISABLE_CONDITION` | omit if default |
| `selfTransitionMode` | `SELF_TRANSITION_MODE_NO_TRANSITION` | omit if default |
| `startStateMode` | `START_STATE_MODE_DEFAULT` | omit if default |
| `maxSimultaneousTransitions` | `32` | omit if 32 |
| `priority` | `0` | omit if 0 |
| `triggerInterval` / `initiateInterval` | all -1/0.0 | omit if all defaults |

---

## Dependencies

- **Skeleton file:** `behavior_src/vanilla/character/character assets/skeleton.yaml` must exist for named bone weights. If missing, bone weights fall back to raw index format.
- **Reference XMLs:** `.reference/Destructible Behavior XMLs/character/behaviors/` must contain the decompiled vanilla XML files (produced by hkxcmd `exportxml`).

---

## Modifier Extraction Strategy

Modifiers fall into two categories:

### Explicitly handled (class-specific logic)
- `hkbModifierGenerator` — extracts modifier + generator refs
- `BSIsActiveModifier` — extracts bIsActive0..4 / bInvertActive0..4 flags
- `hkbModifierList` — extracts modifier ref list
- `hkbEventDrivenModifier` — extracts activate/deactivate events with named resolution

### Generic extraction (all other modifiers)
For the ~20 remaining modifier types (`BSDirectAtModifier`, `hkbTwistModifier`, `hkbTimerModifier`, etc.), the script uses a generic extractor that:
1. Emits `class:` and `name:` fields
2. Emits `userData:` and `enable:` if non-default
3. Extracts bindings
4. Iterates all remaining `hkparam` fields and emits them as-is
5. Inline event objects → uses `ExtractInlineEventYaml` with named resolution
6. References (`#XXXX`) → resolved to names via `RefName()`
7. Reference lists → emitted as YAML arrays

This means **all modifier types are extracted** even if the compiler doesn't support them yet. The YAML will be ready when compiler support is added.

---

## Transition Structure

The vanilla XML transition info uses `<hkparam name="eventId">X</hkparam>` as a **bare integer** — NOT an inline `hkbEventProperty` object. The extraction script reads this bare int and resolves it to an event name.

Transitions also have `triggerInterval` and `initiateInterval` sub-objects with `enterEventId`/`exitEventId`/`enterTime`/`exitTime`. These are only emitted when non-default (most are all -1/0.0).

Conditions on transitions are inlined: `hkbExpressionCondition` → `condition: "expression"`, `hkbStringCondition` → `conditionString: "string"`.

---

## Known Limitations

1. **String event payloads** are deduplicated by the compiler (not the extraction script). The script emits `payload: AnimObjectA` inline on triggers/events, and the compiler handles deduplication.

2. **BSLookAtModifier bone data arrays** and `hkbKeyframeBonesModifier` keyframe info arrays are captured by the generic extractor but may need special handling in the compiler for correct XML emission. The YAML captures the raw hkparam text.

3. **Empty directories** are automatically cleaned up after extraction.

---

## Verification Workflow

After extraction, verify with the full round-trip:

```powershell
# 1. Extract
powershell -ExecutionPolicy Bypass -File "build\extract_behavior.ps1" -Name "blockbehavior"

# 2. Compile
dotnet run --project "hkbuild\src\HKBuild.csproj" -- "behavior_src\vanilla\character\behaviors\blockbehavior.hkx" -o "build\test_block.xml"

# 3. Compare class counts (quick sanity check)
$built = Get-Content "build\test_block.xml" -Raw
$orig = Get-Content ".reference\Destructible Behavior XMLs\character\behaviors\blockbehavior.xml" -Raw
$bClasses = [regex]::Matches($built, 'class="([^"]+)" signature') | ForEach-Object { $_.Groups[1].Value } | Group-Object | Sort-Object Name
$oClasses = [regex]::Matches($orig, 'class="([^"]+)" signature') | ForEach-Object { $_.Groups[1].Value } | Group-Object | Sort-Object Name
foreach ($oc in $oClasses) {
    $bc = $bClasses | Where-Object { $_.Name -eq $oc.Name }
    $bCount = if ($bc) { $bc.Count } else { 0 }
    $marker = if ($oc.Count -ne $bCount) { " <--" } else { "" }
    Write-Output ("{0,-35} {1,4}  {2,4}{3}" -f $oc.Name, $oc.Count, $bCount, $marker)
}
```

All class counts should match. Remaining diffs should be bone weight `numelements` padding only.
