using System.Globalization;
using System.Text;
using HKBuild.Models;

namespace HKBuild;

/// <summary>
/// Emits Havok packfile XML from loaded BehaviorData.
///
/// Two-pass architecture matching vanilla Havok compiler conventions:
///   Pass 1: BFS from root → assigns object IDs top-down.
///   Pass 2: DFS pre-order → emits objects top-down (parent before children).
/// </summary>
public class BehaviorXmlEmitter
{
    private readonly BehaviorData _data;
    private int _nextId = 3; // #0001=Root, #0002=BehaviorGraph, #0003+ = nodes

    // Node name -> assigned ID.
    private readonly Dictionary<string, string> _nodeIds = new();
    // Auxiliary object key -> assigned ID.
    // Key format: "{ownerName}:{auxType}" or "{ownerName}:{auxType}:{index}"
    private readonly Dictionary<string, string> _auxIds = new();
    // Track which nodes have been BFS-visited for ID assignment.
    private readonly HashSet<string> _bfsVisited = [];
    // Track which nodes have been DFS-visited for emission.
    private readonly HashSet<string> _dfsVisited = [];
    // Ordered list of emission actions.
    private readonly List<Action<StringBuilder>> _emissions = [];

    // The source file currently being emitted — set before each node emission
    // so that ResolveVariableIndex / ResolveEventId can include it in errors.
    // Format used in exceptions: "path(1): error : message" which VS parses as
    // a clickable error pointing to line 1 of the originating YAML file.
    private string _currentSourceFile = "(unknown)";

    // Class signatures.
    private const string SigRoot = "0x2772c11e";
    private const string SigBehaviorGraph = "0xb1218f86";
    private const string SigStateMachine = "0x816c1dcb";
    private const string SigStateInfo = "0xed7f9d0";
    private const string SigClipGenerator = "0x333b85b9";
    private const string SigClipTriggerArray = "0x59c23a0f";
    private const string SigBlenderGenerator = "0x22df7147";
    private const string SigBlenderGeneratorChild = "0xe2b384b0";
    private const string SigManualSelectorGenerator = "0xd932fab8";
    private const string SigVariableBindingSet = "0x338ad4ff";
    private const string SigBoneWeightArray = "0xcd902b77";
    private const string SigEventPropertyArray = "0xb07b4388";
    private const string SigBlendingTransitionEffect = "0xfd8584fe";
    private const string SigTransitionInfoArray = "0xe397b11e";
    private const string SigBehaviorGraphData = "0x95aca5d";
    private const string SigVariableValueSet = "0x27812d8d";
    private const string SigBehaviorGraphStringData = "0xc713064e";
    private const string SigModifierGenerator = "0x1f81fae6";
    private const string SigBSIsActiveModifier = "0xb0fde45a";
    private const string SigBSiStateTaggingGenerator = "0xf0826fc1";
    private const string SigStringEventPayload = "0xed04256a";
    private const string SigBehaviorReferenceGenerator = "0xfcb5423";
    private const string SigModifierList = "0xa4180ca1";
    private const string SigBSCyclicBlendTransitionGenerator = "0x5119eb06";
    private const string SigExpressionCondition = "0x1c3c1045";
    private const string SigEventDrivenModifier = "0x7ed3f44e";
    private const string SigBSEventEveryNEventsModifier = "0x6030970c";
    private const string SigEvaluateExpressionModifier = "0xf900f6be";
    private const string SigExpressionDataArray = "0x4b9ee1a2";
    private const string SigEventRangeDataArray = "0x330a56ee";
    private const string SigBoneIndexArray = "0x00aa8619";
    private const string SigBSBoneSwitchGenerator = "0xf33d3eea";
    private const string SigBSBoneSwitchGeneratorBoneData = "0xc1215be6";
    private const string SigStringCondition = "0x5ab50487";
    private const string SigBSSynchronizedClipGenerator = "0xd83bea64";
    private const string SigBSOffsetAnimationGenerator = "0xb8571122";
    private const string SigPoseMatchingGenerator = "0x29e271b4";
    private const string SigFootIkControlsModifier = "0xe5b6f544";

    /// <summary>Signature lookup for generic modifier classes.</summary>
    private static readonly Dictionary<string, string> GenericModifierSignatures = new()
    {
        ["BSDirectAtModifier"] = "0x19a005c0",
        ["BSEventOnFalseToTrueModifier"] = "0x81d0777a",
        ["BSEventOnDeactivateModifier"] = "0x1062d993",
        ["BSInterpValueModifier"] = "0x29adc802",
        ["BSModifyOnceModifier"] = "0x1e20a97a",
        ["BSSpeedSamplerModifier"] = "0xd297fda9",
        ["BSLookAtModifier"] = "0xd756fc25",
        ["BSRagdollContactListenerModifier"] = "0x8003d8ce",
        ["hkbDampingModifier"] = "0x9a040f03",
        ["hkbTwistModifier"] = "0xb6b76b32",
        ["hkbRotateCharacterModifier"] = "0x877ebc0b",
        ["hkbTimerModifier"] = "0x338b4879",
        ["hkbKeyframeBonesModifier"] = "0x95f66629",
        ["hkbGetUpModifier"] = "0x61cb7ac0",
        ["hkbPoweredRagdollControlsModifier"] = "0x7cb54065",
        ["hkbRigidBodyRagdollControlsModifier"] = "0xaa87d1eb",
        ["hkbEventsFromRangeModifier"] = "0xbc561b6e",
    };

    public BehaviorXmlEmitter(BehaviorData data)
    {
        _data = data;

        // Build event/variable name → index lookup from graph data.
        if (data.GraphData != null)
        {
            for (int i = 0; i < data.GraphData.Events.Count; i++)
                _eventNameToId[data.GraphData.Events[i].Name] = i;
            for (int i = 0; i < data.GraphData.Variables.Count; i++)
                _variableNameToIndex[data.GraphData.Variables[i].Name] = i;
            for (int i = 0; i < data.GraphData.CharacterPropertyNames.Count; i++)
                _charPropNameToIndex[data.GraphData.CharacterPropertyNames[i].Name] = i;
        }
    }

    // Event name → index lookup (built from graph data).
    private readonly Dictionary<string, int> _eventNameToId = new();
    // Variable name → index lookup (built from graph data).
    private readonly Dictionary<string, int> _variableNameToIndex = new();
    // Character property name → index lookup (built from graph data).
    private readonly Dictionary<string, int> _charPropNameToIndex = new();
    // String event payload data → assigned object ID (deduplication).
    private readonly Dictionary<string, string> _payloadIds = new();

    /// <summary>
    /// Resolve an event reference: prefer name (if present), fall back to numeric ID.
    /// </summary>
    private int ResolveEventId(string? eventName, int eventId)
    {
        if (eventName != null)
        {
            if (_eventNameToId.TryGetValue(eventName, out int id))
                return id;
            throw new InvalidOperationException(
                $"{_currentSourceFile}(1): error : Unknown event name '{eventName}'." +
                $" Available: {string.Join(", ", _eventNameToId.Keys)}");
        }
        return eventId;
    }

    /// <summary>
    /// Resolve a variable reference: prefer name (if present), fall back to numeric index.
    /// </summary>
    private int ResolveVariableIndex(string? variableName, int variableIndex)
    {
        if (variableName != null)
        {
            if (_variableNameToIndex.TryGetValue(variableName, out int idx))
                return idx;
            throw new InvalidOperationException(
                $"{_currentSourceFile}(1): error : Unknown variable name '{variableName}'." +
                $" Available: {string.Join(", ", _variableNameToIndex.Keys)}");
        }
        return variableIndex;
    }

    /// <summary>
    /// Resolve a character property reference: prefer name, fall back to numeric index.
    /// Handles misextracted YAML where the extractor stored variable names instead of
    /// character property names (both share the same original numeric index).
    /// </summary>
    private int ResolveCharPropIndex(string? propName, int propIndex)
    {
        if (propName != null)
        {
            // Direct match against character property names.
            if (_charPropNameToIndex.TryGetValue(propName, out int idx))
                return idx;
            // Misextracted: extractor stored variable name at same index. Use the variable's
            // index as the character property index (they share the original HKX index).
            if (_variableNameToIndex.TryGetValue(propName, out int varIdx))
                return varIdx;
            Console.Error.WriteLine($"  WARNING: Unknown character property '{propName}', using index {propIndex}");
            return propIndex;
        }
        return propIndex;
    }

    /// <summary>
    /// Get or allocate an ID for a string event payload. Returns "null" for null/empty payloads.
    /// </summary>
    private string ResolvePayloadId(string? payload)
    {
        if (string.IsNullOrEmpty(payload) || payload == "null") return "null";
        if (_payloadIds.TryGetValue(payload, out var id)) return id;
        id = AllocId();
        _payloadIds[payload] = id;
        return id;
    }

    public string Emit()
    {
        var rootGenName = _data.Behavior.Behavior.RootGenerator;

        // Pass 1: BFS from root to assign IDs top-down.
        AssignIdsBfs(rootGenName);

        // Pass 2: DFS pre-order from root to build emission list.
        // Vanilla order: Root → BehaviorGraph → generator subtree → GraphData
        EmitNodeDfs(rootGenName);

        // Pre-allocate graph data IDs so BehaviorGraph can reference them.
        if (_data.GraphData != null)
            AllocGraphDataIds();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"ascii\"?>");
        sb.AppendLine($"<hkpackfile classversion=\"{_data.Behavior.Packfile.ClassVersion}\" contentsversion=\"{_data.Behavior.Packfile.ContentsVersion}\" toplevelobject=\"#0001\">");
        sb.AppendLine();
        sb.AppendLine("\t<hksection name=\"__data__\">");
        sb.AppendLine();

        // Emit root and behavior graph first (matches vanilla ordering).
        EmitRootLevelContainer(sb);
        EmitBehaviorGraph(sb);

        foreach (var emit in _emissions)
            emit(sb);

        // Emit graph data objects last (matches vanilla ordering).
        if (_data.GraphData != null)
            EmitGraphData(sb);

        sb.AppendLine("\t</hksection>");
        sb.AppendLine();
        sb.AppendLine("</hkpackfile>");

        Console.WriteLine($"  Assigned IDs: #0001..#{_nextId - 1:D4} ({_nextId - 1} objects)");
        return sb.ToString();
    }

    private string AllocId() => $"#{_nextId++:D4}";

    // ══════════════════════════════════════════════════════════════
    //  Pass 1: DFS pre-order — assign IDs top-down
    //  (matches hkxcmd convention: parent gets ID before children)
    // ══════════════════════════════════════════════════════════════

    private void AssignIdsBfs(string rootName)
    {
        AssignIdsDfs(rootName);
    }

    private void AssignIdsDfs(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;

        // Each collection is checked independently — a name may exist in multiple
        // collections (e.g. a clip and a state machine can share the same display name).
        // We use NodeKey(collection, name) to disambiguate in _nodeIds / _bfsVisited.
        bool found = false;

        if (_data.StateMachines.TryGetValue(name, out var sm))
        {
            found = true;
            var nk = NodeKey("sm", sm.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (sm.Bindings != null && sm.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                if (sm.ParsedWildcardTransitions != null && sm.ParsedWildcardTransitions.Count > 0)
                {
                    _auxIds[$"{nk}:wildcardTransitions"] = AllocId();
                    foreach (var t in sm.ParsedWildcardTransitions)
                    {
                        AssignTransitionEffectId(t.Transition);
                        if (!string.IsNullOrEmpty(t.Condition) && t.Condition != "null"
                            && !_auxIds.ContainsKey($"cond:{t.Condition}"))
                            _auxIds[$"cond:{t.Condition}"] = AllocId();
                        if (!string.IsNullOrEmpty(t.ConditionString) && t.ConditionString != "null"
                            && !_auxIds.ContainsKey($"strcond:{t.ConditionString}"))
                            _auxIds[$"strcond:{t.ConditionString}"] = AllocId();
                    }
                }

                foreach (var stateName in sm.States)
                {
                    var sk = StateKey(stateName);
                    if (_bfsVisited.Contains(sk)) continue;
                    _bfsVisited.Add(sk);
                    var st = _data.States[stateName];
                    AssignStateIdsDfs(st);
                }
            }
        }
        if (_data.Selectors.TryGetValue(name, out var sel))
        {
            found = true;
            var nk = NodeKey("sel", sel.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (sel.Bindings != null && sel.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                foreach (var genName in sel.Generators)
                    AssignIdsDfs(genName);
            }
        }
        if (_data.Blenders.TryGetValue(name, out var blend))
        {
            found = true;
            var nk = NodeKey("blend", blend.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (blend.Bindings != null && blend.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                for (int i = 0; i < blend.Children.Count; i++)
                {
                    var child = blend.Children[i];
                    if (string.IsNullOrEmpty(child.Generator) || child.Generator == "null")
                        continue;

                    _auxIds[$"{nk}:child:{i}"] = AllocId();
                    AssignIdsDfs(child.Generator);

                    if (child.BoneWeights != null && child.BoneWeights.HasData)
                        _auxIds[$"{nk}:bw:{i}"] = AllocId();
                }
            }
        }
        if (_data.Clips.TryGetValue(name, out var clip))
        {
            found = true;
            var nk = NodeKey("clip", clip.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (clip.Triggers != null && clip.Triggers.Count > 0)
                {
                    foreach (var t in clip.Triggers)
                        ResolvePayloadId(t.Payload);
                    _auxIds[$"{nk}:triggers"] = AllocId();
                }
            }
        }
        if (_data.ModifierGenerators.TryGetValue(name, out var mg))
        {
            found = true;
            var nk = NodeKey("mg", mg.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (mg.Bindings != null && mg.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignIdsDfs(mg.Modifier);
                AssignIdsDfs(mg.Generator);
            }
        }
        if (_data.IsActiveModifiers.TryGetValue(name, out var iam))
        {
            found = true;
            var nk = NodeKey("iam", iam.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (iam.Bindings != null && iam.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();
            }
        }
        if (_data.StateTaggingGenerators.TryGetValue(name, out var stg))
        {
            found = true;
            var nk = NodeKey("stg", stg.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (stg.Bindings != null && stg.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignIdsDfs(stg.PDefaultGenerator);
            }
        }
        if (_data.BehaviorReferences.TryGetValue(name, out var bref))
        {
            found = true;
            var nk = NodeKey("bref", bref.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (bref.Bindings != null && bref.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();
            }
        }
        if (_data.ModifierLists.TryGetValue(name, out var ml))
        {
            found = true;
            var nk = NodeKey("ml", ml.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (ml.Bindings != null && ml.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                foreach (var modName in ml.Modifiers)
                    AssignIdsDfs(modName);
            }
        }
        if (_data.CyclicBlendGenerators.TryGetValue(name, out var cb))
        {
            found = true;
            var nk = NodeKey("cb", cb.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (cb.Bindings != null && cb.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignIdsDfs(cb.PBlenderGenerator);
            }
        }
        if (_data.EventDrivenModifiers.TryGetValue(name, out var edm))
        {
            found = true;
            var nk = NodeKey("edm", edm.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (edm.Bindings != null && edm.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignIdsDfs(edm.Modifier);
            }
        }
        if (_data.EventEveryNModifiers.TryGetValue(name, out var een))
        {
            found = true;
            var nk = NodeKey("een", een.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (een.Bindings != null && een.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();
            }
        }
        if (_data.GenericModifiers.TryGetValue(name, out var gm))
        {
            found = true;
            var nk = NodeKey("gm", gm.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (gm.Bindings != null && gm.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                foreach (var p in gm.ExtraParams)
                {
                    if (p.Kind == GenericParamKind.Scalar && p.ScalarValue != null)
                    {
                        if (IsDataArrayParam(p.Name) && _data.ExpressionDataArrays.ContainsKey(p.ScalarValue))
                            AssignExpressionDataArrayId(p.ScalarValue);
                        else if (IsDataArrayParam(p.Name) && _data.EventRangeDataArrays.ContainsKey(p.ScalarValue))
                            AssignEventRangeDataArrayId(p.ScalarValue);
                        else if (IsDataArrayParam(p.Name) && _data.BoneIndexArrays.ContainsKey(p.ScalarValue))
                            AssignBoneIndexArrayId(p.ScalarValue);
                        else if (_data.GetNodeClass(p.ScalarValue) != null)
                            AssignIdsDfs(p.ScalarValue);
                        else if (_data.BoneIndexArrays.ContainsKey(p.ScalarValue))
                            AssignBoneIndexArrayId(p.ScalarValue);
                        else if (_data.EventRangeDataArrays.ContainsKey(p.ScalarValue))
                            AssignEventRangeDataArrayId(p.ScalarValue);
                    }
                    else if (p.Kind == GenericParamKind.RefList && p.RefListValue != null)
                    {
                        foreach (var r in p.RefListValue)
                            if (_data.GetNodeClass(r) != null)
                                AssignIdsDfs(r);
                    }
                }
            }
        }
        if (_data.FootIkControlsModifiers.TryGetValue(name, out var ficm))
        {
            found = true;
            var nk = NodeKey("ficm", ficm.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (ficm.Bindings != null && ficm.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();
            }
        }
        if (_data.EvaluateExpressionModifiers.TryGetValue(name, out var eem))
        {
            found = true;
            var nk = NodeKey("eem", eem.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (eem.Bindings != null && eem.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignExpressionDataArrayId(eem.Expressions);
            }
        }
        if (_data.BoneSwitchGenerators.TryGetValue(name, out var bsg))
        {
            found = true;
            var nk = NodeKey("bsg", bsg.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (bsg.Bindings != null && bsg.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                if (bsg.Children != null)
                {
                    for (int i = 0; i < bsg.Children.Count; i++)
                    {
                        var child = bsg.Children[i];
                        var childKey = $"{nk}:child:{i}";
                        _auxIds[childKey] = AllocId();
                        if (child.Bindings != null && child.Bindings.Count > 0)
                            _auxIds[$"{childKey}:binding"] = AllocId();
                        if (child.BoneWeights?.HasData == true)
                            _auxIds[$"{childKey}:boneWeight"] = AllocId();
                        AssignIdsDfs(child.PGenerator);
                    }
                }

                AssignIdsDfs(bsg.PDefaultGenerator);
            }
        }
        if (_data.SynchronizedClips.TryGetValue(name, out var sc))
        {
            found = true;
            var nk = NodeKey("sc", sc.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (sc.Bindings != null && sc.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignIdsDfs(sc.PClipGenerator);
            }
        }
        if (_data.OffsetAnimGenerators.TryGetValue(name, out var oag))
        {
            found = true;
            var nk = NodeKey("oag", oag.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (oag.Bindings != null && oag.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                AssignIdsDfs(oag.PDefaultGenerator);
                AssignIdsDfs(oag.POffsetClipGenerator);
            }
        }
        if (_data.PoseMatchingGenerators.TryGetValue(name, out var pmg))
        {
            found = true;
            var nk = NodeKey("pmg", pmg.Name);
            if (_bfsVisited.Add(nk))
            {
                _nodeIds[nk] = AllocId();

                if (pmg.Bindings != null && pmg.Bindings.Count > 0)
                    _auxIds[$"{nk}:binding"] = AllocId();

                if (pmg.Children != null)
                {
                    for (int i = 0; i < pmg.Children.Count; i++)
                    {
                        var child = pmg.Children[i];
                        if (string.IsNullOrEmpty(child.Generator) || child.Generator == "null")
                            continue;

                        _auxIds[$"{nk}:child:{i}"] = AllocId();
                        AssignIdsDfs(child.Generator);
                        if (child.BoneWeights != null && child.BoneWeights.HasData)
                            _auxIds[$"{nk}:bw:{i}"] = AllocId();
                    }
                }
            }
        }

        if (!found)
        {
            Console.Error.WriteLine($"  WARNING: Unknown node '{name}' during ID assignment");
        }
    }

    /// <summary>Assign ID for an expression data array (if not already assigned).</summary>
    private void AssignExpressionDataArrayId(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;
        if (_auxIds.ContainsKey($"eda:{name}")) return;
        if (_data.ExpressionDataArrays.TryGetValue(name, out _))
            _auxIds[$"eda:{name}"] = AllocId();
    }

    /// <summary>Assign ID for an event range data array (if not already assigned).</summary>
    private void AssignEventRangeDataArrayId(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;
        if (_auxIds.ContainsKey($"erda:{name}")) return;
        if (_data.EventRangeDataArrays.TryGetValue(name, out _))
            _auxIds[$"erda:{name}"] = AllocId();
    }

    /// <summary>Assign ID for a bone index array (if not already assigned).</summary>
    private void AssignBoneIndexArrayId(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;
        if (_auxIds.ContainsKey($"bia:{name}")) return;
        if (_data.BoneIndexArrays.TryGetValue(name, out _))
            _auxIds[$"bia:{name}"] = AllocId();
    }

    /// <summary>Assign an ID to a transition effect (if not already assigned).</summary>
    private void AssignTransitionEffectId(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;
        var nk = NodeKey("te", name);
        if (_nodeIds.ContainsKey(nk)) return;
        if (!_data.TransitionEffects.TryGetValue(name, out var effect))
        {
            Console.Error.WriteLine($"  WARNING: Unknown transition effect '{name}'");
            return;
        }

        if (effect.Bindings != null && effect.Bindings.Count > 0)
            _auxIds[$"{nk}:binding"] = AllocId();

        _nodeIds[nk] = AllocId();
    }

    /// <summary>Assign IDs for a state and its auxiliary objects. Uses StateKey() for the state's own ID.</summary>
    private void AssignStateIdsDfs(StateDef state)
    {
        var sk = StateKey(state.Name);
        _nodeIds[sk] = AllocId();

        // Auxiliary: enter/exit event arrays — allocate payload IDs first.
        var enterEvents = state.EnterNotifyEvents as List<EventPropertyDef>;
        var exitEvents = state.ExitNotifyEvents as List<EventPropertyDef>;
        if (enterEvents != null && enterEvents.Count > 0)
        {
            foreach (var ev in enterEvents)
                ResolvePayloadId(ev.Payload);
            _auxIds[$"{sk}:enter"] = AllocId();
        }
        if (exitEvents != null && exitEvents.Count > 0)
        {
            foreach (var ev in exitEvents)
                ResolvePayloadId(ev.Payload);
            _auxIds[$"{sk}:exit"] = AllocId();
        }

        // Auxiliary: transition info array + referenced transition effects.
        if (state.ParsedTransitions != null && state.ParsedTransitions.Count > 0)
        {
            _auxIds[$"{sk}:transitions"] = AllocId();
            foreach (var t in state.ParsedTransitions)
            {
                AssignTransitionEffectId(t.Transition);
                // Allocate expression condition objects (deduplicated by expression string).
                if (!string.IsNullOrEmpty(t.Condition) && t.Condition != "null"
                    && !_auxIds.ContainsKey($"cond:{t.Condition}"))
                    _auxIds[$"cond:{t.Condition}"] = AllocId();
                // Allocate string condition objects (deduplicated by string value).
                if (!string.IsNullOrEmpty(t.ConditionString) && t.ConditionString != "null"
                    && !_auxIds.ContainsKey($"strcond:{t.ConditionString}"))
                    _auxIds[$"strcond:{t.ConditionString}"] = AllocId();
            }
        }

        // Generator subtree (recurse into it after events/transitions).
        AssignIdsDfs(state.Generator);
    }

    // ══════════════════════════════════════════════════════════════
    //  Pass 2: DFS pre-order — emit objects top-down
    // ══════════════════════════════════════════════════════════════

    private void EmitNodeDfs(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;

        // Update source file context so any resolve errors name the originating file.
        if (_data.SourceFiles.TryGetValue(name, out var srcFile))
            _currentSourceFile = srcFile;

        // Each collection checked independently (same name can exist in multiple collections).
        if (_data.StateMachines.TryGetValue(name, out var sm)
            && _dfsVisited.Add(NodeKey("sm", name)))
            EmitStateMachineDfs(sm);
        if (_data.Selectors.TryGetValue(name, out var sel)
            && _dfsVisited.Add(NodeKey("sel", name)))
            EmitManualSelectorDfs(sel);
        if (_data.Blenders.TryGetValue(name, out var blend)
            && _dfsVisited.Add(NodeKey("blend", name)))
            EmitBlenderGeneratorDfs(blend);
        if (_data.Clips.TryGetValue(name, out var clip)
            && _dfsVisited.Add(NodeKey("clip", name)))
            EmitClipGeneratorDfs(clip);
        if (_data.ModifierGenerators.TryGetValue(name, out var mg)
            && _dfsVisited.Add(NodeKey("mg", name)))
            EmitModifierGeneratorDfs(mg);
        if (_data.IsActiveModifiers.TryGetValue(name, out var iam)
            && _dfsVisited.Add(NodeKey("iam", name)))
            EmitIsActiveModifierDfs(iam);
        if (_data.StateTaggingGenerators.TryGetValue(name, out var stg)
            && _dfsVisited.Add(NodeKey("stg", name)))
            EmitStateTaggingGeneratorDfs(stg);
        if (_data.BehaviorReferences.TryGetValue(name, out var bref)
            && _dfsVisited.Add(NodeKey("bref", name)))
            EmitBehaviorReferenceDfs(bref);
        if (_data.ModifierLists.TryGetValue(name, out var ml)
            && _dfsVisited.Add(NodeKey("ml", name)))
            EmitModifierListDfs(ml);
        if (_data.CyclicBlendGenerators.TryGetValue(name, out var cb)
            && _dfsVisited.Add(NodeKey("cb", name)))
            EmitCyclicBlendGeneratorDfs(cb);
        if (_data.EventDrivenModifiers.TryGetValue(name, out var edm)
            && _dfsVisited.Add(NodeKey("edm", name)))
            EmitEventDrivenModifierDfs(edm);
        if (_data.EventEveryNModifiers.TryGetValue(name, out var een)
            && _dfsVisited.Add(NodeKey("een", name)))
            EmitEventEveryNModifierDfs(een);
        if (_data.GenericModifiers.TryGetValue(name, out var gm)
            && _dfsVisited.Add(NodeKey("gm", name)))
            EmitGenericModifierDfs(gm);
        if (_data.FootIkControlsModifiers.TryGetValue(name, out var ficm)
            && _dfsVisited.Add(NodeKey("ficm", name)))
            EmitFootIkControlsModifierDfs(ficm);
        if (_data.EvaluateExpressionModifiers.TryGetValue(name, out var eem)
            && _dfsVisited.Add(NodeKey("eem", name)))
            EmitEvaluateExpressionModifierDfs(eem);
        if (_data.BoneSwitchGenerators.TryGetValue(name, out var bsg)
            && _dfsVisited.Add(NodeKey("bsg", name)))
            EmitBoneSwitchGeneratorDfs(bsg);
        if (_data.SynchronizedClips.TryGetValue(name, out var sc)
            && _dfsVisited.Add(NodeKey("sc", name)))
            EmitSynchronizedClipDfs(sc);
        if (_data.OffsetAnimGenerators.TryGetValue(name, out var oag)
            && _dfsVisited.Add(NodeKey("oag", name)))
            EmitOffsetAnimGeneratorDfs(oag);
        if (_data.PoseMatchingGenerators.TryGetValue(name, out var pmg)
            && _dfsVisited.Add(NodeKey("pmg", name)))
            EmitPoseMatchingGeneratorDfs(pmg);
    }

    private void EmitStateMachineDfs(StateMachineDef sm)
    {
        // Pre-order: emit SM itself first.
        string? bindingId = null;
        if (sm.Bindings != null && sm.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("sm", sm.Name)}:binding"];

        string? wildcardTransId = null;
        if (sm.ParsedWildcardTransitions != null && sm.ParsedWildcardTransitions.Count > 0)
            wildcardTransId = _auxIds[$"{NodeKey("sm", sm.Name)}:wildcardTransitions"];

        var id = _nodeIds[NodeKey("sm", sm.Name)];
        var capturedBindingId = bindingId;
        var capturedWcId = wildcardTransId;
        _emissions.Add(sb => EmitStateMachine(sb, sm, id, capturedBindingId, capturedWcId));

        // Then binding set.
        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, sm.Bindings!));
        }

        // Emit each state's subtree (state → transitions → transition effects → generator).
        foreach (var stateName in sm.States)
        {
            var sk = StateKey(stateName);
            if (_dfsVisited.Contains(sk)) continue;
            _dfsVisited.Add(sk);
            EmitStateDfs(_data.States[stateName]);
        }

        // Wildcard transitions after all states.
        if (sm.ParsedWildcardTransitions != null && sm.ParsedWildcardTransitions.Count > 0)
        {
            var capturedWcTransId = wildcardTransId!;
            var capturedWcTransitions = sm.ParsedWildcardTransitions;
            _emissions.Add(sb => EmitTransitionInfoArray(sb, capturedWcTransId, capturedWcTransitions));

            EmitTransitionEffectsAndConditions(sm.ParsedWildcardTransitions);
        }
    }

    private void EmitStateDfs(StateDef state)
    {
        // Update source file context for resolve errors.
        if (_data.SourceFiles.TryGetValue(state.Name, out var srcFile))
            _currentSourceFile = srcFile;

        // Pre-order: emit state itself first.
        var enterEvents = state.EnterNotifyEvents as List<EventPropertyDef>;
        var exitEvents = state.ExitNotifyEvents as List<EventPropertyDef>;

        string? enterEvId = null;
        string? exitEvId = null;
        if (enterEvents != null && enterEvents.Count > 0)
            enterEvId = _auxIds[$"{StateKey(state.Name)}:enter"];
        if (exitEvents != null && exitEvents.Count > 0)
            exitEvId = _auxIds[$"{StateKey(state.Name)}:exit"];

        string? transId = null;
        if (state.ParsedTransitions != null && state.ParsedTransitions.Count > 0)
            transId = _auxIds[$"{StateKey(state.Name)}:transitions"];

        var stateId = _nodeIds[StateKey(state.Name)];
        _emissions.Add(sb => EmitState(sb, state, stateId, enterEvId, exitEvId, transId));

        // Then event arrays.
        EmitPayloadsForEvents(enterEvents);
        EmitPayloadsForEvents(exitEvents);
        if (enterEvId != null)
        {
            var capturedId = enterEvId;
            _emissions.Add(sb => EmitEventPropertyArray(sb, capturedId, enterEvents!));
        }
        if (exitEvId != null)
        {
            var capturedId = exitEvId;
            _emissions.Add(sb => EmitEventPropertyArray(sb, capturedId, exitEvents!));
        }

        // Then transition info array and referenced effects.
        if (state.ParsedTransitions != null && state.ParsedTransitions.Count > 0)
        {
            var capturedTransId = transId!;
            var capturedTransitions = state.ParsedTransitions;
            _emissions.Add(sb => EmitTransitionInfoArray(sb, capturedTransId, capturedTransitions));

            EmitTransitionEffectsAndConditions(state.ParsedTransitions);
        }

        // Then the state's generator subtree.
        EmitNodeDfs(state.Generator);
    }

    /// <summary>
    /// Emit transition effects and their conditions for a list of transitions.
    /// Shared between state transitions and wildcard transitions.
    /// </summary>
    private void EmitTransitionEffectsAndConditions(List<TransitionInfoDef> transitions)
    {
        foreach (var t in transitions)
        {
            if (!string.IsNullOrEmpty(t.Transition) && t.Transition != "null"
                && _data.TransitionEffects.TryGetValue(t.Transition, out var effect)
                && !_dfsVisited.Contains(t.Transition))
            {
                _dfsVisited.Add(t.Transition);
                var effId = _nodeIds[NodeKey("te", t.Transition)];
                var capturedEffect = effect;

                string? effBindingId = null;
                if (capturedEffect.Bindings != null && capturedEffect.Bindings.Count > 0)
                    effBindingId = _auxIds[$"{NodeKey("te", t.Transition)}:binding"];

                var capturedEffBindingId = effBindingId;
                _emissions.Add(sb => EmitTransitionEffect(sb, capturedEffect, effId, capturedEffBindingId));

                if (effBindingId != null)
                {
                    var capturedBindId = effBindingId;
                    var capturedBindings = capturedEffect.Bindings!;
                    _emissions.Add(sb => EmitVariableBindingSet(sb, capturedBindId, capturedBindings));
                }
            }

            if (!string.IsNullOrEmpty(t.Condition) && t.Condition != "null"
                && _auxIds.TryGetValue($"cond:{t.Condition}", out var condId)
                && !_dfsVisited.Contains($"cond:{t.Condition}"))
            {
                _dfsVisited.Add($"cond:{t.Condition}");
                var capturedExpr = t.Condition;
                var capturedCondId = condId;
                _emissions.Add(sb => EmitExpressionCondition(sb, capturedCondId, capturedExpr));
            }

            if (!string.IsNullOrEmpty(t.ConditionString) && t.ConditionString != "null"
                && _auxIds.TryGetValue($"strcond:{t.ConditionString}", out var sCondId)
                && !_dfsVisited.Contains($"strcond:{t.ConditionString}"))
            {
                _dfsVisited.Add($"strcond:{t.ConditionString}");
                var capturedStr = t.ConditionString;
                var capturedSCondId = sCondId;
                _emissions.Add(sb => EmitStringCondition(sb, capturedSCondId, capturedStr));
            }
        }
    }

    private void EmitManualSelectorDfs(ManualSelectorDef sel)
    {
        // Pre-order: emit selector first.
        string? bindingId = null;
        if (sel.Bindings != null && sel.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("sel", sel.Name)}:binding"];

        var id = _nodeIds[NodeKey("sel", sel.Name)];
        _emissions.Add(sb => EmitManualSelector(sb, sel, id, bindingId));

        // Then binding set.
        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, sel.Bindings!));
        }

        // Then child generators.
        foreach (var genName in sel.Generators)
            EmitNodeDfs(genName);
    }

    private void EmitBlenderGeneratorDfs(BlenderGeneratorDef blend)
    {
        // Pre-order: collect child IDs first (needed by BlenderGenerator emission),
        // then emit blender, binding, and children.
        string? bindingId = null;
        if (blend.Bindings != null && blend.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("blend", blend.Name)}:binding"];

        var childIds = new List<string>();
        for (int i = 0; i < blend.Children.Count; i++)
        {
            var child = blend.Children[i];
            if (string.IsNullOrEmpty(child.Generator) || child.Generator == "null")
                childIds.Add("null");
            else
                childIds.Add(_auxIds[$"{NodeKey("blend", blend.Name)}:child:{i}"]);
        }

        // Emit blender itself first.
        var id = _nodeIds[NodeKey("blend", blend.Name)];
        _emissions.Add(sb => EmitBlenderGenerator(sb, blend, id, bindingId, childIds));

        // Then binding set.
        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, blend.Bindings!));
        }

        // Then children: each child wrapper → child's generator subtree → bone weight array.
        for (int i = 0; i < blend.Children.Count; i++)
        {
            var child = blend.Children[i];
            if (string.IsNullOrEmpty(child.Generator) || child.Generator == "null")
                continue;

            // Emit bone weight array if needed.
            string? bwId = null;
            if (child.BoneWeights != null && child.BoneWeights.HasData)
                bwId = _auxIds[$"{NodeKey("blend", blend.Name)}:bw:{i}"];

            // Emit BlenderGeneratorChild wrapper.
            var childId = _auxIds[$"{NodeKey("blend", blend.Name)}:child:{i}"];
            var capturedChild = child;
            var capturedChildId = childId;
            var capturedBwId = bwId;
            _emissions.Add(sb => EmitBlenderGeneratorChild(sb, capturedChild, capturedChildId, capturedBwId));

            // Emit the child's generator subtree.
            EmitNodeDfs(child.Generator);

            // Emit bone weight array after generator subtree.
            if (bwId != null)
            {
                var capturedBwId2 = bwId;
                var capturedBw = child.BoneWeights!;
                _emissions.Add(sb => EmitBoneWeightArray(sb, capturedBwId2, capturedBw));
            }
        }
    }

    private void EmitClipGeneratorDfs(ClipGeneratorDef clip)
    {
        // Pre-order: emit clip itself first.
        string? trigId = null;
        if (clip.Triggers != null && clip.Triggers.Count > 0)
            trigId = _auxIds[$"{NodeKey("clip", clip.Name)}:triggers"];

        var id = _nodeIds[NodeKey("clip", clip.Name)];
        _emissions.Add(sb => EmitClipGenerator(sb, clip, id, trigId));

        // Then trigger array and payloads.
        if (clip.Triggers != null && clip.Triggers.Count > 0)
        {
            var capturedTrigId = trigId!;
            var capturedTriggers = clip.Triggers;
            _emissions.Add(sb => EmitClipTriggerArray(sb, capturedTrigId, capturedTriggers));

            foreach (var t in clip.Triggers)
            {
                if (!string.IsNullOrEmpty(t.Payload) && t.Payload != "null"
                    && _payloadIds.TryGetValue(t.Payload, out var payId)
                    && !_dfsVisited.Contains($"payload:{t.Payload}"))
                {
                    _dfsVisited.Add($"payload:{t.Payload}");
                    var capturedData = t.Payload;
                    var capturedPayId = payId;
                    _emissions.Add(sb => EmitStringEventPayload(sb, capturedPayId, capturedData));
                }
            }
        }
    }

    private void EmitModifierGeneratorDfs(ModifierGeneratorDef mg)
    {
        string? bindingId = null;
        if (mg.Bindings != null && mg.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("mg", mg.Name)}:binding"];

        var id = _nodeIds[NodeKey("mg", mg.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitModifierGenerator(sb, mg, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, mg.Bindings!));
        }

        EmitNodeDfs(mg.Modifier);
        EmitNodeDfs(mg.Generator);
    }

    private void EmitIsActiveModifierDfs(BSIsActiveModifierDef iam)
    {
        string? bindingId = null;
        if (iam.Bindings != null && iam.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("iam", iam.Name)}:binding"];

        var id = _nodeIds[NodeKey("iam", iam.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitBSIsActiveModifier(sb, iam, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, iam.Bindings!));
        }
    }

    private void EmitStateTaggingGeneratorDfs(BSiStateTaggingGeneratorDef stg)
    {
        string? bindingId = null;
        if (stg.Bindings != null && stg.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("stg", stg.Name)}:binding"];

        var id = _nodeIds[NodeKey("stg", stg.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitBSiStateTaggingGenerator(sb, stg, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, stg.Bindings!));
        }

        EmitNodeDfs(stg.PDefaultGenerator);
    }

    private void EmitBehaviorReferenceDfs(BehaviorReferenceGeneratorDef bref)
    {
        string? bindingId = null;
        if (bref.Bindings != null && bref.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("bref", bref.Name)}:binding"];

        var id = _nodeIds[NodeKey("bref", bref.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitBehaviorReferenceGenerator(sb, bref, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, bref.Bindings!));
        }
    }

    private void EmitModifierListDfs(ModifierListDef ml)
    {
        string? bindingId = null;
        if (ml.Bindings != null && ml.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("ml", ml.Name)}:binding"];

        var id = _nodeIds[NodeKey("ml", ml.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitModifierList(sb, ml, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, ml.Bindings!));
        }

        foreach (var modName in ml.Modifiers)
            EmitNodeDfs(modName);
    }

    private void EmitCyclicBlendGeneratorDfs(BSCyclicBlendTransitionGeneratorDef cb)
    {
        string? bindingId = null;
        if (cb.Bindings != null && cb.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("cb", cb.Name)}:binding"];

        var id = _nodeIds[NodeKey("cb", cb.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitBSCyclicBlendTransitionGenerator(sb, cb, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, cb.Bindings!));
        }

        EmitNodeDfs(cb.PBlenderGenerator);
    }

    private void EmitEventDrivenModifierDfs(EventDrivenModifierDef edm)
    {
        string? bindingId = null;
        if (edm.Bindings != null && edm.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("edm", edm.Name)}:binding"];

        var id = _nodeIds[NodeKey("edm", edm.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitEventDrivenModifier(sb, edm, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, edm.Bindings!));
        }

        EmitNodeDfs(edm.Modifier);
    }

    private void EmitEventEveryNModifierDfs(BSEventEveryNEventsModifierDef een)
    {
        string? bindingId = null;
        if (een.Bindings != null && een.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("een", een.Name)}:binding"];

        var id = _nodeIds[NodeKey("een", een.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitBSEventEveryNEventsModifier(sb, een, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, een.Bindings!));
        }
    }

    private void EmitGenericModifierDfs(GenericModifierDef gm)
    {
        string? bindingId = null;
        if (gm.Bindings != null && gm.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("gm", gm.Name)}:binding"];

        var id = _nodeIds[NodeKey("gm", gm.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitGenericModifier(sb, gm, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, gm.Bindings!));
        }

        foreach (var p in gm.ExtraParams)
        {
            if (p.Kind == GenericParamKind.Scalar && p.ScalarValue != null)
            {
                if (IsDataArrayParam(p.Name) && _data.ExpressionDataArrays.ContainsKey(p.ScalarValue))
                    EmitExpressionDataArrayDfs(p.ScalarValue);
                else if (IsDataArrayParam(p.Name) && _data.EventRangeDataArrays.ContainsKey(p.ScalarValue))
                    EmitEventRangeDataArrayDfs(p.ScalarValue);
                else if (IsDataArrayParam(p.Name) && _data.BoneIndexArrays.ContainsKey(p.ScalarValue))
                    EmitBoneIndexArrayDfs(p.ScalarValue);
                else if (_data.GetNodeClass(p.ScalarValue) != null)
                    EmitNodeDfs(p.ScalarValue);
                else if (_data.BoneIndexArrays.ContainsKey(p.ScalarValue))
                    EmitBoneIndexArrayDfs(p.ScalarValue);
                else if (_data.EventRangeDataArrays.ContainsKey(p.ScalarValue))
                    EmitEventRangeDataArrayDfs(p.ScalarValue);
            }
            else if (p.Kind == GenericParamKind.RefList && p.RefListValue != null)
            {
                foreach (var r in p.RefListValue)
                    if (_data.GetNodeClass(r) != null)
                        EmitNodeDfs(r);
            }
        }
    }

    private void EmitFootIkControlsModifierDfs(FootIkControlsModifierDef ficm)
    {
        string? bindingId = null;
        if (ficm.Bindings != null && ficm.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("ficm", ficm.Name)}:binding"];

        var id = _nodeIds[NodeKey("ficm", ficm.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitFootIkControlsModifier(sb, ficm, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, ficm.Bindings!));
        }
    }

    private void EmitEvaluateExpressionModifierDfs(EvaluateExpressionModifierDef eem)
    {
        string? bindingId = null;
        if (eem.Bindings != null && eem.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("eem", eem.Name)}:binding"];

        var id = _nodeIds[NodeKey("eem", eem.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitEvaluateExpressionModifier(sb, eem, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, eem.Bindings!));
        }

        EmitExpressionDataArrayDfs(eem.Expressions);
    }

    private void EmitExpressionDataArrayDfs(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return;
        if (!_auxIds.TryGetValue($"eda:{name}", out _)) return;
        if (_dfsVisited.Contains($"eda:{name}")) return;
        _dfsVisited.Add($"eda:{name}");

        if (_data.ExpressionDataArrays.TryGetValue(name, out var eda))
        {
            var edaId = _auxIds[$"eda:{name}"];
            _emissions.Add(sb => EmitExpressionDataArray(sb, eda, edaId));
        }
    }

    private void EmitEventRangeDataArrayDfs(string name)
    {
        if (!_auxIds.TryGetValue($"erda:{name}", out _)) return;
        if (_dfsVisited.Contains($"erda:{name}")) return;
        _dfsVisited.Add($"erda:{name}");

        if (_data.EventRangeDataArrays.TryGetValue(name, out var erda))
        {
            var erdaId = _auxIds[$"erda:{name}"];
            _emissions.Add(sb => EmitEventRangeDataArray(sb, erda, erdaId));
        }
    }

    private void EmitBoneIndexArrayDfs(string name)
    {
        if (!_auxIds.TryGetValue($"bia:{name}", out _)) return;
        if (_dfsVisited.Contains($"bia:{name}")) return;
        _dfsVisited.Add($"bia:{name}");

        if (_data.BoneIndexArrays.TryGetValue(name, out var bia))
        {
            var biaId = _auxIds[$"bia:{name}"];
            _emissions.Add(sb => EmitBoneIndexArray(sb, bia, biaId));
        }
    }

    private void EmitBoneSwitchGeneratorDfs(BSBoneSwitchGeneratorDef bsg)
    {
        string? bindingId = null;
        if (bsg.Bindings != null && bsg.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("bsg", bsg.Name)}:binding"];

        var childIds = new List<string>();
        if (bsg.Children != null)
        {
            for (int i = 0; i < bsg.Children.Count; i++)
                childIds.Add(_auxIds[$"{NodeKey("bsg", bsg.Name)}:child:{i}"]);
        }

        // Emit self first.
        var id = _nodeIds[NodeKey("bsg", bsg.Name)];
        var capturedBindingId = bindingId;
        var capturedChildIds = childIds;
        _emissions.Add(sb => EmitBoneSwitchGenerator(sb, bsg, id, capturedBindingId, capturedChildIds));

        // Then binding.
        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, bsg.Bindings!));
        }

        // Then children: bone data → child binding → child generator subtree → bone weight.
        if (bsg.Children != null)
        {
            for (int i = 0; i < bsg.Children.Count; i++)
            {
                var child = bsg.Children[i];
                var childKey = $"{NodeKey("bsg", bsg.Name)}:child:{i}";
                var childId = _auxIds[childKey];

                string? childBindingId = null;
                if (child.Bindings != null && child.Bindings.Count > 0)
                    childBindingId = _auxIds[$"{childKey}:binding"];

                string? boneWeightId = null;
                if (_auxIds.TryGetValue($"{childKey}:boneWeight", out var bwId))
                    boneWeightId = bwId;

                var capturedChildId = childId;
                var capturedGenId = ResolveGeneratorId(child.PGenerator);
                var capturedChildBindingId = childBindingId;
                var capturedBoneWeightId = boneWeightId;
                _emissions.Add(sb => EmitBoneSwitchGeneratorBoneData(sb, capturedChildId, capturedGenId, capturedBoneWeightId, capturedChildBindingId));

                if (childBindingId != null)
                {
                    var capturedChildBindId = childBindingId;
                    _emissions.Add(sb => EmitVariableBindingSet(sb, capturedChildBindId, child.Bindings!));
                }

                EmitNodeDfs(child.PGenerator);

                if (boneWeightId != null)
                {
                    var capturedBwId = boneWeightId;
                    var capturedChild = child;
                    _emissions.Add(sb => EmitBoneSwitchBoneWeight(sb, capturedBwId, capturedChild));
                }
            }
        }

        EmitNodeDfs(bsg.PDefaultGenerator);
    }

    private void EmitSynchronizedClipDfs(BSSynchronizedClipGeneratorDef sc)
    {
        string? bindingId = null;
        if (sc.Bindings != null && sc.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("sc", sc.Name)}:binding"];

        var id = _nodeIds[NodeKey("sc", sc.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitSynchronizedClipGenerator(sb, sc, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, sc.Bindings!));
        }

        EmitNodeDfs(sc.PClipGenerator);
    }

    private void EmitOffsetAnimGeneratorDfs(BSOffsetAnimationGeneratorDef oag)
    {
        string? bindingId = null;
        if (oag.Bindings != null && oag.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("oag", oag.Name)}:binding"];

        var id = _nodeIds[NodeKey("oag", oag.Name)];
        var capturedBindingId = bindingId;
        _emissions.Add(sb => EmitOffsetAnimGenerator(sb, oag, id, capturedBindingId));

        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, oag.Bindings!));
        }

        EmitNodeDfs(oag.PDefaultGenerator);
        EmitNodeDfs(oag.POffsetClipGenerator);
    }

    private void EmitPoseMatchingGeneratorDfs(PoseMatchingGeneratorDef pmg)
    {
        string? bindingId = null;
        if (pmg.Bindings != null && pmg.Bindings.Count > 0)
            bindingId = _auxIds[$"{NodeKey("pmg", pmg.Name)}:binding"];

        var childIds = new List<string>();
        if (pmg.Children != null)
        {
            for (int i = 0; i < pmg.Children.Count; i++)
            {
                var child = pmg.Children[i];
                if (string.IsNullOrEmpty(child.Generator) || child.Generator == "null")
                    childIds.Add("null");
                else
                    childIds.Add(_auxIds[$"{NodeKey("pmg", pmg.Name)}:child:{i}"]);
            }
        }

        // Emit self first.
        var id = _nodeIds[NodeKey("pmg", pmg.Name)];
        var capturedBindingId2 = bindingId;
        var capturedChildIds = childIds;
        _emissions.Add(sb => EmitPoseMatchingGenerator(sb, pmg, id, capturedBindingId2, capturedChildIds));

        // Then binding.
        if (bindingId != null)
        {
            var capturedId = bindingId;
            _emissions.Add(sb => EmitVariableBindingSet(sb, capturedId, pmg.Bindings!));
        }

        // Then children.
        if (pmg.Children != null)
        {
            for (int i = 0; i < pmg.Children.Count; i++)
            {
                var child = pmg.Children[i];
                if (string.IsNullOrEmpty(child.Generator) || child.Generator == "null")
                    continue;

                string? bwId = null;
                if (child.BoneWeights != null && child.BoneWeights.HasData)
                    bwId = _auxIds[$"{NodeKey("pmg", pmg.Name)}:bw:{i}"];

                var childId = _auxIds[$"{NodeKey("pmg", pmg.Name)}:child:{i}"];
                var capturedChild = child;
                var capturedChildId = childId;
                var capturedBwId = bwId;
                _emissions.Add(sb => EmitBlenderGeneratorChild(sb, capturedChild, capturedChildId, capturedBwId));

                EmitNodeDfs(child.Generator);

                if (bwId != null)
                {
                    var capturedBwId2 = bwId;
                    var capturedBw = child.BoneWeights!;
                    _emissions.Add(sb => EmitBoneWeightArray(sb, capturedBwId2, capturedBw));
                }
            }
        }
    }

    // ── Emission methods ──

    private void EmitClipTriggerArray(StringBuilder sb, string id, List<ClipTriggerDef> triggers)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbClipTriggerArray\" signature=\"{SigClipTriggerArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"triggers\" numelements=\"{triggers.Count}\">");

        foreach (var t in triggers)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"localTime\">{t.LocalTime}</hkparam>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"event\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(t.Event, t.EventId)}</hkparam>");
            var payloadRef = ResolvePayloadId(t.Payload);
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"payload\">{payloadRef}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"relativeToEndOfClip\">{Bool(t.RelativeToEndOfClip)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"acyclic\">{Bool(t.Acyclic)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"isAnnotation\">{Bool(t.IsAnnotation)}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }

        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitClipGenerator(StringBuilder sb, ClipGeneratorDef clip, string id, string? trigId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbClipGenerator\" signature=\"{SigClipGenerator}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"variableBindingSet\">null</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{clip.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{clip.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"animationName\">{XmlEsc(clip.AnimationName)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"triggers\">{trigId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"cropStartAmountLocalTime\">{clip.CropStartAmountLocalTime}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"cropEndAmountLocalTime\">{clip.CropEndAmountLocalTime}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"startTime\">{clip.StartTime}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"playbackSpeed\">{clip.PlaybackSpeed}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enforcedDuration\">{clip.EnforcedDuration}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userControlledTimeFraction\">{clip.UserControlledTimeFraction}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"animationBindingIndex\">{clip.AnimationBindingIndex}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"mode\">{clip.Mode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"flags\">{clip.Flags}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitStringEventPayload(StringBuilder sb, string id, string data)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbStringEventPayload\" signature=\"{SigStringEventPayload}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"data\">{data}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitModifierGenerator(StringBuilder sb, ModifierGeneratorDef mg, string id, string? bindingId)
    {
        var modId = ResolveModifierId(mg.Modifier);
        var genId = ResolveGeneratorId(mg.Generator);

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbModifierGenerator\" signature=\"{SigModifierGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{mg.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{mg.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"modifier\">{modId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"generator\">{genId}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBSIsActiveModifier(StringBuilder sb, BSIsActiveModifierDef iam, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSIsActiveModifier\" signature=\"{SigBSIsActiveModifier}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{iam.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{iam.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(iam.Enable)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bIsActive0\">{Bool(iam.BIsActive0)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bInvertActive0\">{Bool(iam.BInvertActive0)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bIsActive1\">{Bool(iam.BIsActive1)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bInvertActive1\">{Bool(iam.BInvertActive1)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bIsActive2\">{Bool(iam.BIsActive2)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bInvertActive2\">{Bool(iam.BInvertActive2)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bIsActive3\">{Bool(iam.BIsActive3)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bInvertActive3\">{Bool(iam.BInvertActive3)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bIsActive4\">{Bool(iam.BIsActive4)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bInvertActive4\">{Bool(iam.BInvertActive4)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBSiStateTaggingGenerator(StringBuilder sb, BSiStateTaggingGeneratorDef stg, string id, string? bindingId)
    {
        var genId = ResolveGeneratorId(stg.PDefaultGenerator);

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSiStateTaggingGenerator\" signature=\"{SigBSiStateTaggingGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{stg.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{stg.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pDefaultGenerator\">{genId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"iStateToSetAs\">{stg.IStateToSetAs}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"iPriority\">{stg.IPriority}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBehaviorReferenceGenerator(StringBuilder sb, BehaviorReferenceGeneratorDef bref, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBehaviorReferenceGenerator\" signature=\"{SigBehaviorReferenceGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{bref.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{bref.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"behaviorName\">{bref.BehaviorName}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitModifierList(StringBuilder sb, ModifierListDef ml, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbModifierList\" signature=\"{SigModifierList}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{ml.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{ml.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(ml.Enable)}</hkparam>");

        var modRefs = ml.Modifiers.Select(m => ResolveModifierId(m));
        if (ml.Modifiers.Count == 0)
            sb.AppendLine("\t\t\t<hkparam name=\"modifiers\" numelements=\"0\"></hkparam>");
        else
            sb.AppendLine($"\t\t\t<hkparam name=\"modifiers\" numelements=\"{ml.Modifiers.Count}\">{string.Join(" ", modRefs)}</hkparam>");

        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBSCyclicBlendTransitionGenerator(StringBuilder sb, BSCyclicBlendTransitionGeneratorDef cb, string id, string? bindingId)
    {
        var blenderGenId = ResolveGeneratorId(cb.PBlenderGenerator);

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSCyclicBlendTransitionGenerator\" signature=\"{SigBSCyclicBlendTransitionGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{cb.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{cb.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pBlenderGenerator\">{blenderGenId}</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"EventToFreezeBlendValue\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(cb.EventToFreezeBlendValue.Event, cb.EventToFreezeBlendValue.Id)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"payload\">{ResolvePayloadId(cb.EventToFreezeBlendValue.Payload)}</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"EventToCrossBlend\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(cb.EventToCrossBlend.Event, cb.EventToCrossBlend.Id)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"payload\">{ResolvePayloadId(cb.EventToCrossBlend.Payload)}</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fBlendParameter\">{cb.FBlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fTransitionDuration\">{cb.FTransitionDuration}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"eBlendCurve\">{cb.EBlendCurve}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitExpressionCondition(StringBuilder sb, string id, string expression)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbExpressionCondition\" signature=\"{SigExpressionCondition}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"expression\">{XmlEsc(expression)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitStringCondition(StringBuilder sb, string id, string conditionString)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbStringCondition\" signature=\"{SigStringCondition}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"conditionString\">{XmlEsc(conditionString)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitEventDrivenModifier(StringBuilder sb, EventDrivenModifierDef edm, string id, string? bindingId)
    {
        var modId = ResolveModifierId(edm.Modifier);

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbEventDrivenModifier\" signature=\"{SigEventDrivenModifier}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{edm.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{edm.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(edm.Enable)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"modifier\">{modId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"activateEventId\">{ResolveEventId(edm.ActivateEvent, edm.ActivateEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"deactivateEventId\">{ResolveEventId(edm.DeactivateEvent, edm.DeactivateEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"activeByDefault\">{Bool(edm.ActiveByDefault)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBSEventEveryNEventsModifier(StringBuilder sb, BSEventEveryNEventsModifierDef een, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSEventEveryNEventsModifier\" signature=\"{SigBSEventEveryNEventsModifier}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{een.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{een.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(een.Enable)}</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"eventToCheckFor\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(een.EventToCheckFor.Event, een.EventToCheckFor.Id)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"payload\">{ResolvePayloadId(een.EventToCheckFor.Payload)}</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"eventToSend\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(een.EventToSend.Event, een.EventToSend.Id)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"payload\">{ResolvePayloadId(een.EventToSend.Payload)}</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"numberOfEventsBeforeSend\">{een.NumberOfEventsBeforeSend}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"minimumNumberOfEventsBeforeSend\">{een.MinimumNumberOfEventsBeforeSend}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"randomizeNumberOfEvents\">{Bool(een.RandomizeNumberOfEvents)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitGenericModifier(StringBuilder sb, GenericModifierDef gm, string id, string? bindingId)
    {
        if (!GenericModifierSignatures.TryGetValue(gm.Class, out var sig))
        {
            Console.Error.WriteLine($"  WARNING: Unknown generic modifier class '{gm.Class}' — skipping");
            return;
        }

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"{gm.Class}\" signature=\"{sig}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{gm.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{gm.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(gm.Enable)}</hkparam>");

        foreach (var p in gm.ExtraParams)
        {
            switch (p.Kind)
            {
                case GenericParamKind.Scalar:
                    // Use data-array-aware resolution for known data array params.
                    var resolved = IsDataArrayParam(p.Name)
                        ? ResolveDataArrayRef(p.ScalarValue)
                        : ResolveGenericRef(p.ScalarValue);
                    sb.AppendLine($"\t\t\t<hkparam name=\"{p.Name}\">{resolved}</hkparam>");
                    break;

                case GenericParamKind.InlineEvent:
                    sb.AppendLine($"\t\t\t<hkparam name=\"{p.Name}\">");
                    sb.AppendLine("\t\t\t\t<hkobject>");
                    sb.AppendLine($"\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(p.EventValue?.Event, p.EventValue?.Id ?? -1)}</hkparam>");
                    sb.AppendLine($"\t\t\t\t\t<hkparam name=\"payload\">{ResolvePayloadId(p.EventValue?.Payload)}</hkparam>");
                    sb.AppendLine("\t\t\t\t</hkobject>");
                    sb.AppendLine("\t\t\t</hkparam>");
                    break;

                case GenericParamKind.RefList:
                    if (p.RefListValue != null)
                    {
                        var resolvedRefs = p.RefListValue.Select(r => ResolveGenericRef(r)).ToList();
                        sb.AppendLine($"\t\t\t<hkparam name=\"{p.Name}\" numelements=\"{resolvedRefs.Count}\">{string.Join(' ', resolvedRefs)}</hkparam>");
                    }
                    break;

                case GenericParamKind.InlineObjectList:
                    if (p.InlineObjectListValue != null)
                    {
                        sb.AppendLine($"\t\t\t<hkparam name=\"{p.Name}\" numelements=\"{p.InlineObjectListValue.Count}\">");
                        foreach (var entry in p.InlineObjectListValue)
                        {
                            sb.AppendLine("\t\t\t\t<hkobject>");
                            foreach (var field in entry.Fields)
                                sb.AppendLine($"\t\t\t\t\t<hkparam name=\"{field.Key}\">{field.Value}</hkparam>");
                            sb.AppendLine("\t\t\t\t</hkobject>");
                        }
                        sb.AppendLine("\t\t\t</hkparam>");
                    }
                    break;
            }
        }

        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private static string Ff(float v)
    {
        if (v == 0f && float.IsNegative(v))
            return "-0.000000";
        return v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void EmitFootIkControlsModifier(StringBuilder sb, FootIkControlsModifierDef ficm, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbFootIkControlsModifier\" signature=\"{SigFootIkControlsModifier}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{ficm.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{ficm.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(ficm.Enable)}</hkparam>");

        // controlData → gains (nested inline structs)
        var g = ficm.ControlData.Gains;
        sb.AppendLine("\t\t\t<hkparam name=\"controlData\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"gains\">");
        sb.AppendLine("\t\t\t\t\t\t<hkobject>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"onOffGain\">{Ff(g.OnOffGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"groundAscendingGain\">{Ff(g.GroundAscendingGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"groundDescendingGain\">{Ff(g.GroundDescendingGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"footPlantedGain\">{Ff(g.FootPlantedGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"footRaisedGain\">{Ff(g.FootRaisedGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"footUnlockGain\">{Ff(g.FootUnlockGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"worldFromModelFeedbackGain\">{Ff(g.WorldFromModelFeedbackGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"errorUpDownBias\">{Ff(g.ErrorUpDownBias)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"alignWorldFromModelGain\">{Ff(g.AlignWorldFromModelGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"hipOrientationGain\">{Ff(g.HipOrientationGain)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"maxKneeAngleDifference\">{Ff(g.MaxKneeAngleDifference)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"ankleOrientationGain\">{Ff(g.AnkleOrientationGain)}</hkparam>");
        sb.AppendLine("\t\t\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t\t\t</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");

        // legs array
        var legs = ficm.Legs ?? [];
        if (legs.Count == 0)
        {
            sb.AppendLine("\t\t\t<hkparam name=\"legs\" numelements=\"0\"></hkparam>");
        }
        else
        {
            sb.AppendLine($"\t\t\t<hkparam name=\"legs\" numelements=\"{legs.Count}\">");
            foreach (var leg in legs)
            {
                sb.AppendLine("\t\t\t\t<hkobject>");
                sb.AppendLine($"\t\t\t\t\t<hkparam name=\"groundPosition\">{leg.GroundPosition}</hkparam>");
                sb.AppendLine("\t\t\t\t\t<hkparam name=\"ungroundedEvent\">");
                sb.AppendLine("\t\t\t\t\t\t<hkobject>");
                var ue = leg.UngroundedEvent;
                int ueId = (ue != null) ? ResolveEventId(ue.Event, ue.Id) : -1;
                string uePayload = (ue?.Payload != null) ? ResolvePayloadId(ue.Payload) : "null";
                sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"id\">{ueId}</hkparam>");
                sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"payload\">{uePayload}</hkparam>");
                sb.AppendLine("\t\t\t\t\t\t</hkobject>");
                sb.AppendLine("\t\t\t\t\t</hkparam>");
                sb.AppendLine($"\t\t\t\t\t<hkparam name=\"verticalError\">{Ff(leg.VerticalError)}</hkparam>");
                sb.AppendLine($"\t\t\t\t\t<hkparam name=\"hitSomething\">{Bool(leg.HitSomething)}</hkparam>");
                sb.AppendLine($"\t\t\t\t\t<hkparam name=\"isPlantedMS\">{Bool(leg.IsPlantedMS)}</hkparam>");
                sb.AppendLine("\t\t\t\t</hkobject>");
            }
            sb.AppendLine("\t\t\t</hkparam>");
        }

        sb.AppendLine($"\t\t\t<hkparam name=\"errorOutTranslation\">{ficm.ErrorOutTranslation}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"alignWithGroundRotation\">{ficm.AlignWithGroundRotation}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    /// <summary>Resolve a generic param value that may be a node reference, data array reference, or scalar.</summary>
    private string ResolveGenericRef(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "null") return "null";
        // Known node? Try all prefixed keys.
        foreach (var prefix in _nodePrefixes)
            if (_nodeIds.TryGetValue($"{prefix}:{value}", out var nodeId)) return nodeId;
        // Data arrays (only reached if not a known node).
        if (_auxIds.TryGetValue($"eda:{value}", out var edaId)) return edaId;
        if (_auxIds.TryGetValue($"erda:{value}", out var erdaId)) return erdaId;
        if (_auxIds.TryGetValue($"bia:{value}", out var biaId)) return biaId;
        // Not a reference — return as-is (scalar value).
        return value;
    }

    /// <summary>Resolve a generic param that is known to reference a data array (priority over nodes).</summary>
    private string ResolveDataArrayRef(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "null") return "null";
        if (_auxIds.TryGetValue($"eda:{value}", out var edaId)) return edaId;
        if (_auxIds.TryGetValue($"erda:{value}", out var erdaId)) return erdaId;
        if (_auxIds.TryGetValue($"bia:{value}", out var biaId)) return biaId;
        foreach (var prefix in _nodePrefixes)
            if (_nodeIds.TryGetValue($"{prefix}:{value}", out var nodeId)) return nodeId;
        return value;
    }

    /// <summary>Param names that reference data arrays instead of nodes.</summary>
    private static bool IsDataArrayParam(string paramName) =>
        paramName is "eventRanges" or "expressions" or "ranges" or "keyframedBonesList"
            or "pBoneWeights" or "boneIndices";

    private void EmitEvaluateExpressionModifier(StringBuilder sb, EvaluateExpressionModifierDef eem, string id, string? bindingId)
    {
        var expId = _auxIds.GetValueOrDefault($"eda:{eem.Expressions}", "null");

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbEvaluateExpressionModifier\" signature=\"{SigEvaluateExpressionModifier}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{eem.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{eem.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(eem.Enable)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"expressions\">{expId}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitExpressionDataArray(StringBuilder sb, ExpressionDataArrayDef eda, string id)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbExpressionDataArray\" signature=\"{SigExpressionDataArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"expressionsData\" numelements=\"{eda.ExpressionsData.Count}\">");
        foreach (var expr in eda.ExpressionsData)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"expression\">{XmlEsc(expr.Expression)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"assignmentVariableIndex\">{ResolveVariableIndex(expr.AssignmentVariable, expr.AssignmentVariableIndex)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"assignmentEventIndex\">{ResolveEventId(expr.AssignmentEvent, expr.AssignmentEventIndex)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"eventMode\">{expr.EventMode}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitEventRangeDataArray(StringBuilder sb, EventRangeDataArrayDef erda, string id)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbEventRangeDataArray\" signature=\"{SigEventRangeDataArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"eventData\" numelements=\"{erda.EventData.Count}\">");
        foreach (var range in erda.EventData)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"upperBound\">{range.UpperBound}</hkparam>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"event\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(range.Event, range.EventId)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"payload\">{ResolvePayloadId(range.Payload)}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"eventMode\">{range.EventMode}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBoneIndexArray(StringBuilder sb, BoneIndexArrayDef bia, string id)
    {
        // BoneIndices can be a List<object> of bone names or a space-separated string of indices.
        var indices = new List<int>();
        if (bia.BoneIndices is List<object> boneList)
        {
            foreach (var item in boneList)
            {
                var boneName = item?.ToString() ?? "";
                indices.Add(ResolveBoneIndex(boneName));
            }
        }
        else if (bia.BoneIndices is string indexStr)
        {
            foreach (var part in indexStr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part, out int idx))
                    indices.Add(idx);
        }

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBoneIndexArray\" signature=\"{SigBoneIndexArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"boneIndices\" numelements=\"{indices.Count}\">");
        sb.AppendLine($"\t\t\t\t{string.Join(' ', indices)}");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBoneSwitchGenerator(StringBuilder sb, BSBoneSwitchGeneratorDef bsg, string id, string? bindingId, List<string> childIds)
    {
        var defaultGenId = ResolveGeneratorId(bsg.PDefaultGenerator);
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSBoneSwitchGenerator\" signature=\"{SigBSBoneSwitchGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{bsg.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{bsg.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pDefaultGenerator\">{defaultGenId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"ChildrenA\" numelements=\"{childIds.Count}\">{string.Join(' ', childIds)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBoneSwitchGeneratorBoneData(StringBuilder sb, string id, string generatorId, string? boneWeightId, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSBoneSwitchGeneratorBoneData\" signature=\"{SigBSBoneSwitchGeneratorBoneData}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pGenerator\">{generatorId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"spBoneWeight\">{boneWeightId ?? "null"}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBoneSwitchBoneWeight(StringBuilder sb, string id, BoneSwitchChildDef child)
    {
        var bw = child.BoneWeights!;
        int count;
        string values;
        if (bw.IsNamed)
        {
            if (_data.BoneNames == null)
                throw new InvalidOperationException(
                    "Named bone weights used but no skeleton.yaml found. " +
                    "Place skeleton.yaml in a 'character assets' sibling directory.");
            (count, values) = bw.Resolve(_data.BoneNames);
        }
        else
        {
            count = bw.Count;
            values = bw.Values;
        }

        var floats = string.IsNullOrWhiteSpace(values)
            ? Array.Empty<string>()
            : values.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBoneWeightArray\" signature=\"{SigBoneWeightArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"boneWeights\" numelements=\"{count}\">");
        for (int i = 0; i < floats.Length; i += 16)
        {
            var row = floats.Skip(i).Take(16);
            sb.AppendLine($"\t\t\t\t{string.Join(' ', row)}");
        }
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitSynchronizedClipGenerator(StringBuilder sb, BSSynchronizedClipGeneratorDef sc, string id, string? bindingId)
    {
        var clipId = ResolveGeneratorId(sc.PClipGenerator);
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSSynchronizedClipGenerator\" signature=\"{SigBSSynchronizedClipGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{sc.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{sc.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pClipGenerator\">{clipId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"SyncAnimPrefix\">{sc.SyncAnimPrefix}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bSyncClipIgnoreMarkPlacement\">{Bool(sc.BSyncClipIgnoreMarkPlacement)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fGetToMarkTime\">{sc.FGetToMarkTime}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fMarkErrorThreshold\">{sc.FMarkErrorThreshold}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bLeadCharacter\">{Bool(sc.BLeadCharacter)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bReorientSupportChar\">{Bool(sc.BReorientSupportChar)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"bApplyMotionFromRoot\">{Bool(sc.BApplyMotionFromRoot)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"sAnimationBindingIndex\">{sc.SAnimationBindingIndex}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitOffsetAnimGenerator(StringBuilder sb, BSOffsetAnimationGeneratorDef oag, string id, string? bindingId)
    {
        var defGenId = ResolveGeneratorId(oag.PDefaultGenerator);
        var offClipId = ResolveGeneratorId(oag.POffsetClipGenerator);
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"BSOffsetAnimationGenerator\" signature=\"{SigBSOffsetAnimationGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{oag.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{oag.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pDefaultGenerator\">{defGenId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pOffsetClipGenerator\">{offClipId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fOffsetVariable\">{oag.FOffsetVariable}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fOffsetRangeStart\">{oag.FOffsetRangeStart}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"fOffsetRangeEnd\">{oag.FOffsetRangeEnd}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitPoseMatchingGenerator(StringBuilder sb, PoseMatchingGeneratorDef pmg, string id, string? bindingId, List<string> childIds)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbPoseMatchingGenerator\" signature=\"{SigPoseMatchingGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{pmg.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{pmg.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"referencePoseWeightThreshold\">{pmg.ReferencePoseWeightThreshold}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"blendParameter\">{pmg.BlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"minCyclicBlendParameter\">{pmg.MinCyclicBlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"maxCyclicBlendParameter\">{pmg.MaxCyclicBlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"indexOfSyncMasterChild\">{pmg.IndexOfSyncMasterChild}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"flags\">{pmg.Flags}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"subtractLastChild\">{Bool(pmg.SubtractLastChild)}</hkparam>");
        var childRefs = childIds.Count > 0 ? string.Join(" ", childIds) : "";
        sb.AppendLine($"\t\t\t<hkparam name=\"children\" numelements=\"{childIds.Count}\">{childRefs}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"worldFromModelRotation\">{pmg.WorldFromModelRotation}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"blendSpeed\">{pmg.BlendSpeed}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"minSpeedToSwitch\">{pmg.MinSpeedToSwitch}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"minSwitchTimeNoError\">{pmg.MinSwitchTimeNoError}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"minSwitchTimeFullError\">{pmg.MinSwitchTimeFullError}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"startPlayingEventId\">{ResolveEventId(pmg.StartPlayingEvent, pmg.StartPlayingEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"startMatchingEventId\">{ResolveEventId(pmg.StartMatchingEvent, pmg.StartMatchingEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"rootBoneIndex\">{pmg.RootBoneIndex}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"otherBoneIndex\">{pmg.OtherBoneIndex}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"anotherBoneIndex\">{pmg.AnotherBoneIndex}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"pelvisIndex\">{pmg.PelvisIndex}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"mode\">{pmg.Mode}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitVariableBindingSet(StringBuilder sb, string id, List<BindingDef> bindings)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbVariableBindingSet\" signature=\"{SigVariableBindingSet}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"bindings\" numelements=\"{bindings.Count}\">");

        foreach (var b in bindings)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"memberPath\">{b.MemberPath}</hkparam>");
            var varIdx = b.BindingType == "BINDING_TYPE_CHARACTER_PROPERTY"
                ? ResolveCharPropIndex(b.Variable, b.VariableIndex)
                : ResolveVariableIndex(b.Variable, b.VariableIndex);
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"variableIndex\">{varIdx}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"bitIndex\">{b.BitIndex}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"bindingType\">{b.BindingType}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }

        sb.AppendLine("\t\t\t</hkparam>");
        var enableIdx = bindings.FindIndex(b => b.MemberPath == "enable");
        sb.AppendLine($"\t\t\t<hkparam name=\"indexOfBindingToEnable\">{enableIdx}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBoneWeightArray(StringBuilder sb, string id, BoneWeightsDef bw)
    {
        // Resolve named bone weights if skeleton is available.
        int count = bw.Count;
        string values = bw.Values;
        if (bw.IsNamed)
        {
            if (_data.BoneNames == null)
                throw new InvalidOperationException(
                    "Named bone weights used but no skeleton.yaml found. " +
                    "Place skeleton.yaml in a 'character assets' sibling directory.");
            (count, values) = bw.Resolve(_data.BoneNames);
        }

        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBoneWeightArray\" signature=\"{SigBoneWeightArray}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"variableBindingSet\">null</hkparam>");

        if (count == 0 || string.IsNullOrWhiteSpace(values))
        {
            sb.AppendLine("\t\t\t<hkparam name=\"boneWeights\" numelements=\"0\"></hkparam>");
        }
        else
        {
            sb.AppendLine($"\t\t\t<hkparam name=\"boneWeights\" numelements=\"{count}\">");
            var floats = values.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < floats.Length; i += 16)
            {
                var row = floats.Skip(i).Take(16);
                sb.AppendLine($"\t\t\t\t{string.Join(' ', row)}");
            }
            sb.AppendLine("\t\t\t</hkparam>");
        }

        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBlenderGeneratorChild(StringBuilder sb, BlenderChildDef child, string id, string? bwId)
    {
        var genId = child.Generator == "null" ? "null" : ResolveGeneratorId(child.Generator);
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBlenderGeneratorChild\" signature=\"{SigBlenderGeneratorChild}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"variableBindingSet\">null</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"generator\">{genId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"boneWeights\">{bwId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"weight\">{child.Weight}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"worldFromModelWeight\">{child.WorldFromModelWeight}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBlenderGenerator(StringBuilder sb, BlenderGeneratorDef blend, string id, string? bindingId, List<string> childIds)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBlenderGenerator\" signature=\"{SigBlenderGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{blend.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{blend.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"referencePoseWeightThreshold\">{blend.ReferencePoseWeightThreshold}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"blendParameter\">{blend.BlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"minCyclicBlendParameter\">{blend.MinCyclicBlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"maxCyclicBlendParameter\">{blend.MaxCyclicBlendParameter}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"indexOfSyncMasterChild\">{blend.IndexOfSyncMasterChild}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"flags\">{blend.Flags}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"subtractLastChild\">{Bool(blend.SubtractLastChild)}</hkparam>");

        // Children as space-separated ID refs (nulls preserved for vanilla compat).
        sb.AppendLine($"\t\t\t<hkparam name=\"children\" numelements=\"{childIds.Count}\">");
        sb.AppendLine($"\t\t\t\t{string.Join(' ', childIds)}");
        sb.AppendLine("\t\t\t</hkparam>");

        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitManualSelector(StringBuilder sb, ManualSelectorDef sel, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbManualSelectorGenerator\" signature=\"{SigManualSelectorGenerator}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{sel.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{sel.Name}</hkparam>");

        // Generators as space-separated ID refs.
        var genIds = sel.Generators.Select(ResolveId).ToList();
        sb.AppendLine($"\t\t\t<hkparam name=\"generators\" numelements=\"{genIds.Count}\">");
        sb.AppendLine($"\t\t\t\t{string.Join(' ', genIds)}");
        sb.AppendLine("\t\t\t</hkparam>");

        sb.AppendLine($"\t\t\t<hkparam name=\"selectedGeneratorIndex\">{sel.SelectedGeneratorIndex}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"currentGeneratorIndex\">{sel.CurrentGeneratorIndex}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitEventPropertyArray(StringBuilder sb, string id, List<EventPropertyDef> events)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbStateMachineEventPropertyArray\" signature=\"{SigEventPropertyArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"events\" numelements=\"{events.Count}\">");

        foreach (var ev in events)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"id\">{ResolveEventId(ev.Event, ev.Id)}</hkparam>");
            var payloadRef = ResolvePayloadId(ev.Payload);
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"payload\">{payloadRef}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }

        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitState(StringBuilder sb, StateDef state, string id, string? enterEvId, string? exitEvId, string? transId)
    {
        var genId = ResolveGeneratorId(state.Generator);
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbStateMachineStateInfo\" signature=\"{SigStateInfo}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"variableBindingSet\">null</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"listeners\" numelements=\"0\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enterNotifyEvents\">{enterEvId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"exitNotifyEvents\">{exitEvId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"transitions\">{transId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"generator\">{genId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{state.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"stateId\">{state.StateId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"probability\">{state.Probability}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"enable\">{Bool(state.Enable)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitStateMachine(StringBuilder sb, StateMachineDef sm, string id, string? bindingId, string? wildcardTransId)
    {
        var stateIds = sm.States.Select(s => ResolveId(StateKey(s))).ToList();
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbStateMachine\" signature=\"{SigStateMachine}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{sm.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{sm.Name}</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"eventToSendWhenStateOrTransitionChanges\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"id\">-1</hkparam>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"payload\">null</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"startStateChooser\">null</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"startStateId\">{sm.StartStateId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"returnToPreviousStateEventId\">{ResolveEventId(sm.ReturnToPreviousStateEvent, sm.ReturnToPreviousStateEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"randomTransitionEventId\">{ResolveEventId(sm.RandomTransitionEvent, sm.RandomTransitionEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"transitionToNextHigherStateEventId\">{ResolveEventId(sm.TransitionToNextHigherStateEvent, sm.TransitionToNextHigherStateEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"transitionToNextLowerStateEventId\">{ResolveEventId(sm.TransitionToNextLowerStateEvent, sm.TransitionToNextLowerStateEventId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"syncVariableIndex\">{ResolveVariableIndex(sm.SyncVariable, sm.SyncVariableIndex)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"wrapAroundStateId\">{Bool(sm.WrapAroundStateId)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"maxSimultaneousTransitions\">{sm.MaxSimultaneousTransitions}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"startStateMode\">{sm.StartStateMode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"selfTransitionMode\">{sm.SelfTransitionMode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"states\" numelements=\"{stateIds.Count}\">");
        sb.AppendLine($"\t\t\t\t{string.Join(' ', stateIds)}");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"wildcardTransitions\">{wildcardTransId ?? "null"}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitTransitionEffect(StringBuilder sb, TransitionEffectDef effect, string id, string? bindingId)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBlendingTransitionEffect\" signature=\"{SigBlendingTransitionEffect}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableBindingSet\">{bindingId ?? "null"}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"userData\">{effect.UserData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{effect.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"selfTransitionMode\">{effect.SelfTransitionMode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"eventMode\">{effect.EventMode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"duration\">{effect.Duration}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"toGeneratorStartTimeFraction\">{effect.ToGeneratorStartTimeFraction}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"flags\">{effect.Flags}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"endMode\">{effect.EndMode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"blendCurve\">{effect.BlendCurve}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitTransitionInfoArray(StringBuilder sb, string id, List<TransitionInfoDef> transitions)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbStateMachineTransitionInfoArray\" signature=\"{SigTransitionInfoArray}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"transitions\" numelements=\"{transitions.Count}\">");

        foreach (var t in transitions)
        {
            var transEffectId = ResolveTransitionEffectId(t.Transition);
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"triggerInterval\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"enterEventId\">{ResolveEventId(t.TriggerInterval.EnterEvent, t.TriggerInterval.EnterEventId)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"exitEventId\">{ResolveEventId(t.TriggerInterval.ExitEvent, t.TriggerInterval.ExitEventId)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"enterTime\">{t.TriggerInterval.EnterTime}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"exitTime\">{t.TriggerInterval.ExitTime}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"initiateInterval\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"enterEventId\">{ResolveEventId(t.InitiateInterval.EnterEvent, t.InitiateInterval.EnterEventId)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"exitEventId\">{ResolveEventId(t.InitiateInterval.ExitEvent, t.InitiateInterval.ExitEventId)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"enterTime\">{t.InitiateInterval.EnterTime}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"exitTime\">{t.InitiateInterval.ExitTime}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"transition\">{transEffectId}</hkparam>");
            // Resolve condition — expression condition or string condition.
            var condRef = "null";
            if (!string.IsNullOrEmpty(t.Condition) && t.Condition != "null"
                && _auxIds.TryGetValue($"cond:{t.Condition}", out var cId))
                condRef = cId;
            else if (!string.IsNullOrEmpty(t.ConditionString) && t.ConditionString != "null"
                && _auxIds.TryGetValue($"strcond:{t.ConditionString}", out var scId))
                condRef = scId;
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"condition\">{condRef}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"eventId\">{ResolveEventId(t.Event, t.EventId)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"toStateId\">{t.ToStateId}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"fromNestedStateId\">{t.FromNestedStateId}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"toNestedStateId\">{t.ToNestedStateId}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"priority\">{t.Priority}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"flags\">{t.Flags}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }

        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private string _gdId = "";
    private string _vvsId = "";
    private string _sdId = "";

    private void AllocGraphDataIds()
    {
        _gdId = AllocId();
        _auxIds["graphdata"] = _gdId;
        _vvsId = AllocId();
        _sdId = AllocId();
    }

    private void EmitGraphData(StringBuilder sb)
    {
        var gd = _data.GraphData!;

        // ── 1. Emit BehaviorGraphData ──
        sb.AppendLine($"\t\t<hkobject name=\"{_gdId}\" class=\"hkbBehaviorGraphData\" signature=\"{SigBehaviorGraphData}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"attributeDefaults\" numelements=\"{gd.AttributeDefaultCount}\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableInfos\" numelements=\"{gd.Variables.Count}\">");
        foreach (var v in gd.Variables)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"role\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"role\">{v.Role}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"flags\">{v.RoleFlags}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"type\">{v.Type}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");
        var charPropCount = gd.CharacterPropertyNames.Count > 0
            ? gd.CharacterPropertyNames.Count
            : gd.CharacterPropertyInfoCount;
        sb.AppendLine($"\t\t\t<hkparam name=\"characterPropertyInfos\" numelements=\"{charPropCount}\">");
        for (int i = 0; i < charPropCount; i++)
        {
            var cpType = i < gd.CharacterPropertyNames.Count ? gd.CharacterPropertyNames[i].Type : "VARIABLE_TYPE_POINTER";
            var cpFlags = i < gd.CharacterPropertyNames.Count ? gd.CharacterPropertyNames[i].Flags : "0";
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"role\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine("\t\t\t\t\t\t\t<hkparam name=\"role\">ROLE_DEFAULT</hkparam>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"flags\">{cpFlags}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"type\">{cpType}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"eventInfos\" numelements=\"{gd.Events.Count}\">");
        foreach (var e in gd.Events)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"flags\">{e.Flags}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"wordMinVariableValues\" numelements=\"{gd.WordMinVariableValueCount}\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"wordMaxVariableValues\" numelements=\"{gd.WordMaxVariableValueCount}\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableInitialValues\">{_vvsId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"stringData\">{_sdId}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();

        // ── 2. Emit VariableValueSet ──
        sb.AppendLine($"\t\t<hkobject name=\"{_vvsId}\" class=\"hkbVariableValueSet\" signature=\"{SigVariableValueSet}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"wordVariableValues\" numelements=\"{gd.Variables.Count}\">");
        foreach (var v in gd.Variables)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"value\">{v.Value}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");

        var quadValues = new List<string>();
        if (gd.QuadVariableValues.Count > 0)
        {
            quadValues.AddRange(gd.QuadVariableValues);
        }
        else
        {
            foreach (var v in gd.Variables)
            {
                if (v.Type is "VARIABLE_TYPE_VECTOR4" or "VARIABLE_TYPE_QUATERNION")
                {
                    if (v.QuadValue != null)
                        quadValues.Add(v.QuadValue);
                    else if (v.Type == "VARIABLE_TYPE_QUATERNION")
                        quadValues.Add("(0.000000 0.000000 0.000000 1.000000)");
                    else
                        quadValues.Add("(0.000000 0.000000 0.000000 0.000000)");
                }
            }
        }
        if (quadValues.Count > 0)
        {
            sb.AppendLine($"\t\t\t<hkparam name=\"quadVariableValues\" numelements=\"{quadValues.Count}\">");
            foreach (var qv in quadValues)
                sb.AppendLine($"\t\t\t\t{qv}");
            sb.AppendLine("\t\t\t</hkparam>");
        }
        else
        {
            sb.AppendLine("\t\t\t<hkparam name=\"quadVariableValues\" numelements=\"0\"></hkparam>");
        }

        sb.AppendLine($"\t\t\t<hkparam name=\"variantVariableValues\" numelements=\"{gd.VariantVariableValueCount}\"></hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();

        // ── 3. Emit BehaviorGraphStringData ──
        sb.AppendLine($"\t\t<hkobject name=\"{_sdId}\" class=\"hkbBehaviorGraphStringData\" signature=\"{SigBehaviorGraphStringData}\">");
        sb.AppendLine($"\t\t\t<hkparam name=\"eventNames\" numelements=\"{gd.Events.Count}\">");
        foreach (var e in gd.Events)
            sb.AppendLine($"\t\t\t\t<hkcstring>{e.Name}</hkcstring>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"attributeNames\" numelements=\"0\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableNames\" numelements=\"{gd.Variables.Count}\">");
        foreach (var v in gd.Variables)
            sb.AppendLine($"\t\t\t\t<hkcstring>{v.Name}</hkcstring>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"characterPropertyNames\" numelements=\"{gd.CharacterPropertyNames.Count}\">");
        foreach (var cp in gd.CharacterPropertyNames)
            sb.AppendLine($"\t\t\t\t<hkcstring>{cp.Name}</hkcstring>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitBehaviorGraph(StringBuilder sb)
    {
        var rootId = ResolveGeneratorId(_data.Behavior.Behavior.RootGenerator);
        var dataId = _data.GraphData != null ? _auxIds.GetValueOrDefault("graphdata") ?? "null" : "null";
        sb.AppendLine($"\t\t<hkobject name=\"#0002\" class=\"hkbBehaviorGraph\" signature=\"{SigBehaviorGraph}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"variableBindingSet\">null</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"userData\">0</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{_data.Behavior.Behavior.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"variableMode\">{_data.Behavior.Behavior.VariableMode}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"rootGenerator\">{rootId}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"data\">{dataId}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitRootLevelContainer(StringBuilder sb)
    {
        sb.AppendLine($"\t\t<hkobject name=\"#0001\" class=\"hkRootLevelContainer\" signature=\"{SigRoot}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"namedVariants\" numelements=\"1\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"name\">hkbBehaviorGraph</hkparam>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"className\">hkbBehaviorGraph</hkparam>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"variant\">#0002</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private string ResolveId(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return "null";
        // Try all collection prefixes in priority order.
        foreach (var prefix in _nodePrefixes)
        {
            if (_nodeIds.TryGetValue($"{prefix}:{name}", out var id)) return id;
        }
        // Legacy: try bare name and state key.
        if (_nodeIds.TryGetValue(name, out var bareId)) return bareId;
        if (_nodeIds.TryGetValue(StateKey(name), out var stId)) return stId;
        Console.Error.WriteLine($"  WARNING: Cannot resolve ID for '{name}'");
        return "null";
    }

    /// <summary>Resolve a name with a preferred set of prefixes tried first.</summary>
    private string ResolveId(string? name, string[] preferredPrefixes)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return "null";
        foreach (var prefix in preferredPrefixes)
            if (_nodeIds.TryGetValue($"{prefix}:{name}", out var id)) return id;
        return ResolveId(name);
    }

    /// <summary>Collection prefixes tried by ResolveId, in priority order.</summary>
    private static readonly string[] _nodePrefixes =
        ["sm", "sel", "blend", "clip", "mg", "iam", "stg", "bref", "ml",
         "cb", "edm", "een", "gm", "ficm", "eem", "bsg", "sc", "oag", "pmg", "te"];

    /// <summary>Prefixes for generator types (used when resolving generator references).</summary>
    private static readonly string[] _generatorPrefixes =
        ["sm", "sel", "blend", "clip", "mg", "stg", "bref", "cb", "bsg", "sc", "oag", "pmg"];

    /// <summary>Prefixes for modifier types (used when resolving modifier references).</summary>
    private static readonly string[] _modifierPrefixes =
        ["iam", "edm", "een", "gm", "ficm", "eem", "ml"];

    /// <summary>Resolve a name specifically as a transition effect (tries "te" prefix first).</summary>
    private string ResolveTransitionEffectId(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "null") return "null";
        if (_nodeIds.TryGetValue(NodeKey("te", name), out var teId)) return teId;
        return ResolveId(name);
    }

    /// <summary>Resolve a name as a generator reference.</summary>
    private string ResolveGeneratorId(string? name) => ResolveId(name, _generatorPrefixes);

    /// <summary>Resolve a name as a modifier reference.</summary>
    private string ResolveModifierId(string? name) => ResolveId(name, _modifierPrefixes);

    private static string Bool(bool v) => v ? "true" : "false";

    /// <summary>Resolve a bone name to its index using the skeleton.</summary>
    private int ResolveBoneIndex(string boneName)
    {
        if (int.TryParse(boneName, out int idx)) return idx;
        if (_data.BoneNames != null)
        {
            var index = _data.BoneNames.IndexOf(boneName);
            if (index >= 0) return index;
        }
        Console.Error.WriteLine($"  WARNING: Cannot resolve bone '{boneName}'");
        return -1;
    }

    /// <summary>States use a prefixed key to avoid name collisions with generators.</summary>
    private static string StateKey(string name) => $"state:{name}";

    /// <summary>Build a type-prefixed key for _nodeIds to disambiguate same-named objects
    /// across different collections (e.g. clip vs state machine).</summary>
    private static string NodeKey(string collection, string name) => $"{collection}:{name}";

    /// <summary>Emit any string event payloads referenced by an event list.</summary>
    private void EmitPayloadsForEvents(List<EventPropertyDef>? events)
    {
        if (events == null) return;
        foreach (var ev in events)
        {
            if (!string.IsNullOrEmpty(ev.Payload) && ev.Payload != "null"
                && _payloadIds.TryGetValue(ev.Payload, out var payId)
                && !_dfsVisited.Contains($"payload:{ev.Payload}"))
            {
                _dfsVisited.Add($"payload:{ev.Payload}");
                var capturedData = ev.Payload;
                var capturedPayId = payId;
                _emissions.Add(sb => EmitStringEventPayload(sb, capturedPayId, capturedData));
            }
        }
    }

    private static string XmlEsc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
