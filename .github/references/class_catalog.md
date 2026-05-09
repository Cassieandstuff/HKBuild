# HKBuild — HKX Class Catalog

All Havok class types found across the 18 vanilla humanoid behavior XMLs, plus character file types. Grouped by category with support status.

## Legend

- ✅ **Supported** — Reader + emitter implemented and verified
- ❌ **Not yet** — Needs implementation

---

## Infrastructure (always present)

| Class | Status | Count | Notes |
|---|---|---|---|
| `hkRootLevelContainer` | ✅ | 18 | Packfile root, auto-generated |
| `hkbBehaviorGraph` | ✅ | 18 | One per behavior file |
| `hkbBehaviorGraphData` | ✅ | 7 | Variable/event type definitions |
| `hkbBehaviorGraphStringData` | ✅ | 7 | Variable/event name strings |
| `hkbVariableValueSet` | ✅ | 7 | Initial variable values |
| `hkbVariableBindingSet` | ✅ | 1548 | Variable→property bindings on any node |

---

## State Machines

| Class | Status | Count | Notes |
|---|---|---|---|
| `hkbStateMachine` | ✅ | 867 | All 18 behaviors |
| `hkbStateMachineStateInfo` | ✅ | 2748 | All 18 behaviors |
| `hkbStateMachineEventPropertyArray` | ✅ | 787 | Enter/exit notify events |
| `hkbStateMachineTransitionInfoArray` | ✅ | 1027 | State transition definitions |

---

## Generators

| Class | Status | Count | Behaviors | Notes |
|---|---|---|---|---|
| `hkbClipGenerator` | ✅ | 2640 | All 18 | Animation clip playback |
| `hkbClipTriggerArray` | ✅ | 2089 | All 18 | Clip event triggers |
| `hkbBlenderGenerator` | ✅ | 515 | 16 | Weighted blend of children |
| `hkbBlenderGeneratorChild` | ✅ | 1671 | 16 | Child wrapper for blender |
| `hkbBoneWeightArray` | ✅ | 381 | 14 | Per-bone blend weights |
| `hkbManualSelectorGenerator` | ✅ | 196 | 13 | Variable-driven generator switch |
| `hkbBehaviorReferenceGenerator` | ✅ | 25 | 7 | References another behavior file |
| `BSBoneSwitchGenerator` | ✅ | 56 | 4 | Bone-masked generator switch (BS custom) |
| `BSBoneSwitchGeneratorBoneData` | ✅ | 54 | 4 | Bone data for BSBoneSwitchGenerator |
| `BSCyclicBlendTransitionGenerator` | ✅ | 43 | 8 | Cyclic blend with transitions (BS custom) |
| `BSSynchronizedClipGenerator` | ✅ | 143 | 3 | Synchronized paired animation |
| `BSiStateTaggingGenerator` | ✅ | 68 | 7 | Tags state for game queries (BS custom) |
| `BSOffsetAnimationGenerator` | ✅ | 2 | 1 | Offset animation (0_master only) |
| `hkbPoseMatchingGenerator` | ✅ | 3 | 1 | Pose match blend (0_master only) |

---

## Transition Effects

| Class | Status | Count | Notes |
|---|---|---|---|
| `hkbBlendingTransitionEffect` | ✅ | 194 | Blend-based state transitions, supports bindings |

---

## Modifiers

Modifiers wrap generators and modify output. The `hkbModifierGenerator` pairs a generator with a modifier (or modifier list).

| Class | Status | Count | Behaviors | Notes |
|---|---|---|---|---|
| `hkbModifierGenerator` | ✅ | 335 | 13 | Generator + modifier pair |
| `hkbModifierList` | ✅ | 58 | 10 | Ordered list of modifiers |
| `hkbEventDrivenModifier` | ✅ | 11 | 4 | Activates child modifier on event |
| `hkbEvaluateExpressionModifier` | ✅ | 34 | 7 | Expression-evaluated modifier |
| `hkbExpressionDataArray` | ✅ | 34 | 7 | Expression data for above |
| `hkbDampingModifier` | ✅ | 7 | 3 | Smooth value damping |
| `hkbTwistModifier` | ✅ | 5 | 3 | Bone twist |
| `hkbRotateCharacterModifier` | ✅ | 3 | 3 | Character rotation |
| `hkbTimerModifier` | ✅ | 2 | 2 | Timer-driven events |
| `hkbKeyframeBonesModifier` | ✅ | 3 | 2 | Keyframe bone control |
| `hkbFootIkControlsModifier` | ✅ | 2 | 2 | Foot IK runtime controls |
| `hkbRigidBodyRagdollControlsModifier` | ✅ | 2 | 2 | Rigid body ragdoll |
| `hkbPoweredRagdollControlsModifier` | ✅ | 2 | 1 | Powered ragdoll |
| `hkbGetUpModifier` | ✅ | 1 | 1 | Get-up from ragdoll |
| `BSIsActiveModifier` | ✅ | 81 | 12 | Checks if behavior is active (BS custom) |
| `BSDirectAtModifier` | ✅ | 8 | 3 | Aim/look-at direction (BS custom) |
| `BSEventOnFalseToTrueModifier` | ✅ | 8 | 3 | Fires event on bool transition (BS custom) |
| `BSEventOnDeactivateModifier` | ✅ | 15 | 2 | Fires event when deactivated (BS custom) |
| `BSEventEveryNEventsModifier` | ✅ | 40 | 2 | Fires event every N occurrences (BS custom) |
| `BSInterpValueModifier` | ✅ | 3 | 2 | Interpolates a value (BS custom) |
| `BSRagdollContactListenerModifier` | ✅ | 2 | 1 | Ragdoll contact events (BS custom) |
| `BSModifyOnceModifier` | ✅ | 2 | 1 | Runs child modifier once (BS custom) |
| `BSLookAtModifier` | ✅ | 3 | 1 | Look-at target (BS custom) |
| `BSSpeedSamplerModifier` | ✅ | 2 | 1 | Samples movement speed (BS custom) |

---

## Conditions

| Class | Status | Count | Behaviors | Notes |
|---|---|---|---|---|
| `hkbExpressionCondition` | ✅ | 62 | 8 | Expression-based transition condition |
| `hkbStringCondition` | ✅ | 2 | 2 | String comparison condition |

---

## Payloads

| Class | Status | Count | Behaviors | Notes |
|---|---|---|---|---|
| `hkbStringEventPayload` | ✅ | 66 | 4 | String payload attached to events |

---

## Misc

| Class | Status | Count | Behaviors | Notes |
|---|---|---|---|---|
| `hkbBoneIndexArray` | ✅ | 9 | 2 | Bone index list (used by keyframe/ragdoll modifiers) |
| `hkbEventRangeDataArray` | ✅ | 2 | 1 | Event range data |
| `hkbEventsFromRangeModifier` | ✅ | 2 | 1 | Fires events based on value ranges |

---

## Character File Types (separate pipeline)

| Class | Status | Notes |
|---|---|---|
| `hkRootLevelContainer` | ✅ | Shared with behavior |
| `hkbCharacterData` | ✅ | Character controller, model axes |
| `hkbCharacterStringData` | ✅ | Name, rig, ragdoll, behavior paths, animations |
| `hkbVariableValueSet` | ✅ | Property initial values |
| `hkbBoneWeightArray` | ✅ | Per-property bone masks |
| `hkbFootIkDriverInfo` | ✅ | Foot IK leg definitions |
| `hkbMirroredSkeletonInfo` | ✅ | Bone pair mirroring |

---

## Implementation Status

**Supported: 55/55 class types** — compiles **all 18/18** humanoid behaviors

Completed (most recent first):
- ✅ `BSOffsetAnimationGenerator`, `hkbPoseMatchingGenerator` — unlocked 0_master (18/18 complete)
- ✅ **Generic modifier system** — 17 modifier types via single data-driven emitter, plus `BSBoneSwitchGenerator`, `BSSynchronizedClipGenerator`, `hkbEvaluateExpressionModifier`, `hkbExpressionDataArray`, `hkbEventRangeDataArray`, `hkbEventsFromRangeModifier`, `hkbBoneIndexArray`, `hkbStringCondition` — unlocked 6 new behaviors (magicmountedbehavior, blockbehavior, magicbehavior, mt_behavior, 1hm_behavior, horsebehavior)
- ✅ `hkbEventDrivenModifier`, `BSEventEveryNEventsModifier` — unlocked idlebehavior; also added wildcard transition support
- ✅ `hkbModifierList`, `BSCyclicBlendTransitionGenerator`, `hkbBehaviorReferenceGenerator`, `hkbExpressionCondition` — unlocked 7 new behaviors
- ✅ `hkbModifierGenerator`, `BSIsActiveModifier`, `BSiStateTaggingGenerator`, `hkbStringEventPayload` — initial modifier/generator support
- ✅ All infrastructure, state machines, clips, blenders, selectors, transitions, bindings, bone weights

### No remaining unsupported types. 🎉
