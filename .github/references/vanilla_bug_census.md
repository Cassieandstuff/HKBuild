# Vanilla Behavior Bug Census

> Discovered via HKBuild's YAML intermediate format, which makes the full structure of Bethesda's Havok behavior graphs human-readable for the first time.
>
> **Source:** `behavior_src/vanilla/character/` — extracted from Skyrim SE 1.6.x vanilla HKX binaries.
>
> **Status:** Observational only. These bugs should NOT be fixed in a vanilla-matching recompile — changing variable/event indices would break every Nemesis/Pandora mod patch in existence.

---

## 1. Dead Variables — 648 / 1,462 (44%)

Nearly half the variables declared in `0_master.hkx/data/graphdata.yaml` are never referenced by any binding, clip trigger, or expression across all 16 vanilla behaviors.

Every character in the game allocates and carries these at runtime. Havok indexes past them so there's no crash, but it's ~44% bloat in the variable value set.

### Notable Dead Variables

| Variable | Category |
|---|---|
| `IntVariable` | Generic placeholder — never renamed |
| `BoolVariable` | Generic placeholder — never renamed |
| `testPitchOffset` | Debug leftover |
| `testIdleStart`, `testIdleLoop` | Debug leftover |
| `testEffectStart`, `testEffectStop` | Debug leftover |
| `TestSit_GetUp`, `TestSit_EnterBed`, `TestSit_StartLoop`, `TestSit_EnterChair`, `TestSit_Stop` | Entire scrapped sit-test system |
| `testDialogueIdleBStart` | Debug leftover |
| `iDrunkVariable` | Unused drunk system variable |
| `iState_NPCSprinting`, `iState_NPCDefault`, `iState_NPCSneaking`, `iState_NPCBowDrawn`, `iState_NPCBlocking`, `iState_NPCBleedout` | Unused NPC state tracking system |
| `DualMagicState`, `iDualMagicState`, `InDualMagicState` | Three variables for the same cut feature |
| `fIsFirstPerson` | Float version of the bool — unused |
| `FemaleOffset` | Unused gender offset |

### Curiosities (Used But Suspicious)

| Variable | Note |
|---|---|
| `testInt` | Has the word "test" but IS actually bound in a behavior |
| `0.5secondBlend` | Variable name containing a decimal point. Used. |
| `00NextClip` | Leading zeros in name. Used. |
| `iDrunkVariable` | Named like a test, declared as an int variable, never referenced |

---

## 2. Dead Events — 552 / 1,232 (45%)

Same pattern as variables. Nearly half the events in the master graph data are never referenced by any clip trigger, transition condition, event-driven modifier, or inline event property.

### Breakdown by Category

| Category | Count | Examples |
|---|---|---|
| Cut kill moves | 72 | `KillingBlow`, `KillMoveB` through `KillMoveL`, `KillMoveF`, `KillMoveG` |
| Cut paired animations (`pa_*`) | 61 | `pa_KillMoveSabreCat`, `pa_KillMoveDraugr`, `pa_KillMoveDragonA` |
| Test/debug events | 10 | `TestSit_*`, `testIdleStart`, `testEffectStart` |
| Cut horse events | 6 | `HorseEnterOut`, `HorseDeath`, `NPCStopHorseCamera` |
| Cut magic events | ~20 | `MRh_Cast_State_to_MRh_Readied_State`, `MRh_PreCharge_to_MRh_ChargeLoop` |
| Cut/orphaned foot events | 6 | `FootLeft2`, `FootRight2`, `FootLeft3`, `FootRight3`, `2_FootScuffLeft`, `2_FootScuffRight` |
| Cut content | misc | `ReanimateStart`, `IdleExecutioneeChop`, `IdleExecutioneeIdleEnterInstant` |

### Cut Kill Move Analysis

The dead kill move events suggest Bethesda planned significantly more kill move variety than shipped. The `pa_` prefix pattern indicates these were designed as paired animations (attacker + victim synchronized). Kill moves that exist as events but have no behavior wiring:

- `KillMoveB` through `KillMoveM` (letters B through M — only A shipped fully)
- `pa_KillMoveSabreCat` — a dedicated sabre cat kill move
- `pa_KillMoveDraugr`, `pa_KillMoveDraugrB` — draugr-specific kill moves
- `pa_KillMoveDragonA` — a dragon kill move variant

---

## 3. Shared Kill Move Path References — 14 Clips

Fourteen clips in `0_master.hkx` reference paired kill move animations using relative paths that don't exist in `defaultmale.hkx/animations.txt`:

```
..\sharedkillmoves\human&horse\paired_dismount.hkx
..\sharedkillmoves\human&horse\paired_mount.hkx
..\sharedkillmoves\human&dragon\paired_dragoncalldismount.hkx
..\sharedkillmoves\human&dragon\paired_dragoncallmount.hkx
..\sharedkillmoves\human&dragon\paired_bitegrapple.hkx
..\sharedkillmoves\human&dragon\paired_dragonmount.hkx
..\sharedkillmoves\human&werewolf\paired_ww_pairedheadsmash.hkx
..\sharedkillmoves\human&werewolf\paired_ww_pairedheadthrow.hkx
..\sharedkillmoves\human&werewolf\paired_ww_pairedmaulingwithhuman.hkx
..\sharedkillmoves\human&werewolf\paired_ww_pairedfeedingwithhuman.hkx
..\sharedkillmoves\human&wolf\paired_extractwerewolfspirit.hkx
..\sharedkillmoves\human&vampirelord\paired_vampirelord_feedfront.hkx (×2)
..\sharedkillmoves\human&vampirelord\paired_vampirelord_feedback.hkx
```

These work at runtime through Skyrim's paired animation resolution system (the engine finds the matching animation in the other actor's behavior graph), but they're structurally orphaned from the character's own animation list.

---

## 4. Zero-Duration Transitions — 66 Files

Sixty-six transition effect files specify `duration: 0.000000`. This means an instant snap between states with no blending. While some are clearly intentional (e.g., `DefaultBlendResetTransition` — reset should be instant), others may be placeholder values that were never tuned.

---

## 5. The "Never Delete, Only Append" Pattern

The overarching finding is that Bethesda's behavior development followed an append-only model:

1. Variables and events were added during development and **never removed**
2. Test/debug declarations were left in production builds
3. Cut content (kill moves, sit system, horse events) left dead entries rather than being cleaned up
4. Multiple naming attempts for the same concept (`DualMagicState` / `iDualMagicState` / `InDualMagicState`)

This is consistent with a large team iterating under deadline pressure on a binary format they couldn't easily diff or review. **This is exactly the problem HKBuild solves** — with YAML source, dead declarations are visible, diff-able, and removable.

---

## Methodology

All findings were produced by:
1. Extracting vanilla HKX binaries to YAML via `hkbuild extract`
2. Parsing `graphdata.yaml` for all variable and event declarations
3. Searching all 7,207 YAML files across 16 behaviors for references to each declaration
4. Cross-referencing clip animation paths against `defaultmale.hkx/animations.txt`

The YAML intermediate format makes this kind of analysis trivial — something that was previously impossible without reverse-engineering the binary HKX format directly.
