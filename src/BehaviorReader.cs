using HKBuild.Models;
using YamlDotNet.Serialization;

namespace HKBuild;

/// <summary>
/// Loaded behavior data — everything the emitter needs.
/// All nodes keyed by their unique name.
/// </summary>
public class BehaviorData
{
    public required BehaviorFile Behavior { get; init; }
    public required Dictionary<string, ClipGeneratorDef> Clips { get; init; }
    public required Dictionary<string, BlenderGeneratorDef> Blenders { get; init; }
    public required Dictionary<string, ManualSelectorDef> Selectors { get; init; }
    public required Dictionary<string, StateMachineDef> StateMachines { get; init; }
    public required Dictionary<string, StateDef> States { get; init; }
    public required Dictionary<string, TransitionEffectDef> TransitionEffects { get; init; }
    public required Dictionary<string, ModifierGeneratorDef> ModifierGenerators { get; init; }
    public required Dictionary<string, BSIsActiveModifierDef> IsActiveModifiers { get; init; }
    public required Dictionary<string, BSiStateTaggingGeneratorDef> StateTaggingGenerators { get; init; }
    public required Dictionary<string, BehaviorReferenceGeneratorDef> BehaviorReferences { get; init; }
    public required Dictionary<string, ModifierListDef> ModifierLists { get; init; }
    public required Dictionary<string, BSCyclicBlendTransitionGeneratorDef> CyclicBlendGenerators { get; init; }
    public required Dictionary<string, EventDrivenModifierDef> EventDrivenModifiers { get; init; }
    public required Dictionary<string, BSEventEveryNEventsModifierDef> EventEveryNModifiers { get; init; }
    public required Dictionary<string, GenericModifierDef> GenericModifiers { get; init; }
    public required Dictionary<string, FootIkControlsModifierDef> FootIkControlsModifiers { get; init; }
    public required Dictionary<string, EvaluateExpressionModifierDef> EvaluateExpressionModifiers { get; init; }
    public required Dictionary<string, BSBoneSwitchGeneratorDef> BoneSwitchGenerators { get; init; }
    public required Dictionary<string, BSSynchronizedClipGeneratorDef> SynchronizedClips { get; init; }
    public required Dictionary<string, BSOffsetAnimationGeneratorDef> OffsetAnimGenerators { get; init; }
    public required Dictionary<string, PoseMatchingGeneratorDef> PoseMatchingGenerators { get; init; }
    public required Dictionary<string, ExpressionDataArrayDef> ExpressionDataArrays { get; init; }
    public required Dictionary<string, EventRangeDataArrayDef> EventRangeDataArrays { get; init; }
    public required Dictionary<string, BoneIndexArrayDef> BoneIndexArrays { get; init; }
    public BehaviorGraphDataDef? GraphData { get; init; }

    /// <summary>Skeleton bone names in index order (null if no skeleton.yaml found).</summary>
    public List<string>? BoneNames { get; init; }

    /// <summary>Behavior-local bone weight presets loaded from bone_presets.yaml.</summary>
    public required Dictionary<string, Dictionary<string, string>> BonePresets { get; init; }

    /// <summary>
    /// Node name → absolute source file path.
    /// Populated by BehaviorReader for every loaded node so the emitter can
    /// include the originating file in error messages.
    /// </summary>
    public required Dictionary<string, string> SourceFiles { get; init; }

    /// <summary>Resolve a generator name to its class type.</summary>
    public string? GetNodeClass(string name)
    {
        if (name == "null") return null;
        if (Clips.ContainsKey(name)) return "hkbClipGenerator";
        if (Blenders.ContainsKey(name)) return "hkbBlenderGenerator";
        if (Selectors.ContainsKey(name)) return "hkbManualSelectorGenerator";
        if (StateMachines.ContainsKey(name)) return "hkbStateMachine";
        if (ModifierGenerators.ContainsKey(name)) return "hkbModifierGenerator";
        if (IsActiveModifiers.ContainsKey(name)) return "BSIsActiveModifier";
        if (StateTaggingGenerators.ContainsKey(name)) return "BSiStateTaggingGenerator";
        if (BehaviorReferences.ContainsKey(name)) return "hkbBehaviorReferenceGenerator";
        if (ModifierLists.ContainsKey(name)) return "hkbModifierList";
        if (CyclicBlendGenerators.ContainsKey(name)) return "BSCyclicBlendTransitionGenerator";
        if (EventDrivenModifiers.ContainsKey(name)) return "hkbEventDrivenModifier";
        if (EventEveryNModifiers.ContainsKey(name)) return "BSEventEveryNEventsModifier";
        if (GenericModifiers.ContainsKey(name)) return GenericModifiers[name].Class;
        if (FootIkControlsModifiers.ContainsKey(name)) return "hkbFootIkControlsModifier";
        if (EvaluateExpressionModifiers.ContainsKey(name)) return "hkbEvaluateExpressionModifier";
        if (BoneSwitchGenerators.ContainsKey(name)) return "BSBoneSwitchGenerator";
        if (SynchronizedClips.ContainsKey(name)) return "BSSynchronizedClipGenerator";
        if (OffsetAnimGenerators.ContainsKey(name)) return "BSOffsetAnimationGenerator";
        if (PoseMatchingGenerators.ContainsKey(name)) return "hkbPoseMatchingGenerator";
        return null;
    }
}

/// <summary>
/// Reads a behavior source directory and returns a fully loaded BehaviorData.
/// </summary>
public static class BehaviorReader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static BehaviorData Load(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Behavior directory not found: {directory}");

        // Tracks node name → absolute source file path for all loaded nodes.
        // Passed into every load helper so the emitter can report precise file
        // locations in error messages using the MSBuild format path(1): error : msg.
        var sourceFiles = new Dictionary<string, string>();

        var behavior = LoadYaml<BehaviorFile>(Path.Combine(directory, "behavior.yaml"));
        var clips = LoadNodeDir<ClipGeneratorDef>(Path.Combine(directory, "clips"), sourceFiles);
        var selectors = LoadNodeDir<ManualSelectorDef>(Path.Combine(directory, "selectors"), sourceFiles);
        var transitionEffects = LoadNodeDir<TransitionEffectDef>(Path.Combine(directory, "transitions"), sourceFiles);

        // Generators directory contains multiple class types — disambiguate by class field.
        var blenders = new Dictionary<string, BlenderGeneratorDef>();
        var cyclicBlendGenerators = new Dictionary<string, BSCyclicBlendTransitionGeneratorDef>();
        var boneSwitchGenerators = new Dictionary<string, BSBoneSwitchGeneratorDef>();
        var synchronizedClips = new Dictionary<string, BSSynchronizedClipGeneratorDef>();
        var offsetAnimGenerators = new Dictionary<string, BSOffsetAnimationGeneratorDef>();
        var poseMatchingGenerators = new Dictionary<string, PoseMatchingGeneratorDef>();
        var generatorsDir = Path.Combine(directory, "generators");
        if (Directory.Exists(generatorsDir))
        {
            foreach (var file in Directory.GetFiles(generatorsDir, "*.yaml", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                var absFile = Path.GetFullPath(file);
                if (text.Contains("class: BSCyclicBlendTransitionGenerator"))
                {
                    var cb = DeserializeFrom<BSCyclicBlendTransitionGeneratorDef>(text, absFile);
                    cyclicBlendGenerators[cb.Name] = cb;
                    sourceFiles[cb.Name] = absFile;
                }
                else if (text.Contains("class: BSBoneSwitchGenerator"))
                {
                    var bsg = DeserializeFrom<BSBoneSwitchGeneratorDef>(text, absFile);
                    boneSwitchGenerators[bsg.Name] = bsg;
                    sourceFiles[bsg.Name] = absFile;
                }
                else if (text.Contains("class: BSSynchronizedClipGenerator"))
                {
                    var sc = DeserializeFrom<BSSynchronizedClipGeneratorDef>(text, absFile);
                    synchronizedClips[sc.Name] = sc;
                    sourceFiles[sc.Name] = absFile;
                }
                else if (text.Contains("class: BSOffsetAnimationGenerator"))
                {
                    var oag = DeserializeFrom<BSOffsetAnimationGeneratorDef>(text, absFile);
                    offsetAnimGenerators[oag.Name] = oag;
                    sourceFiles[oag.Name] = absFile;
                }
                else if (text.Contains("class: hkbPoseMatchingGenerator"))
                {
                    var pmg = DeserializeFrom<PoseMatchingGeneratorDef>(text, absFile);
                    poseMatchingGenerators[pmg.Name] = pmg;
                    sourceFiles[pmg.Name] = absFile;
                }
                else if (text.Contains("class: hkbManualSelectorGenerator"))
                {
                    var ms = DeserializeFrom<ManualSelectorDef>(text, absFile);
                    selectors[ms.Name] = ms;
                    sourceFiles[ms.Name] = absFile;
                }
                else
                {
                    var blend = DeserializeFrom<BlenderGeneratorDef>(text, absFile);
                    blenders[blend.Name] = blend;
                    sourceFiles[blend.Name] = absFile;
                }
            }
        }

        // Modifiers directory contains multiple class types — disambiguate by class field.
        var modifierGenerators = new Dictionary<string, ModifierGeneratorDef>();
        var isActiveModifiers = new Dictionary<string, BSIsActiveModifierDef>();
        var modifierLists = new Dictionary<string, ModifierListDef>();
        var eventDrivenModifiers = new Dictionary<string, EventDrivenModifierDef>();
        var eventEveryNModifiers = new Dictionary<string, BSEventEveryNEventsModifierDef>();
        var evaluateExpressionModifiers = new Dictionary<string, EvaluateExpressionModifierDef>();
        var genericModifiers = new Dictionary<string, GenericModifierDef>();
        var footIkControlsModifiers = new Dictionary<string, FootIkControlsModifierDef>();
        var modifiersDir = Path.Combine(directory, "modifiers");
        // Modifier classes with dedicated models (not handled by generic).
        var dedicatedModifierClasses = new HashSet<string>
        {
            "hkbModifierGenerator", "BSIsActiveModifier", "hkbModifierList",
            "hkbEventDrivenModifier", "BSEventEveryNEventsModifier",
            "hkbEvaluateExpressionModifier", "hkbFootIkControlsModifier"
        };
        if (Directory.Exists(modifiersDir))
        {
            foreach (var file in Directory.GetFiles(modifiersDir, "*.yaml"))
            {
                var text = File.ReadAllText(file);
                var absFile = Path.GetFullPath(file);
                if (text.Contains("class: hkbModifierGenerator"))
                {
                    var mg = DeserializeFrom<ModifierGeneratorDef>(text, absFile);
                    modifierGenerators[mg.Name] = mg;
                    sourceFiles[mg.Name] = absFile;
                }
                else if (text.Contains("class: BSIsActiveModifier"))
                {
                    var iam = DeserializeFrom<BSIsActiveModifierDef>(text, absFile);
                    isActiveModifiers[iam.Name] = iam;
                    sourceFiles[iam.Name] = absFile;
                }
                else if (text.Contains("class: hkbModifierList"))
                {
                    var ml = DeserializeFrom<ModifierListDef>(text, absFile);
                    modifierLists[ml.Name] = ml;
                    sourceFiles[ml.Name] = absFile;
                }
                else if (text.Contains("class: hkbEventDrivenModifier"))
                {
                    var edm = DeserializeFrom<EventDrivenModifierDef>(text, absFile);
                    eventDrivenModifiers[edm.Name] = edm;
                    sourceFiles[edm.Name] = absFile;
                }
                else if (text.Contains("class: BSEventEveryNEventsModifier"))
                {
                    var een = DeserializeFrom<BSEventEveryNEventsModifierDef>(text, absFile);
                    eventEveryNModifiers[een.Name] = een;
                    sourceFiles[een.Name] = absFile;
                }
                else if (text.Contains("class: hkbEvaluateExpressionModifier"))
                {
                    var eem = DeserializeFrom<EvaluateExpressionModifierDef>(text, absFile);
                    evaluateExpressionModifiers[eem.Name] = eem;
                    sourceFiles[eem.Name] = absFile;
                }
                else if (text.Contains("class: hkbFootIkControlsModifier"))
                {
                    var ficm = DeserializeFrom<FootIkControlsModifierDef>(text, absFile);
                    footIkControlsModifiers[ficm.Name] = ficm;
                    sourceFiles[ficm.Name] = absFile;
                }
                else
                {
                    // Generic modifier — parse base fields + extra params from raw dict.
                    var gm = DeserializeFrom<GenericModifierDef>(text, absFile);
                    if (gm != null && !string.IsNullOrEmpty(gm.Name))
                    {
                        gm.ExtraParams = ParseGenericExtraParams(text);
                        genericModifiers[gm.Name] = gm;
                        sourceFiles[gm.Name] = absFile;
                    }
                }
            }
        }

        // Behavior references directory.
        var behaviorReferences = LoadNodeDir<BehaviorReferenceGeneratorDef>(Path.Combine(directory, "references"), sourceFiles);

        // Tagging generators directory.
        var stateTaggingGenerators = LoadNodeDir<BSiStateTaggingGeneratorDef>(Path.Combine(directory, "tagging"), sourceFiles);

        // States directory contains both StateMachine and StateInfo files.
        var stateMachines = new Dictionary<string, StateMachineDef>();
        var states = new Dictionary<string, StateDef>();
        var statesDir = Path.Combine(directory, "states");
        if (Directory.Exists(statesDir))
        {
            foreach (var file in Directory.GetFiles(statesDir, "*.yaml"))
            {
                var text = File.ReadAllText(file);
                var absFile = Path.GetFullPath(file);
                // Peek at the class field to determine type.
                if (text.Contains("class: hkbStateMachine\n") || text.Contains("class: hkbStateMachine\r"))
                {
                    var sm = DeserializeFrom<StateMachineDef>(text, absFile);
                    // Parse wildcard transitions from the SM's inline transitions: block.
                    sm.ParsedWildcardTransitions = ParseTransitions(text);
                    stateMachines[sm.Name] = sm;
                    sourceFiles[sm.Name] = absFile;
                }
                else
                {
                    var state = DeserializeFrom<StateDef>(text, absFile);
                    // Parse enter/exit notify events from the raw YAML.
                    state.EnterNotifyEvents = ParseEventProperties(text, "enterNotifyEvents");
                    state.ExitNotifyEvents = ParseEventProperties(text, "exitNotifyEvents");
                    // Parse inline transitions from the raw YAML.
                    state.ParsedTransitions = ParseTransitions(text);
                    states[state.Name] = state;
                    sourceFiles[state.Name] = absFile;
                }
            }
        }

        // Load graph data if referenced.
        BehaviorGraphDataDef? graphData = null;
        var dataDir = Path.Combine(directory, "data");
        if (behavior.Behavior.Data != null && behavior.Behavior.Data != "null" && Directory.Exists(dataDir))
        {
            var dataFile = Path.Combine(dataDir, $"{behavior.Behavior.Data}.yaml");
            if (File.Exists(dataFile))
                graphData = LoadYaml<BehaviorGraphDataDef>(dataFile);
        }

        // Load expression data arrays, event range data arrays, and bone index arrays from data dir.
        var expressionDataArrays = new Dictionary<string, ExpressionDataArrayDef>();
        var eventRangeDataArrays = new Dictionary<string, EventRangeDataArrayDef>();
        var boneIndexArrays = new Dictionary<string, BoneIndexArrayDef>();
        if (Directory.Exists(dataDir))
        {
            foreach (var file in Directory.GetFiles(dataDir, "*_expressions.yaml"))
            {
                var eda = LoadYaml<ExpressionDataArrayDef>(file);
                expressionDataArrays[eda.Name] = eda;
            }
            foreach (var file in Directory.GetFiles(dataDir, "*_ranges.yaml"))
            {
                var erd = LoadYaml<EventRangeDataArrayDef>(file);
                eventRangeDataArrays[erd.Name] = erd;
            }
            foreach (var file in Directory.GetFiles(dataDir, "*_boneIndex.yaml"))
            {
                var bia = LoadYaml<BoneIndexArrayDef>(file);
                // Use the name field (set by extraction to ownerModifier_paramName).
                if (!string.IsNullOrEmpty(bia.Name))
                    boneIndexArrays[bia.Name] = bia;
            }
        }

        // Load skeleton bone names (search upward for character assets/skeleton.yaml).
        var boneNames = SkeletonReader.FindAndLoad(directory);

        // Load behavior-local bone weight presets.
        var bonePresets = new Dictionary<string, Dictionary<string, string>>();
        var presetsFile = Path.Combine(directory, "bone_presets.yaml");
        if (File.Exists(presetsFile))
        {
            var presetsDef = LoadYaml<BonePresetsDef>(presetsFile);
            bonePresets = presetsDef.Presets ?? new Dictionary<string, Dictionary<string, string>>();
        }

        // Fix up EvaluateExpressionModifier references: the extraction script writes
        // "expressions: null" because hkbExpressionDataArray has no name param.
        // The data array is named after the owning modifier — resolve by matching names.
        foreach (var eem in evaluateExpressionModifiers.Values)
        {
            if ((string.IsNullOrEmpty(eem.Expressions) || eem.Expressions == "null")
                && expressionDataArrays.ContainsKey(eem.Name))
            {
                eem.Expressions = eem.Name;
            }
        }

        // Similarly for hkbEventsFromRangeModifier — fix generic modifier 'eventRanges' references.
        foreach (var gm in genericModifiers.Values)
        {
            if (gm.Class == "hkbEventsFromRangeModifier")
            {
                var rangesParam = gm.ExtraParams.FirstOrDefault(p => p.Name == "eventRanges");
                if (rangesParam != null && (rangesParam.ScalarValue == "null" || string.IsNullOrEmpty(rangesParam.ScalarValue))
                    && eventRangeDataArrays.ContainsKey(gm.Name))
                {
                    rangesParam.ScalarValue = gm.Name;
                }
            }
        }

        // Fix up generic modifier bone index array references.
        // The extraction names arrays as "{modifierName}_{paramName}".
        foreach (var gm in genericModifiers.Values)
        {
            foreach (var p in gm.ExtraParams)
            {
                if (p.Kind == GenericParamKind.Scalar
                    && (p.ScalarValue == "null" || string.IsNullOrEmpty(p.ScalarValue)))
                {
                    var candidateKey = $"{gm.Name}_{p.Name}";
                    if (boneIndexArrays.ContainsKey(candidateKey))
                        p.ScalarValue = candidateKey;
                }
            }
        }

        // Resolve toState names → toStateId on transitions.
        ResolveStateNames(stateMachines, states, sourceFiles);
        ResolveBoneWeightPresets(blenders, boneSwitchGenerators, bonePresets, sourceFiles);

        Console.WriteLine($"Loaded behavior '{behavior.Behavior.Name}':");
        Console.WriteLine($"  {clips.Count} clips");
        Console.WriteLine($"  {blenders.Count} blender generators");
        Console.WriteLine($"  {cyclicBlendGenerators.Count} cyclic blend generators");
        Console.WriteLine($"  {boneSwitchGenerators.Count} bone switch generators");
        Console.WriteLine($"  {synchronizedClips.Count} synchronized clips");
        Console.WriteLine($"  {offsetAnimGenerators.Count} offset animation generators");
        Console.WriteLine($"  {poseMatchingGenerators.Count} pose matching generators");
        Console.WriteLine($"  {selectors.Count} manual selectors");
        Console.WriteLine($"  {modifierGenerators.Count} modifier generators");
        Console.WriteLine($"  {isActiveModifiers.Count} isActive modifiers");
        Console.WriteLine($"  {modifierLists.Count} modifier lists");
        Console.WriteLine($"  {eventDrivenModifiers.Count} event-driven modifiers");
        Console.WriteLine($"  {eventEveryNModifiers.Count} eventEveryN modifiers");
        Console.WriteLine($"  {evaluateExpressionModifiers.Count} expression modifiers");
        Console.WriteLine($"  {footIkControlsModifiers.Count} footIk controls modifiers");
        Console.WriteLine($"  {genericModifiers.Count} generic modifiers");
        Console.WriteLine($"  {stateTaggingGenerators.Count} state tagging generators");
        Console.WriteLine($"  {behaviorReferences.Count} behavior references");
        Console.WriteLine($"  {stateMachines.Count} state machines");
        Console.WriteLine($"  {states.Count} states");
        Console.WriteLine($"  {transitionEffects.Count} transition effects");
        Console.WriteLine($"  {expressionDataArrays.Count} expression data arrays");
        Console.WriteLine($"  {eventRangeDataArrays.Count} event range data arrays");
        Console.WriteLine($"  {boneIndexArrays.Count} bone index arrays");
        if (graphData != null)
            Console.WriteLine($"  graph data: {graphData.Variables.Count} vars, {graphData.Events.Count} events");

        return new BehaviorData
        {
            Behavior = behavior,
            Clips = clips,
            Blenders = blenders,
            Selectors = selectors,
            StateMachines = stateMachines,
            States = states,
            TransitionEffects = transitionEffects,
            ModifierGenerators = modifierGenerators,
            IsActiveModifiers = isActiveModifiers,
            StateTaggingGenerators = stateTaggingGenerators,
            BehaviorReferences = behaviorReferences,
            ModifierLists = modifierLists,
            CyclicBlendGenerators = cyclicBlendGenerators,
            EventDrivenModifiers = eventDrivenModifiers,
            EventEveryNModifiers = eventEveryNModifiers,
            GenericModifiers = genericModifiers,
            FootIkControlsModifiers = footIkControlsModifiers,
            EvaluateExpressionModifiers = evaluateExpressionModifiers,
            BoneSwitchGenerators = boneSwitchGenerators,
            SynchronizedClips = synchronizedClips,
            OffsetAnimGenerators = offsetAnimGenerators,
            PoseMatchingGenerators = poseMatchingGenerators,
            ExpressionDataArrays = expressionDataArrays,
            EventRangeDataArrays = eventRangeDataArrays,
            BoneIndexArrays = boneIndexArrays,
            GraphData = graphData,
            BoneNames = boneNames,
            BonePresets = bonePresets,
            SourceFiles = sourceFiles
        };
    }

    private static T LoadYaml<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{Path.GetFullPath(path)}(1): error : Required file not found.");

        var absPath = Path.GetFullPath(path);
        var text = File.ReadAllText(path);
        try
        {
            return Yaml.Deserialize<T>(text)
                ?? throw new InvalidDataException($"{absPath}(1): error : Failed to deserialize (null result).");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"{absPath}(1): error : {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserialize YAML text that was already read from <paramref name="absFile"/>.
    /// Wraps any parse exception in the MSBuild error format so VS can locate it.
    /// </summary>
    private static T DeserializeFrom<T>(string text, string absFile)
    {
        try
        {
            return Yaml.Deserialize<T>(text)
                ?? throw new InvalidDataException($"{absFile}(1): error : Failed to deserialize (null result).");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"{absFile}(1): error : {ex.Message}", ex);
        }
    }

    private static Dictionary<string, T> LoadNodeDir<T>(
        string dir, Dictionary<string, string> sourceFiles) where T : class
    {
        var result = new Dictionary<string, T>();
        if (!Directory.Exists(dir)) return result;

        foreach (var file in Directory.GetFiles(dir, "*.yaml", SearchOption.AllDirectories))
        {
            var absFile = Path.GetFullPath(file);
            var text = File.ReadAllText(file);
            var node = DeserializeFrom<T>(text, absFile);
            // Get the name via reflection (all node types have a Name property).
            var nameProp = typeof(T).GetProperty("Name");
            var name = nameProp?.GetValue(node) as string
                ?? Path.GetFileNameWithoutExtension(file);
            result[name] = node;
            sourceFiles[name] = absFile;
        }

        return result;
    }

    /// <summary>
    /// Parse enter/exit notify event lists from raw YAML text.
    /// Returns null if "null", or a List&lt;EventPropertyDef&gt;.
    /// </summary>
    private static object? ParseEventProperties(string yamlText, string fieldName)
    {
        // Find the field in the YAML text.
        var lines = yamlText.Split('\n');
        int fieldLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd().StartsWith($"{fieldName}:"))
            {
                fieldLine = i;
                break;
            }
        }

        if (fieldLine < 0) return null;

        var value = lines[fieldLine].Substring(lines[fieldLine].IndexOf(':') + 1).Trim();
        if (value == "null") return null;

        // Parse the list of events.
        var events = new List<EventPropertyDef>();
        for (int i = fieldLine + 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("  ")) break; // End of indented block.

            if (line.TrimStart().StartsWith("- id:") || line.TrimStart().StartsWith("- event:"))
            {
                var ev = new EventPropertyDef();
                if (line.TrimStart().StartsWith("- event:"))
                {
                    var eventName = line.Substring(line.IndexOf("event:") + 6).Trim();
                    ev.Event = eventName;
                }
                else
                {
                    var idStr = line.Substring(line.IndexOf("id:") + 3).Trim();
                    ev.Id = int.Parse(idStr);
                }

                // Next line should be payload.
                if (i + 1 < lines.Length && lines[i + 1].Contains("payload:"))
                {
                    var payloadStr = lines[i + 1].Substring(lines[i + 1].IndexOf("payload:") + 8).Trim();
                    ev.Payload = payloadStr;
                    i++;
                }

                events.Add(ev);
            }
        }

        return events.Count > 0 ? events : null;
    }

    /// <summary>Parse inline transitions list from raw YAML text.</summary>
    private static List<TransitionInfoDef>? ParseTransitions(string yamlText)
    {
        // Find the "transitions:" field.
        var lines = yamlText.Split('\n');
        int fieldLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd().StartsWith("transitions:"))
            {
                fieldLine = i;
                break;
            }
        }

        if (fieldLine < 0) return null;

        // Extract the transitions block as a standalone YAML document.
        var transBlock = new System.Text.StringBuilder();
        transBlock.AppendLine("items:");
        for (int i = fieldLine + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("  ")) break;
            transBlock.AppendLine(line);
        }

        var wrapper = Yaml.Deserialize<TransitionsWrapper>(transBlock.ToString());
        return wrapper?.Items;
    }

    private class TransitionsWrapper
    {
        [YamlMember(Alias = "items")]
        public List<TransitionInfoDef>? Items { get; set; }
    }

    /// <summary>
    /// Resolve toState names to toStateId on all transitions.
    /// For each state machine, builds a state name → stateId lookup
    /// and resolves any transition that uses toState instead of toStateId.
    /// </summary>
    private static void ResolveStateNames(
        Dictionary<string, StateMachineDef> stateMachines,
        Dictionary<string, StateDef> states,
        Dictionary<string, string> sourceFiles)
    {
        foreach (var sm in stateMachines.Values)
        {
            // Build name → stateId lookup for this state machine's states.
            var stateNameToId = new Dictionary<string, int>();
            foreach (var stateName in sm.States)
            {
                if (states.TryGetValue(stateName, out var state))
                    stateNameToId[stateName] = state.StateId;
            }

            // Resolve toState on transitions within each of this SM's states.
            foreach (var stateName in sm.States)
            {
                if (!states.TryGetValue(stateName, out var state)) continue;
                if (state.ParsedTransitions == null) continue;

                var stateFile = sourceFiles.TryGetValue(stateName, out var sf) ? sf : "(unknown)";
                foreach (var t in state.ParsedTransitions)
                {
                    if (t.ToState != null)
                    {
                        if (stateNameToId.TryGetValue(t.ToState, out int id))
                            t.ToStateId = id;
                        else
                            throw new InvalidDataException(
                                $"{stateFile}(1): error : Unknown state '{t.ToState}' in transition from '{stateName}'." +
                                $" Available states: {string.Join(", ", stateNameToId.Keys)}");
                    }
                }
            }
                    // Resolve toState on wildcard transitions for this SM.
                    if (sm.ParsedWildcardTransitions != null)
                    {
                        var smFile = sourceFiles.TryGetValue(sm.Name, out var smf) ? smf : "(unknown)";
                        foreach (var t in sm.ParsedWildcardTransitions)
                        {
                            if (t.ToState != null)
                            {
                                if (stateNameToId.TryGetValue(t.ToState, out int id))
                                    t.ToStateId = id;
                                else
                                    throw new InvalidDataException(
                                        $"{smFile}(1): error : Unknown state '{t.ToState}' in wildcard transition of '{sm.Name}'." +
                                        $" Available states: {string.Join(", ", stateNameToId.Keys)}");
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Resolve boneWeights.preset references into boneWeights.named dictionaries.
            /// </summary>
            private static void ResolveBoneWeightPresets(
                Dictionary<string, BlenderGeneratorDef> blenders,
                Dictionary<string, BSBoneSwitchGeneratorDef> boneSwitchGenerators,
                Dictionary<string, Dictionary<string, string>> presets,
                Dictionary<string, string> sourceFiles)
            {
                foreach (var blend in blenders.Values)
                {
                    if (blend.Children == null) continue;
                    foreach (var child in blend.Children)
                        ResolveBoneWeightPreset(child.BoneWeights, blend.Name, sourceFiles, presets);
                }

                foreach (var bsg in boneSwitchGenerators.Values)
                {
                    if (bsg.Children == null) continue;
                    foreach (var child in bsg.Children)
                        ResolveBoneWeightPreset(child.BoneWeights, bsg.Name, sourceFiles, presets);
                }
            }

            private static void ResolveBoneWeightPreset(
                BoneWeightsDef? boneWeights,
                string ownerNodeName,
                Dictionary<string, string> sourceFiles,
                Dictionary<string, Dictionary<string, string>> presets)
            {
                if (boneWeights == null || !boneWeights.IsPreset)
                    return;

                var sourceFile = sourceFiles.TryGetValue(ownerNodeName, out var sf) ? sf : "(unknown)";
                var presetName = boneWeights.Preset!.Trim();

                if (boneWeights.Named != null)
                {
                    throw new InvalidDataException(
                        $"{sourceFile}(1): error : boneWeights on '{ownerNodeName}' sets both 'preset' and 'named'.");
                }

                if (!presets.TryGetValue(presetName, out var presetWeights))
                {
                    throw new InvalidDataException(
                        $"{sourceFile}(1): error : Unknown bone weight preset '{presetName}' on '{ownerNodeName}'.");
                }

                boneWeights.Named = new Dictionary<string, string>(presetWeights);
            }

            /// <summary>Parse extra params from generic modifier YAML text (everything beyond base fields).</summary>
            private static List<GenericParam> ParseGenericExtraParams(string yamlText)
            {
                var baseFields = new HashSet<string> { "class", "name", "userData", "enable", "bindings" };
                var result = new List<GenericParam>();
                var lines = yamlText.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("  ") || line.StartsWith("#")) continue;

                    var colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;

                    var key = line[..colonIdx].Trim();
                    if (baseFields.Contains(key)) continue;

                    var valueStr = line[(colonIdx + 1)..].Trim();

                    // Check if next line is indented (inline event or ref-list).
                    if (string.IsNullOrEmpty(valueStr) && i + 1 < lines.Length && lines[i + 1].TrimEnd().StartsWith("  "))
                    {
                        // Collect the indented block.
                        var blockLines = new List<string>();
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            var bl = lines[j].TrimEnd();
                            if (string.IsNullOrWhiteSpace(bl) || !bl.StartsWith("  ")) break;
                            blockLines.Add(bl);
                        }

                        if (blockLines.Count > 0 && blockLines[0].TrimStart().StartsWith("event:"))
                        {
                            // Inline event (event: name format).
                            var eventName = blockLines[0].TrimStart()["event:".Length..].Trim();
                            result.Add(new GenericParam
                            {
                                Name = key,
                                Kind = GenericParamKind.InlineEvent,
                                EventValue = new InlineEventDef { Event = eventName }
                            });
                            i += blockLines.Count;
                        }
                        else if (blockLines.Count > 0 && blockLines[0].TrimStart().StartsWith("id:"))
                        {
                            // Inline event (id: N format).
                            var idStr = blockLines[0].TrimStart()["id:".Length..].Trim();
                            result.Add(new GenericParam
                            {
                                Name = key,
                                Kind = GenericParamKind.InlineEvent,
                                EventValue = new InlineEventDef { Id = int.Parse(idStr) }
                            });
                            i += blockLines.Count;
                        }
                        else if (blockLines.Count > 0 && blockLines[0].TrimStart().StartsWith("- "))
                        {
                            // Check if it's an inline object list (- key: value) or a ref list (- name).
                            var firstItem = blockLines[0].TrimStart()[2..].Trim();
                            if (firstItem.Contains(':'))
                            {
                                // Inline object list (e.g. bones/eyeBones on BSLookAtModifier).
                                var objects = new List<InlineObjectEntry>();
                                InlineObjectEntry? current = null;
                                foreach (var bl in blockLines)
                                {
                                    var trimmed = bl.TrimStart();
                                    if (trimmed.StartsWith("- "))
                                    {
                                        current = new InlineObjectEntry();
                                        objects.Add(current);
                                        var kv = trimmed[2..].Trim();
                                        var kvColon = kv.IndexOf(':');
                                        if (kvColon > 0)
                                            current.Fields.Add(new(kv[..kvColon].Trim(), kv[(kvColon + 1)..].Trim()));
                                    }
                                    else if (current != null)
                                    {
                                        var kv = trimmed.Trim();
                                        var kvColon = kv.IndexOf(':');
                                        if (kvColon > 0)
                                            current.Fields.Add(new(kv[..kvColon].Trim(), kv[(kvColon + 1)..].Trim()));
                                    }
                                }
                                result.Add(new GenericParam
                                {
                                    Name = key,
                                    Kind = GenericParamKind.InlineObjectList,
                                    InlineObjectListValue = objects
                                });
                                i += blockLines.Count;
                            }
                            else
                            {
                                // Ref list.
                                var refs = blockLines
                                    .Where(l => l.TrimStart().StartsWith("- "))
                                    .Select(l => l.TrimStart()[2..].Trim())
                                    .ToList();
                                result.Add(new GenericParam
                                {
                                    Name = key,
                                    Kind = GenericParamKind.RefList,
                                    RefListValue = refs
                                });
                                i += blockLines.Count;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(valueStr))
                    {
                        // Scalar or reference. References are names that exist as other nodes.
                        // We can't resolve here (don't have the full node list yet), so store as scalar.
                        // The emitter will check if the value is a known node name.
                        result.Add(new GenericParam
                        {
                            Name = key,
                            Kind = GenericParamKind.Scalar,
                            ScalarValue = valueStr
                        });
                    }
                }

                return result;
            }
        }
