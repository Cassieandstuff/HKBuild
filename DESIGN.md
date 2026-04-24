# HKBuild — Havok Behavior Compiler

## Overview

HKBuild is a build tool that compiles a directory tree of small, human-readable
YAML/text source files into Havok binary `.hkx` packfiles. It replaces the need
to hand-edit monolithic XML files or rely on Pandora for mod-owned behavior and
character files.

## Design Principles

1. **One concept per file.** A bone mask, a clip generator, a state — each is its
   own file. No file exceeds ~50 lines of authored content.

2. **References by name, not by ID.** Files reference each other by logical name.
   The compiler assigns `#NNNN` IDs and resolves references. Authors never write IDs.

3. **Plain text for lists.** Animation paths are one-per-line text files. Adding an
   animation is adding a line.

4. **Parallel arrays are implicit.** Declaring a property with a name and type causes
   the compiler to emit entries in both `characterPropertyNames` AND
   `characterPropertyInfos`. No manual synchronization.

5. **Creature-agnostic.** The same tool builds humanoid, dragon, and custom creature
   character files.

---

## Source Tree Layout — Character File

```
hkbuild/
  characters/
    <name>/                        (e.g. defaultmale, defaultfemale, dragontest)
      character.yaml               packfile metadata + character identity + controller + model axes
      animations.txt               one animation path per line
      properties/
        <PropertyName>.yaml        one file per character property
      foot_ik.yaml                 foot IK driver configuration
      mirror.yaml                  skeleton mirror axis + bone pair map
```

### character.yaml

Top-level character definition. Generates:
- `hkRootLevelContainer` (packfile root)
- `hkbCharacterData` (controller capsule, model axes, references to other objects)
- Scalar fields of `hkbCharacterStringData` (name, rig, ragdoll, behavior paths)

```yaml
packfile:
  classversion: 8
  contentsversion: "hk_2010.2.0-r1"

character:
  name: DefaultMale
  rig: "Character Assets\\skeleton.HKX"
  ragdoll: "Character Assets\\skeleton.HKX"
  behavior: "Behaviors\\0_Master.hkx"
  scale: 1.0
  controller:
    capsuleHeight: 1.7
    capsuleRadius: 0.4
    collisionFilterInfo: 1
  model:
    up:      [0.0, 0.0, 1.0, 0.0]
    forward: [0.0, 1.0, 0.0, 0.0]
    right:   [1.0, 0.0, 0.0, 0.0]
```

### animations.txt

Plain text, one animation path per line. The compiler counts lines and emits the
`animationNames` array with the correct `numelements`.

```
Animations\1HM_1stP_Run.hkx
Animations\1hm_1stP_Walk.hkx
...
Animations\WW_Stage9.hkx
```

### properties/*.yaml

Each file defines one character property. The compiler:
1. Reads all property files in directory order (alphabetical by filename)
2. Emits `characterPropertyNames` entries (from `name` field)
3. Emits `characterPropertyInfos` entries (from `type` and `role` fields)
4. For `VARIABLE_TYPE_POINTER` types: creates an `hkbBoneWeightArray` object from `bone_weights`
5. Builds `hkbVariableValueSet`:
   - `wordVariableValues` from `initial_value` (scalar) or auto-assigned bone weight index (pointer)
   - `variantVariableValues` referencing the bone weight array objects

**Scalar property:**
```yaml
name: IsMale
type: VARIABLE_TYPE_BOOL
role: ROLE_DEFAULT
initial_value: 1
```

**Pointer property (bone weight mask):**
```yaml
name: UpperBody
type: VARIABLE_TYPE_POINTER
role: ROLE_DEFAULT
bone_weights:
  count: 99
  values: >-
    0.000000 0.000000 ... 1.000000 0.000000 0.000000
```

### Property ordering

**IMPORTANT:** The order of properties in the compiled output must match vanilla
exactly for Pandora compatibility. The compiler uses an explicit ordering file
or falls back to alphabetical-by-filename. For vanilla characters, the property
files should be numbered or an `_order.txt` file should list them:

```
# _order.txt — explicit property ordering
IsFemale
IsHorse
IsMale
IsThirdPerson
cCharacterSelector
SneakMagicLowerBody
...
```

---

## Compiler Pipeline

```
Source Files (.yaml/.txt)
        │
   [1. Parse]         Read character.yaml, animations.txt, properties/*.yaml,
        │              foot_ik.yaml, mirror.yaml
        ▼
   [2. Validate]      Check all required files present, bone weight counts match,
        │              property types are valid
        ▼
   [3. Assign IDs]    Deterministic #NNNN assignment:
        │                #0001 = hkRootLevelContainer
        │                #0002 = hkbCharacterData
        │                #0003 = hkbVariableValueSet
        │                #0004+ = hkbBoneWeightArray (one per POINTER property)
        │                next = hkbFootIkDriverInfo
        │                next = hkbCharacterStringData
        │                next = hkbMirroredSkeletonInfo
        ▼
   [4. Emit XML]      Generate Havok-format XML:
        │                - <hkpackfile> envelope
        │                - <hksection name="__data__">
        │                - All hkobjects with resolved #NNNN references
        ▼
   [5. Compile]       Feed XML to HKX2E → binary .hkx
```

---

## Implementation

**Language:** C# (same ecosystem as HKX2E)

**Entry point:** `hkbuild characters/<name>/ -o <output>.hkx`

**Dependencies:**
- HKX2E library (`.reference/HKX2-Enhanced-Library-main/`) for step 5
- YamlDotNet (NuGet) for YAML parsing

**Build integration:** CMake `add_custom_command` runs hkbuild during the build
to compile character/behavior source into `.hkx` files.

---

## Future: Behavior File Support

The same tool extends to behavior files with additional node types:

```
behaviors/
  <name>/                          (e.g. staggerbehavior, bashbehavior)
    behavior.yaml                  packfile metadata + root generator ref + graph data ref
    clips/*.yaml                   hkbClipGenerator nodes
    generators/*.yaml              hkbBlenderGenerator nodes
    selectors/*.yaml               hkbManualSelectorGenerator nodes
    states/*.yaml                  hkbStateMachine + hkbStateMachineStateInfo (disambiguated by class field)
    transitions/*.yaml             hkbBlendingTransitionEffect nodes
    data/graphdata.yaml            hkbBehaviorGraphData + variables + events
```

### Supported HKX class types

| Class | Source | Notes |
|---|---|---|
| hkRootLevelContainer | *auto* | Packfile root |
| hkbBehaviorGraph | behavior.yaml | Root graph node |
| hkbStateMachine | states/*.yaml | `class: hkbStateMachine` distinguishes from states |
| hkbStateMachineStateInfo | states/*.yaml | `class: hkbStateMachineStateInfo` |
| hkbStateMachineEventPropertyArray | states/*.yaml | Inlined as `enterNotifyEvents`/`exitNotifyEvents` lists |
| hkbStateMachineTransitionInfoArray | states/*.yaml | Inlined as `transitions` list on states |
| hkbBlendingTransitionEffect | transitions/*.yaml | Supports optional `bindings` for variable-driven duration |
| hkbClipGenerator | clips/*.yaml | |
| hkbClipTriggerArray | clips/*.yaml | Inlined as `triggers` list |
| hkbBlenderGenerator | generators/*.yaml | |
| hkbBlenderGeneratorChild | generators/*.yaml | Inlined as `children` list |
| hkbBoneWeightArray | generators/*.yaml | Inlined in child `boneWeights` |
| hkbManualSelectorGenerator | selectors/*.yaml | |
| hkbVariableBindingSet | *any node with `bindings`* | State machines, selectors, blenders, transition effects |
| hkbBehaviorGraphData | data/graphdata.yaml | Variable infos + event infos |
| hkbBehaviorGraphStringData | data/graphdata.yaml | Event names + variable names |
| hkbVariableValueSet | data/graphdata.yaml | Initial variable values |

### Inline transitions

States support inline transition definitions. Each transition references a
transition effect by name and specifies event ID, target state, and flags:

```yaml
class: hkbStateMachineStateInfo
name: BlockBashIntro_State
stateId: 1
generator: BashIntroWeaponTypeMSG
transitions:
  - eventId: 2
    toStateId: 0
    transition: attackStartTransition
    flags: FLAG_DISABLE_CONDITION
    triggerInterval:
      enterEventId: -1
      exitEventId: -1
    initiateInterval:
      enterEventId: -1
      exitEventId: -1
```

### Graph data

When `behavior.yaml` references a `data` field, the compiler loads the
corresponding file from `data/` and emits the three graph data objects
(BehaviorGraphData, VariableValueSet, BehaviorGraphStringData) after
all generator nodes.

### Verified behaviors

| Behavior | Objects | Lines | Diff vs Original |
|---|---|---|---|
| StaggerBehavior | 96 | 2684 | **0 diffs** (byte-identical) |
| BashBehavior | 127 | 4333 | **4 diffs** (cosmetic ID ordering on one transition effect binding) |

---

## Verification Strategy

Before implementing HKX2E compilation (step 5), verify correctness by:
1. Assembling stubs back into XML (steps 1-4)
2. Diffing assembled XML against the original `defaultmale.xml`
3. Differences should be limited to:
   - Whitespace/formatting
   - Comment ordering
   - Object emission order (IDs may differ but graph is isomorphic)

---

## Object→File Mapping (defaultmale)

| # | Class | Source File(s) |
|---|---|---|
| #0001 | hkRootLevelContainer | character.yaml |
| #0002 | hkbCharacterData | character.yaml |
| #0003 | hkbVariableValueSet | *derived from properties/*.yaml* |
| #0004–#0029 | hkbBoneWeightArray (×26) | properties/*.yaml (POINTER types) |
| #0030 | hkbFootIkDriverInfo | foot_ik.yaml |
| #0031 | hkbCharacterStringData | character.yaml + animations.txt + properties/*.yaml |
| #0032 | hkbMirroredSkeletonInfo | mirror.yaml |

Total: 32 objects from 40 source files (1 character.yaml + 1 animations.txt + 36 properties + 1 foot_ik + 1 mirror).
