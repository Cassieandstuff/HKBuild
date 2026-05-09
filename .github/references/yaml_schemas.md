# HKBuild — YAML Schemas

Every YAML file format used by the behavior compiler, with real examples from verified behaviors.

---

## behavior.yaml

Top-level behavior definition. One per `.hkx` folder.

```yaml
packfile:
  classversion: 8
  contentsversion: "hk_2010.2.0-r1"

behavior:
  name: "BashBehavior.hkb"
  variableMode: VARIABLE_MODE_DISCARD_WHEN_INACTIVE
  rootGenerator: BashBehavior        # Name of root generator (must exist in states/selectors/etc)
  data: graphdata                     # Optional: name of file in data/ (omit or null if no graph data)
```

**Generates:** `hkbBehaviorGraph` (#0002)

---

## clips/*.yaml — Clip Generator

One file per `hkbClipGenerator`. References an animation file by path.

Triggers can use either `event: EventName` (resolved from graph data) or `eventId: N` (raw index).
Most trigger fields have defaults and can be omitted — only `localTime` and `event`/`eventId` are required.

```yaml
class: hkbClipGenerator
name: 1HM_BlockBash
animationName: Animations\1HM_BlockBash.hkx
mode: MODE_SINGLE_PLAY
playbackSpeed: 1.000000
triggers:
  - localTime: 0.000000
    event: preHitFrame
  - localTime: 0.000000
    event: bashExit
    relativeToEndOfClip: true        # Only needed for end-of-clip triggers
  - localTime: 0.033000
    event: SoundPlay.NPCHumanCombatShieldBash
  - localTime: 0.099000
    event: HitFrame
```

| Trigger field | Default | When to include |
|---|---|---|
| `payload` | `null` | Only when attaching payload data |
| `relativeToEndOfClip` | `false` | Only for end-of-clip triggers (`true`) |
| `acyclic` | `false` | Only for acyclic triggers |
| `isAnnotation` | `false` | Only for annotation triggers |

**Generates:** `hkbClipGenerator` + optional `hkbClipTriggerArray`

### Minimal clip (no triggers, all defaults)
```yaml
class: hkbClipGenerator
name: DefaultBlockBash
animationName: Animations\BlockBash.hkx
mode: MODE_SINGLE_PLAY
playbackSpeed: 1.000000
```

---

## generators/*.yaml — Blender Generator

One file per `hkbBlenderGenerator`. Blends multiple child generators with weights and optional bone masks.

### Bone weight preset reference

Presets are behavior-local and defined in `bone_presets.yaml` (see schema below). Any `boneWeights` field can reference a preset by name:

```yaml
children:
  - generator: CLIP_TF_HoverForward
    weight: 1.000000
    worldFromModelWeight: 1.000000
    boneWeights:
      preset: TF_BodyNoHead
```

`preset` and `named` cannot both be set on the same `boneWeights` object.

### Named bone weights (preferred)

Requires `character assets/skeleton.yaml` in a parent directory. Only non-zero weights need listing — unlisted bones default to `0.000000`.

```yaml
class: hkbBlenderGenerator
name: BashStaffRightBlend
flags: 0
subtractLastChild: false
children:
  - generator: StaffRight_Idle
    weight: 1.000000
    worldFromModelWeight: 0.000000
    boneWeights:
      named:
        "NPC R Hand [RHnd]": 1.000000
        "AnimObjectR": 1.000000
        "Weapon": 1.000000
        "NPC R MagicNode [RMag]": 1.000000
        "NPC R Finger00 [RF00]": 1.000000
        "NPC R Finger01 [RF01]": 1.000000
        # ... remaining right-side bones
  - generator: Bash_Behavior
    weight: 1.000000
    worldFromModelWeight: 1.000000
    boneWeights:
      named:
        "NPC Root [Root]": 1.000000
        "NPC COM [COM ]": 1.000000
        "NPC Pelvis [Pelv]": 1.000000
        # ... all body bones except right hand/fingers
```

### Raw bone weights (legacy, still supported)

```yaml
    boneWeights:
      count: 99
      values: >-
        0.000000 0.000000 ... 1.000000 0.000000
```

**Generates:** `hkbBlenderGenerator` + per-child `hkbBlenderGeneratorChild` + optional `hkbBoneWeightArray` + optional `hkbVariableBindingSet`

**Note:** Named format always resolves to the full skeleton bone count. Raw format preserves the original count exactly.

---

## selectors/*.yaml — Manual Selector Generator

One file per `hkbManualSelectorGenerator`. Selects one generator from a list based on a variable.

Bindings can use either `variable: VarName` (resolved from graph data) or `variableIndex: N` (raw index).

```yaml
class: hkbManualSelectorGenerator
name: BashWeaponTypeMSG
bindings:
  - memberPath: selectedGeneratorIndex
    variable: iRightHandType       # Named variable (preferred)
generators:
  - Bash_Behavior           # Names of generators to select from
  - Bash_Behavior
  - BashStaffRightBlend
```

Optional fields: `selectedGeneratorIndex` (default: 0), `currentGeneratorIndex` (default: 0), `userData` (default: 0).

**Generates:** `hkbManualSelectorGenerator` + optional `hkbVariableBindingSet`

---

## states/*.yaml — State Machine

State machines and states share the `states/` directory. Disambiguated by the `class` field.

### State Machine

```yaml
class: hkbStateMachine
name: BashBehavior
startStateId: 0
selfTransitionMode: SELF_TRANSITION_MODE_FORCE_TRANSITION_TO_START_STATE
bindings:
  - memberPath: isActive
    variable: IsBashing
states:
  - Bash_State
  - BashPower_State
  - BlockBashIntro_State
```

Optional fields (all have defaults matching vanilla):
- `selfTransitionMode` (default: SELF_TRANSITION_MODE_NO_TRANSITION)
- `userData` (default: 0)
- `returnToPreviousStateEventId` / `returnToPreviousStateEvent` (default: -1)
- `randomTransitionEventId` / `randomTransitionEvent` (default: -1)
- `transitionToNextHigherStateEventId` / `transitionToNextHigherStateEvent` (default: -1)
- `transitionToNextLowerStateEventId` / `transitionToNextLowerStateEvent` (default: -1)
- `syncVariableIndex` / `syncVariable` (default: -1)
- `wrapAroundStateId` (default: false)
- `maxSimultaneousTransitions` (default: 32)
- `startStateMode` (default: START_STATE_MODE_DEFAULT)
- `wildcardTransitions` (default: null)

**Generates:** `hkbStateMachine` + optional `hkbVariableBindingSet`

### State Info

Events can use either `event: EventName` (resolved from graph data) or `eventId: N` / `id: N` (raw index).
Transitions can use `toState: StateName` (resolved at load time) or `toStateId: N` (raw index).

```yaml
class: hkbStateMachineStateInfo
name: DefaultBlockBash
stateId: 0
generator: BlockBashMSG
enterNotifyEvents:
  - event: tailCombatIdle
transitions:
  - event: SneakStart
    toState: SneakBlockBash
    transition: DefaultBlendTransition
```

Optional state fields: `probability` (default: 1.000000), `enable` (default: true).

Most transition fields have defaults and can be omitted:

| Field | Default | When to include |
|---|---|---|
| `flags` | `FLAG_DISABLE_CONDITION` | Only when using conditions |
| `fromNestedStateId` | `0` | Only for nested state machine transitions |
| `toNestedStateId` | `0` | Only for nested state machine transitions |
| `priority` | `0` | Only when multiple transitions compete |
| `triggerInterval` | all -1/0.0 | Only when using timed trigger windows |
| `initiateInterval` | all -1/0.0 | Only when using timed initiate windows |
| `payload` (notify events) | `null` | Only when attaching payload data |

A minimal transition needs only `event`/`toState`/`transition`. A minimal trigger needs only `localTime`/`event` (plus `relativeToEndOfClip: true` for end-of-clip triggers).

**Generates:** `hkbStateMachineStateInfo` + optional `hkbStateMachineEventPropertyArray` (×2) + optional `hkbStateMachineTransitionInfoArray`

### Transition parsing note

The `transitions` field is parsed from raw YAML text by `BehaviorReader.ParseTransitions()`, not by the YAML deserializer (because YamlDotNet would try to parse it as a string). The `Transitions` property on `StateDef` is marked `[YamlIgnore]`; the parsed result goes into `ParsedTransitions`.

---

## transitions/*.yaml — Transition Effect

One file per `hkbBlendingTransitionEffect`.

```yaml
class: hkbBlendingTransitionEffect
name: attackStartTransition
selfTransitionMode: SELF_TRANSITION_MODE_RESET
duration: 0.000000
bindings:
  - memberPath: duration
    variable: blendFast
```

Optional fields (all have defaults):
- `selfTransitionMode` (default: SELF_TRANSITION_MODE_CONTINUE_IF_CYCLIC_BLEND_IF_ACYCLIC)
- `eventMode` (default: EVENT_MODE_DEFAULT)
- `toGeneratorStartTimeFraction` (default: 0.000000)
- `flags` (default: 0)
- `endMode` (default: END_MODE_NONE)
- `blendCurve` (default: BLEND_CURVE_SMOOTH)
- `userData` (default: 0)

A minimal transition effect needs only `class`, `name`, and `duration`.

**Generates:** `hkbBlendingTransitionEffect` + optional `hkbVariableBindingSet`

---

## data/*.yaml — Graph Data

Referenced by `behavior.yaml`'s `data` field. Contains variables and events for the behavior.

```yaml
variables:
  - name: blendFast
    type: VARIABLE_TYPE_REAL
    value: 1028443341              # Raw word value (int representation of float)
  - name: IsBashing
    type: VARIABLE_TYPE_BOOL
    value: 0
  - name: iLeftHandType
    type: VARIABLE_TYPE_INT32
    value: 0

events:
  - name: bashPowerStart
  - name: HitFrame
    flags: FLAG_SYNC_POINT         # Only non-default flags need listing
  - name: SoundPlay.NPCHumanCombatShieldBash
```

Optional fields with defaults:
- `flags` (default: "0") — per event
- `role` (default: ROLE_DEFAULT) — per variable
- `roleFlags` (default: 0) — per variable
- `attributeDefaultCount` (default: 0) — top level
- `characterPropertyInfoCount` (default: 0) — top level
- `quadVariableValueCount` (default: 0) — top level
- `variantVariableValueCount` (default: 0) — top level
- `wordMinVariableValueCount` (default: 0) — top level
- `wordMaxVariableValueCount` (default: 0) — top level

**Generates:** `hkbBehaviorGraphData` + `hkbVariableValueSet` + `hkbBehaviorGraphStringData`

---

## bone_presets.yaml — Behavior-local bone weight presets

Optional file at the root of a behavior folder (`<behavior>.hkx/bone_presets.yaml`).

```yaml
presets:
  TF_BodyNoHead:
    "NPC Root [Root]": 1.000000
    "NPC Pelvis [Pelv]": 1.000000
    "NPC Neck [Neck]": 0.000000
    "NPC Head [Head]": 0.000000
```

Each preset is a named `bone name -> weight` map, equivalent to `boneWeights.named`. During load, HKBuild expands `boneWeights.preset` references into full named maps.

---

## Variable Binding Set (inline on any node)

Not a standalone file — the `bindings` list can appear on any node that supports `variableBindingSet`:

```yaml
bindings:
  - memberPath: selectedGeneratorIndex
    variableIndex: 5
  - memberPath: isActive
    variableIndex: 1
```

Optional fields per binding:
- `bitIndex` (default: -1)
- `bindingType` (default: BINDING_TYPE_VARIABLE)

**Generates:** `hkbVariableBindingSet`

---

## modifiers/*.yaml — Modifier Generator

Pairs a modifier with a generator — the modifier runs while the generator plays. The `modifiers/` directory contains multiple class types, disambiguated by the `class` field.

### hkbModifierGenerator

```yaml
class: hkbModifierGenerator
name: SneakSprintRoll_MG
userData: 1
modifier: bAllowRotation_IsActiveMod
generator: SprintBehavior
```

Optional: `userData` (default: 0), `bindings`.

### BSIsActiveModifier

```yaml
class: BSIsActiveModifier
name: bAllowRotation_IsActiveMod
userData: 2
bindings:
  - memberPath: bIsActive0
    variable: bAllowRotation
```

All `bIsActive0..4` and `bInvertActive0..4` fields default to `false` and can be omitted.
Optional: `userData` (default: 0), `enable` (default: true), `bindings`.

**Generates:** `hkbModifierGenerator` or `BSIsActiveModifier` + optional `hkbVariableBindingSet`

---

## tagging/*.yaml — State Tagging Generator

Wraps a generator and tags the state with an integer for game-side queries.

```yaml
class: BSiStateTaggingGenerator
name: Sprint_iStateGen
userData: 1
pDefaultGenerator: SprintBlend
iStateToSetAs: 1
iPriority: 7
```

Optional: `userData` (default: 0), `bindings`.

**Generates:** `BSiStateTaggingGenerator` + optional `hkbVariableBindingSet`

---

## String Event Payloads

Not a standalone file — payloads are string values on clip triggers or state notify events:

```yaml
triggers:
  - localTime: 0.000000
    event: StartAnimatedCameraDelta
    payload: AnimObjectA
```

When `payload` is a non-null string, the compiler emits an `hkbStringEventPayload` object and references it by ID. Payloads with identical data strings share a single object (deduplication).

---

## Character File Schemas

These are in the character pipeline (`CharacterReader` / `CharacterXmlEmitter`), documented in `hkbuild/DESIGN.md`:

- `character.yaml` — packfile metadata, rig/ragdoll/behavior paths, controller, model axes
- `animations.txt` — one animation path per line
- `properties/*.yaml` — character properties with types, roles, bone weights (named format supported)
- `foot_ik.yaml` — foot IK leg definitions
- `mirror.yaml` — skeleton mirror axis + bone pair map (named format supported)

### Named bone weights in properties

Character properties also support the named bone weight format:

```yaml
name: UpperBody
type: VARIABLE_TYPE_POINTER
role: ROLE_DEFAULT
bone_weights:
  named:
    "NPC Spine [Spn0]": 1.000000
    "NPC Spine1 [Spn1]": 1.000000
    "NPC Spine2 [Spn2]": 1.000000
    "NPC L Clavicle [LClv]": 1.000000
    # ... all upper body bones
```

### Named bone pair map in mirror.yaml

Mirror bone pair maps can use bone names instead of raw indices. Unlisted bones mirror to themselves (identity). Both directions must be listed explicitly.

```yaml
mirrorAxis: [1.0, 0.0, 0.0, 0.0]

bonePairMap:
  named:
    "NPC L Thigh [LThg]": "NPC R Thigh [RThg]"
    "NPC R Thigh [RThg]": "NPC L Thigh [LThg]"
    "NPC L Hand [LHnd]": "NPC R Hand [RHnd]"
    "NPC R Hand [RHnd]": "NPC L Hand [LHnd]"
    "AnimObjectL": "AnimObjectR"
    "AnimObjectR": "AnimObjectL"
    # ... all left↔right pairs (66 total for humanoid)
```

Raw format (`count` + `values`) is still supported for backward compatibility.

---

## Skeleton Source File

Located at `character assets/skeleton.yaml` (sibling to `behaviors/` and `characters/`).

The compiler searches upward from the source directory to find it. Provides the bone name → index mapping for named bone weights.

```yaml
# Skeleton bone list — humanoid (defaultmale / defaultfemale)
bones:
  - "NPC Root [Root]"        # index 0
  - "x_NPC LookNode [Look]" # index 1
  - "x_NPC Translate [Pos ]" # index 2
  # ... 99 total for humanoid
  - "Camera3rd [Cam3]"       # index 97
  - "Camera Control"         # index 98
```

Male and female humanoid skeletons share identical bone names and ordering.
