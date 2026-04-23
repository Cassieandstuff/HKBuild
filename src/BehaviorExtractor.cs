using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HKBuild;

/// <summary>
/// Native C# port of extract_behavior.ps1.
/// Extracts a Havok packfile XML into the YAML source tree consumed by HKBuild.
/// </summary>
public static class BehaviorExtractor
{
    /// <summary>
    /// Extracts a behavior XML string into a YAML source tree.
    /// </summary>
    /// <param name="xmlContent">Havok packfile XML (from HKX2E or hkxcmd).</param>
    /// <param name="outputDir">Destination folder. Must not exist unless <paramref name="force"/> is true.</param>
    /// <param name="force">Overwrite an existing output directory.</param>
    /// <param name="log">Progress callback; defaults to Console.WriteLine.</param>
    public static void Extract(string xmlContent, string outputDir,
                               bool force = false, Action<string>? log = null)
    {
        log ??= Console.WriteLine;
        if (Directory.Exists(outputDir) && !force)
            throw new InvalidOperationException(
                $"Output directory '{outputDir}' already exists. Use --force to overwrite.");

        new ExtractContext(xmlContent, outputDir, log).Run();
    }

    // =========================================================================
    //  Private context
    // =========================================================================
    private sealed class ExtractContext
    {
        private readonly string            _out;
        private readonly Action<string>    _log;
        private readonly List<XElement>    _all;
        private readonly Dictionary<string, XElement>  _byId;
        private readonly Dictionary<string, string>    _nameOf;

        private readonly List<string>            _varNames = new();
        private readonly List<string>            _evNames  = new();
        private readonly Dictionary<int, string> _varByIdx = new();
        private readonly Dictionary<int, string> _evByIdx  = new();
        private readonly List<string>            _boneNames = new();
        private readonly Dictionary<int, string> _boneByIdx = new();
        private readonly Dictionary<string, Dictionary<int, string>> _smStates = new();

        private readonly string _classver;
        private readonly string _contentsver;

        internal ExtractContext(string xml, string outputDir, Action<string> log)
        {
            _out = outputDir;
            _log = log;

            var doc  = XDocument.Parse(xml);
            var root = doc.Root!;
            _classver    = (string?)root.Attribute("classversion")   ?? "";
            _contentsver = (string?)root.Attribute("contentsversion") ?? "";

            _all = root.Elements("hksection")
                       .SelectMany(s => s.Elements("hkobject"))
                       .ToList();

            _byId   = new(_all.Count);
            _nameOf = new(_all.Count);
            foreach (var obj in _all)
            {
                var id = (string?)obj.Attribute("name");
                if (id is null) continue;
                _byId[id] = obj;
                var n = P(obj, "name");
                if (n is not null) _nameOf[id] = n;
            }

            _boneNames.AddRange(LoadBoneNames(_out));
            for (int i = 0; i < _boneNames.Count; i++) _boneByIdx[i] = _boneNames[i];

            var strObj = _all.FirstOrDefault(o => Cls(o) == "hkbBehaviorGraphStringData");
            if (strObj is not null)
            {
                foreach (var e in PNode(strObj, "variableNames")
                                  ?.Elements("hkcstring") ?? Enumerable.Empty<XElement>())
                    _varNames.Add(e.Value);
                foreach (var e in PNode(strObj, "eventNames")
                                  ?.Elements("hkcstring") ?? Enumerable.Empty<XElement>())
                    _evNames.Add(e.Value);
            }
            for (int i = 0; i < _varNames.Count; i++) _varByIdx[i] = _varNames[i];
            for (int i = 0; i < _evNames.Count; i++)  _evByIdx[i]  = _evNames[i];

            foreach (var sm in _all.Where(o => Cls(o) == "hkbStateMachine"))
            {
                var smName = P(sm, "name") ?? "";
                var map = new Dictionary<int, string>();
                foreach (var stRef in RefList(P(sm, "states")))
                {
                    if (!_byId.TryGetValue(stRef, out var stObj)) continue;
                    var sn = P(stObj, "name") ?? "";
                    if (int.TryParse(P(stObj, "stateId"), out int sid)) map[sid] = sn;
                }
                _smStates[smName] = map;
            }
        }

        private static readonly string[] SubDirs =
            ["clips", "generators", "selectors", "states", "transitions",
             "modifiers", "tagging", "references", "data"];

        internal void Run()
        {
            foreach (var sub in SubDirs)
                Directory.CreateDirectory(Path.Combine(_out, sub));

            ExtractBehaviorYaml();
            ExtractGraphData();

            int clips    = ExtractClips();             _log($"  {clips} clips");
            int blenders = ExtractBlenderGenerators(); _log($"  {blenders} blender generators");
            int sels     = ExtractSelectors();         _log($"  {sels} selectors");
            var (sms, sts) = ExtractStates();
            _log($"  {sms} state machines");
            _log($"  {sts} states");
            int tes  = ExtractTransitionEffects();     _log($"  {tes} transition effects");
            int mods = ExtractModifiers();             _log($"  {mods} modifiers");

            int tags  = ExtractTaggingGenerators();    if (tags  > 0) _log($"  {tags} state tagging generators");
            int refs  = ExtractBehaviorRefs();         if (refs  > 0) _log($"  {refs} behavior references");
            int cbgs  = ExtractCyclicBlends();         if (cbgs  > 0) _log($"  {cbgs} cyclic blend generators");
            int bsgs  = ExtractBoneSwitchGenerators(); if (bsgs  > 0) _log($"  {bsgs} bone switch generators");
            int syncs = ExtractSynchronizedClips();    if (syncs > 0) _log($"  {syncs} synchronized clips");
            ExtractOffsetAnimationGenerators();
            ExtractPoseMatchingGenerators();

            int edas = ExtractExpressionDataArrays();  if (edas > 0) _log($"  {edas} expression data arrays");
            int erds = ExtractEventRangeDataArrays();   if (erds > 0) _log($"  {erds} event range data arrays");
            int bias = ExtractBoneIndexArrays();         if (bias > 0) _log($"  {bias} bone index arrays");

            foreach (var sub in SubDirs)
            {
                var p = Path.Combine(_out, sub);
                if (Directory.Exists(p) && !Directory.EnumerateFiles(p).Any())
                    Directory.Delete(p);
            }

            int total = Directory.EnumerateFiles(_out, "*", SearchOption.AllDirectories).Count();
            _log($"\nDone! Extracted {total} files to {_out}");
        }

        // ── behavior.yaml ─────────────────────────────────────────────────── //
        private void ExtractBehaviorYaml()
        {
            var bg = _all.FirstOrDefault(o => Cls(o) == "hkbBehaviorGraph");
            if (bg is null) return;

            var sb = new StringBuilder();
            sb.Append("packfile:\n");
            sb.Append($"  classversion: {_classver}\n");
            sb.Append($"  contentsversion: \"{_contentsver}\"\n");
            sb.Append("\nbehavior:\n");
            sb.Append($"  name: \"{P(bg, "name")}\"\n");
            sb.Append($"  variableMode: {P(bg, "variableMode")}\n");
            sb.Append($"  rootGenerator: {Ref(P(bg, "rootGenerator"))}\n");
            var data = P(bg, "data");
            if (data is not null && data != "null") sb.Append("  data: graphdata\n");

            Write(Path.Combine(_out, "behavior.yaml"), sb);
            _log("  behavior.yaml");
        }

        // ── data/graphdata.yaml ───────────────────────────────────────────── //
        private void ExtractGraphData()
        {
            var gd  = _all.FirstOrDefault(o => Cls(o) == "hkbBehaviorGraphData");
            var vvs = _all.FirstOrDefault(o => Cls(o) == "hkbVariableValueSet");
            if (gd is null) return;

            var varInfos = PNode(gd, "variableInfos")?.Elements("hkobject").ToList() ?? [];
            var evInfos  = PNode(gd, "eventInfos")?.Elements("hkobject").ToList()  ?? [];
            var wordVals = vvs is not null
                ? (PNode(vvs, "wordVariableValues")?.Elements("hkobject").ToList() ?? [])
                : (List<XElement>)[];

            var sb = new StringBuilder();
            sb.Append("variables:\n");
            for (int i = 0; i < _varNames.Count; i++)
            {
                var info      = i < varInfos.Count ? varInfos[i] : null;
                var roleInner = info is not null ? PNode(info, "role")?.Element("hkobject") : null;
                var role      = roleInner is not null ? P(roleInner, "role")  : null;
                var rflags    = roleInner is not null ? P(roleInner, "flags") : null;
                var type      = info is not null ? P(info, "type") : null;
                var val       = i < wordVals.Count ? P(wordVals[i], "value") : null;

                sb.Append($"  - name: {_varNames[i]}\n");
                sb.Append($"    type: {type}\n");
                if (role is not null && role != "ROLE_DEFAULT") sb.Append($"    role: {role}\n");
                if (!IsDefault(rflags)) sb.Append($"    roleFlags: {rflags}\n");
                sb.Append($"    value: {val}\n");
            }
            sb.Append("\nevents:\n");
            for (int i = 0; i < _evNames.Count; i++)
            {
                var info   = i < evInfos.Count ? evInfos[i] : null;
                var eflags = info is not null ? P(info, "flags") : null;
                sb.Append($"  - name: {_evNames[i]}\n");
                if (eflags is not null && eflags != "0") sb.Append($"    flags: {eflags}\n");
            }

            Write(Path.Combine(_out, "data", "graphdata.yaml"), sb);
            _log($"  data/graphdata.yaml ({_varNames.Count} vars, {_evNames.Count} events)");
        }

        // ── clips/*.yaml ──────────────────────────────────────────────────── //
        private int ExtractClips()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbClipGenerator"))
            {
                var cname = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbClipGenerator\n");
                sb.Append($"name: {cname}\n");
                sb.Append($"animationName: {P(obj, "animationName")}\n");
                sb.Append($"mode: {P(obj, "mode")}\n");
                sb.Append($"playbackSpeed: {Fmt(P(obj, "playbackSpeed"))}\n");
                OptInt(sb, obj, "userData");
                OptFloat(sb, obj, "startTime");
                OptFloat(sb, obj, "cropStartAmountLocalTime");
                OptFloat(sb, obj, "cropEndAmountLocalTime");
                OptFloat(sb, obj, "enforcedDuration");
                OptFloat(sb, obj, "userControlledTimeFraction");

                var flags = P(obj, "flags");
                if (!IsDefault(flags)) sb.Append($"flags: {flags}\n");
                var abi = P(obj, "animationBindingIndex");
                if (abi is not null && int.TryParse(abi, out int abiI) && abiI != -1)
                    sb.Append($"animationBindingIndex: {abi}\n");

                var trigRef = P(obj, "triggers");
                if (trigRef is not null && trigRef != "null" && _byId.TryGetValue(trigRef, out var trigObj))
                {
                    var trigs = PNode(trigObj, "triggers")?.Elements("hkobject").ToList() ?? [];
                    if (trigs.Count > 0)
                    {
                        sb.Append("triggers:\n");
                        foreach (var t in trigs)
                        {
                            var lt      = P(t, "localTime");
                            var evInner = PNode(t, "event")?.Element("hkobject");
                            var ep      = evInner is not null ? EventProp(evInner) : (null, -1, (string?)null);
                            sb.Append($"  - localTime: {Fmt(lt)}\n");
                            if (ep.Name is not null) sb.Append($"    event: {ep.Name}\n");
                            else sb.Append($"    eventId: {ep.Id}\n");
                            if (ep.Payload is not null) sb.Append($"    payload: {ep.Payload}\n");
                            if (P(t, "relativeToEndOfClip") == "true") sb.Append("    relativeToEndOfClip: true\n");
                            if (P(t, "acyclic")             == "true") sb.Append("    acyclic: true\n");
                            if (P(t, "isAnnotation")        == "true") sb.Append("    isAnnotation: true\n");
                        }
                    }
                }

                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "clips", SafeName(cname) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── generators/*.yaml  (hkbBlenderGenerator) ─────────────────────── //
        private int ExtractBlenderGenerators()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbBlenderGenerator"))
            {
                var bname = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbBlenderGenerator\n");
                sb.Append($"name: {bname}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"flags: {P(obj, "flags")}\n");
                sb.Append($"subtractLastChild: {P(obj, "subtractLastChild")}\n");
                sb.Append(Bindings(obj));
                sb.Append("children:\n");
                foreach (var cref in RefList(P(obj, "children")))
                {
                    if (!_byId.TryGetValue(cref, out var childObj)) continue;
                    sb.Append($"  - generator: {Ref(P(childObj, "generator"))}\n");
                    sb.Append($"    weight: {Fmt(P(childObj, "weight"))}\n");
                    sb.Append($"    worldFromModelWeight: {Fmt(P(childObj, "worldFromModelWeight"))}\n");
                    sb.Append(BoneWeightsYaml(P(childObj, "boneWeights")));
                }
                Write(Path.Combine(_out, "generators", SafeName(bname) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── selectors/*.yaml ──────────────────────────────────────────────── //
        private int ExtractSelectors()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbManualSelectorGenerator"))
            {
                var sname = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbManualSelectorGenerator\n");
                sb.Append($"name: {sname}\n");
                OptInt(sb, obj, "userData");
                sb.Append(Bindings(obj));
                sb.Append("generators:\n");
                foreach (var r in RefList(P(obj, "generators")))
                    sb.Append($"  - {Ref(r)}\n");
                Write(Path.Combine(_out, "selectors", SafeName(sname) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── states/*.yaml ─────────────────────────────────────────────────── //
        private (int smCount, int stateCount) ExtractStates()
        {
            int smCount = 0;
            var stateNames = _all.Where(o => Cls(o) == "hkbStateMachineStateInfo")
                                 .Select(o => P(o, "name"))
                                 .ToHashSet();

            foreach (var smObj in _all.Where(o => Cls(o) == "hkbStateMachine"))
            {
                var smName = P(smObj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbStateMachine\n");
                sb.Append($"name: {smName}\n");
                OptInt(sb, smObj, "userData");
                sb.Append($"startStateId: {P(smObj, "startStateId")}\n");
                OptEvent(sb, smObj, "returnToPreviousStateEventId",       "returnToPreviousStateEvent");
                OptEvent(sb, smObj, "randomTransitionEventId",            "randomTransitionEvent");
                OptEvent(sb, smObj, "transitionToNextHigherStateEventId", "transitionToNextHigherStateEvent");
                OptEvent(sb, smObj, "transitionToNextLowerStateEventId",  "transitionToNextLowerStateEvent");

                var stm = P(smObj, "selfTransitionMode");
                if (stm is not null && stm != "SELF_TRANSITION_MODE_NO_TRANSITION")
                    sb.Append($"selfTransitionMode: {stm}\n");
                var ssm = P(smObj, "startStateMode");
                if (ssm is not null && ssm != "START_STATE_MODE_DEFAULT")
                    sb.Append($"startStateMode: {ssm}\n");

                var svi = P(smObj, "syncVariableIndex");
                if (svi is not null && int.TryParse(svi, out int sviI) && sviI != -1)
                {
                    if (_varByIdx.TryGetValue(sviI, out var svn)) sb.Append($"syncVariable: {svn}\n");
                    else sb.Append($"syncVariableIndex: {svi}\n");
                }
                if (P(smObj, "wrapAroundStateId") == "true") sb.Append("wrapAroundStateId: true\n");
                var mst = P(smObj, "maxSimultaneousTransitions");
                if (mst is not null && int.TryParse(mst, out int mstI) && mstI != 32)
                    sb.Append($"maxSimultaneousTransitions: {mst}\n");

                sb.Append(Bindings(smObj));
                sb.Append(Transitions(smObj, "wildcardTransitions", smName));
                sb.Append("states:\n");
                foreach (var r in RefList(P(smObj, "states")))
                    sb.Append($"  - {Ref(r)}\n");

                var smFile = SafeName(smName);
                if (stateNames.Contains(smName)) smFile += "_SM";
                Write(Path.Combine(_out, "states", smFile + ".yaml"), sb);
                smCount++;
            }

            int stateCount = 0;
            foreach (var stateObj in _all.Where(o => Cls(o) == "hkbStateMachineStateInfo"))
            {
                var sName = P(stateObj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbStateMachineStateInfo\n");
                sb.Append($"name: {sName}\n");
                sb.Append($"stateId: {P(stateObj, "stateId")}\n");
                sb.Append($"generator: {Ref(P(stateObj, "generator"))}\n");

                var prob = P(stateObj, "probability");
                if (prob is not null &&
                    double.TryParse(prob, NumberStyles.Float, CultureInfo.InvariantCulture, out double probD)
                    && probD != 1.0)
                    sb.Append($"probability: {Fmt(prob)}\n");
                if (P(stateObj, "enable") == "false") sb.Append("enable: false\n");

                sb.Append(Bindings(stateObj));
                sb.Append(NotifyEvents(stateObj, "enterNotifyEvents"));
                sb.Append(NotifyEvents(stateObj, "exitNotifyEvents"));
                sb.Append(Transitions(stateObj, "transitions", FindOwnerSM(sName)));

                Write(Path.Combine(_out, "states", SafeName(sName) + ".yaml"), sb);
                stateCount++;
            }
            return (smCount, stateCount);
        }

        // ── transitions/*.yaml ────────────────────────────────────────────── //
        private int ExtractTransitionEffects()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbBlendingTransitionEffect"))
            {
                var tname = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbBlendingTransitionEffect\n");
                sb.Append($"name: {tname}\n");
                OptInt(sb, obj, "userData");
                var stm = P(obj, "selfTransitionMode");
                if (stm is not null && stm != "SELF_TRANSITION_MODE_CONTINUE_IF_CYCLIC_BLEND_IF_ACYCLIC")
                    sb.Append($"selfTransitionMode: {stm}\n");
                var em = P(obj, "eventMode");
                if (em is not null && em != "EVENT_MODE_DEFAULT") sb.Append($"eventMode: {em}\n");
                sb.Append($"duration: {Fmt(P(obj, "duration"))}\n");
                OptFloat(sb, obj, "toGeneratorStartTimeFraction");
                var flags = P(obj, "flags");
                if (!IsDefault(flags)) sb.Append($"flags: {flags}\n");
                var endMode = P(obj, "endMode");
                if (endMode is not null && endMode != "END_MODE_NONE") sb.Append($"endMode: {endMode}\n");
                var bc = P(obj, "blendCurve");
                if (bc is not null && bc != "BLEND_CURVE_SMOOTH") sb.Append($"blendCurve: {bc}\n");
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "transitions", SafeName(tname) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── modifiers/*.yaml ──────────────────────────────────────────────── //
        private int ExtractModifiers() =>
            ExtractModifierGenerators() +
            ExtractBSIsActiveModifiers() +
            ExtractModifierLists() +
            ExtractEventDrivenModifiers() +
            ExtractFootIkControlsModifiers() +
            ExtractGenericModifiers();

        private int ExtractModifierGenerators()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbModifierGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbModifierGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"modifier: {Ref(P(obj, "modifier"))}\n");
                sb.Append($"generator: {Ref(P(obj, "generator"))}\n");
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "modifiers", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractBSIsActiveModifiers()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "BSIsActiveModifier"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: BSIsActiveModifier\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                if (P(obj, "enable") == "false") sb.Append("enable: false\n");
                sb.Append(Bindings(obj));
                for (int i = 0; i <= 4; i++)
                {
                    if (P(obj, $"bIsActive{i}") == "true")     sb.Append($"bIsActive{i}: true\n");
                    if (P(obj, $"bInvertActive{i}") == "true")  sb.Append($"bInvertActive{i}: true\n");
                }
                Write(Path.Combine(_out, "modifiers", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractModifierLists()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbModifierList"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbModifierList\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                if (P(obj, "enable") == "false") sb.Append("enable: false\n");
                sb.Append(Bindings(obj));
                var modRefs = RefList(P(obj, "modifiers"));
                if (modRefs.Count > 0)
                {
                    sb.Append("modifiers:\n");
                    foreach (var r in modRefs) sb.Append($"  - {Ref(r)}\n");
                }
                Write(Path.Combine(_out, "modifiers", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractEventDrivenModifiers()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbEventDrivenModifier"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbEventDrivenModifier\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                if (P(obj, "enable") == "false") sb.Append("enable: false\n");
                sb.Append($"modifier: {Ref(P(obj, "modifier"))}\n");
                OptEvent(sb, obj, "activateEventId",   "activateEvent");
                OptEvent(sb, obj, "deactivateEventId", "deactivateEvent");
                if (P(obj, "activeByDefault") == "true") sb.Append("activeByDefault: true\n");
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "modifiers", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractFootIkControlsModifiers()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbFootIkControlsModifier"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbFootIkControlsModifier\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                if (P(obj, "enable") == "false") sb.Append("enable: false\n");
                sb.Append(Bindings(obj));

                var gainsInner = PNode(obj, "controlData")?.Element("hkobject") is { } cdInner
                               ? PNode(cdInner, "gains")?.Element("hkobject") : null;
                if (gainsInner is not null)
                {
                    sb.Append("controlData:\n  gains:\n");
                    foreach (var gp in gainsInner.Elements("hkparam"))
                    {
                        var gv = gp.Value.Trim();
                        if (!string.IsNullOrEmpty(gv))
                            sb.Append($"    {(string?)gp.Attribute("name")}: {Fmt(gv)}\n");
                    }
                }

                var legsNode = PNode(obj, "legs");
                if (legsNode is not null &&
                    int.TryParse((string?)legsNode.Attribute("numelements"), out int lc) && lc > 0)
                {
                    sb.Append("legs:\n");
                    foreach (var leg in legsNode.Elements("hkobject"))
                    {
                        sb.Append($"  - groundPosition: {P(leg, "groundPosition")}\n");
                        var ueInner = PNode(leg, "ungroundedEvent")?.Element("hkobject");
                        if (ueInner is not null)
                        {
                            var ueId = int.TryParse(P(ueInner, "id"), out int uid) ? uid : -1;
                            sb.Append("    ungroundedEvent:\n");
                            if (ueId == -1) sb.Append("      id: -1\n");
                            else if (_evByIdx.TryGetValue(ueId, out var uen)) sb.Append($"      event: {uen}\n");
                            else sb.Append($"      id: {ueId}\n");
                            var uePayRef = P(ueInner, "payload");
                            if (uePayRef is not null && uePayRef != "null" &&
                                _byId.TryGetValue(uePayRef, out var payObj))
                            {
                                var payData = P(payObj, "data");
                                if (payData is not null) sb.Append($"      payload: {payData}\n");
                            }
                        }
                        sb.Append($"    verticalError: {Fmt(P(leg, "verticalError"))}\n");
                        sb.Append($"    hitSomething: {Bool(P(leg, "hitSomething"))}\n");
                        sb.Append($"    isPlantedMS: {Bool(P(leg, "isPlantedMS"))}\n");
                    }
                }
                sb.Append($"errorOutTranslation: {P(obj, "errorOutTranslation")}\n");
                sb.Append($"alignWithGroundRotation: {P(obj, "alignWithGroundRotation")}\n");
                Write(Path.Combine(_out, "modifiers", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private static readonly HashSet<string> GenericModClasses = new()
        {
            "BSDirectAtModifier","BSEventOnFalseToTrueModifier","BSEventOnDeactivateModifier",
            "BSEventEveryNEventsModifier","BSInterpValueModifier","BSRagdollContactListenerModifier",
            "BSModifyOnceModifier","BSLookAtModifier","BSSpeedSamplerModifier",
            "hkbDampingModifier","hkbTwistModifier","hkbRotateCharacterModifier",
            "hkbTimerModifier","hkbKeyframeBonesModifier",
            "hkbGetUpModifier","hkbPoweredRagdollControlsModifier","hkbRigidBodyRagdollControlsModifier",
            "hkbEvaluateExpressionModifier","hkbEventsFromRangeModifier"
        };
        private static readonly HashSet<string> BaseModParams =
            new() { "variableBindingSet", "userData", "name", "enable" };

        private int ExtractGenericModifiers()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => GenericModClasses.Contains(Cls(o) ?? "")))
            {
                var mName = P(obj, "name") ?? "";
                var cls   = Cls(obj)!;
                var sb = new StringBuilder();
                sb.Append($"class: {cls}\n");
                sb.Append($"name: {mName}\n");
                OptInt(sb, obj, "userData");
                if (P(obj, "enable") == "false") sb.Append("enable: false\n");
                sb.Append(Bindings(obj));

                foreach (var p in obj.Elements("hkparam"))
                {
                    var pname = (string?)p.Attribute("name");
                    if (pname is null || BaseModParams.Contains(pname)) continue;

                    var children = p.Elements("hkobject").ToList();
                    if (children.Count > 0)
                    {
                        bool isInlineEvent = children.Count == 1 &&
                            p.Attribute("numelements") is null &&
                            children[0].Elements("hkparam")
                                       .Select(f => (string?)f.Attribute("name"))
                                       .ToHashSet() is { Count: 2 } fset &&
                            fset.Contains("id") && fset.Contains("payload");

                        if (isInlineEvent)
                        {
                            sb.Append(InlineEventYaml(obj, pname, ""));
                        }
                        else
                        {
                            sb.Append($"{pname}:\n");
                            foreach (var child in children)
                            {
                                bool first = true;
                                foreach (var fp in child.Elements("hkparam"))
                                {
                                    var fv = fp.Value.Trim(); var fname = (string?)fp.Attribute("name");
                                    if (!string.IsNullOrEmpty(fv) && fname is not null)
                                    { sb.Append(first ? $"  - {fname}: {fv}\n" : $"    {fname}: {fv}\n"); first = false; }
                                }
                            }
                        }
                        continue;
                    }

                    var pText = p.Value.Trim();
                    if (pText.StartsWith('#')) { sb.Append($"{pname}: {Ref(pText)}\n"); continue; }
                    if (pText.Contains('#'))
                    {
                        var refs = RefList(pText);
                        if (refs.Count > 0)
                        { sb.Append($"{pname}:\n"); foreach (var r in refs) sb.Append($"  - {Ref(r)}\n"); continue; }
                    }
                    if (!string.IsNullOrEmpty(pText)) sb.Append($"{pname}: {pText}\n");
                }
                Write(Path.Combine(_out, "modifiers", SafeName(mName) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── tagging/*.yaml ────────────────────────────────────────────────── //
        private int ExtractTaggingGenerators()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "BSiStateTaggingGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: BSiStateTaggingGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"pDefaultGenerator: {Ref(P(obj, "pDefaultGenerator"))}\n");
                sb.Append($"iStateToSetAs: {P(obj, "iStateToSetAs")}\n");
                sb.Append($"iPriority: {P(obj, "iPriority")}\n");
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "tagging", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── references/*.yaml ─────────────────────────────────────────────── //
        private int ExtractBehaviorRefs()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbBehaviorReferenceGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbBehaviorReferenceGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"behaviorName: {P(obj, "behaviorName")}\n");
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "references", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        // ── additional generators ─────────────────────────────────────────── //
        private int ExtractCyclicBlends()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "BSCyclicBlendTransitionGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: BSCyclicBlendTransitionGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"pBlenderGenerator: {Ref(P(obj, "pBlenderGenerator"))}\n");
                sb.Append(InlineEventYaml(obj, "EventToFreezeBlendValue", ""));
                sb.Append(InlineEventYaml(obj, "EventToCrossBlend", ""));
                sb.Append($"fBlendParameter: {Fmt(P(obj, "fBlendParameter"))}\n");
                sb.Append($"fTransitionDuration: {Fmt(P(obj, "fTransitionDuration"))}\n");
                var ebc = P(obj, "eBlendCurve");
                if (ebc is not null && ebc != "BLEND_CURVE_SMOOTH") sb.Append($"eBlendCurve: {ebc}\n");
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "generators", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractBoneSwitchGenerators()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "BSBoneSwitchGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: BSBoneSwitchGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"pDefaultGenerator: {Ref(P(obj, "pDefaultGenerator"))}\n");
                sb.Append(Bindings(obj));
                var childRefs = RefList(P(obj, "ChildrenA"));
                if (childRefs.Count > 0)
                {
                    sb.Append("children:\n");
                    foreach (var cref in childRefs)
                    {
                        if (!_byId.TryGetValue(cref, out var co)) continue;
                        sb.Append($"  - pGenerator: {Ref(P(co, "pGenerator"))}\n");
                        sb.Append(BoneWeightsYaml(P(co, "spBoneWeight")));
                        var childB = Bindings(co);
                        if (!string.IsNullOrEmpty(childB))
                            foreach (var line in childB.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                                sb.Append($"    {line}\n");
                    }
                }
                Write(Path.Combine(_out, "generators", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractSynchronizedClips()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "BSSynchronizedClipGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: BSSynchronizedClipGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append($"pClipGenerator: {Ref(P(obj, "pClipGenerator"))}\n");
                sb.Append($"SyncAnimPrefix: {P(obj, "SyncAnimPrefix")}\n");
                sb.Append(Bindings(obj));
                var skip = new HashSet<string> { "variableBindingSet","userData","name","pClipGenerator","SyncAnimPrefix" };
                foreach (var p in obj.Elements("hkparam"))
                {
                    var pname = (string?)p.Attribute("name");
                    if (pname is null || skip.Contains(pname)) continue;
                    var v = p.Value.Trim();
                    if (!string.IsNullOrEmpty(v)) sb.Append($"{pname}: {v}\n");
                }
                Write(Path.Combine(_out, "generators", SafeName(n) + ".yaml"), sb);
                count++;
            }
            return count;
        }

        private void ExtractOffsetAnimationGenerators()
        {
            foreach (var obj in _all.Where(o => Cls(o) == "BSOffsetAnimationGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: BSOffsetAnimationGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                var dg = P(obj, "pDefaultGenerator");
                if (dg is not null) sb.Append($"pDefaultGenerator: {Ref(dg)}\n");
                var oc = P(obj, "pOffsetClipGenerator");
                if (oc is not null) sb.Append($"pOffsetClipGenerator: {Ref(oc)}\n");
                OptFloat(sb, obj, "fOffsetVariable",   alwaysEmit: true);
                OptFloat(sb, obj, "fOffsetRangeStart", alwaysEmit: true);
                OptFloat(sb, obj, "fOffsetRangeEnd",   alwaysEmit: true);
                sb.Append(Bindings(obj));
                Write(Path.Combine(_out, "generators", SafeName(n) + ".yaml"), sb);
            }
        }

        private void ExtractPoseMatchingGenerators()
        {
            foreach (var obj in _all.Where(o => Cls(o) == "hkbPoseMatchingGenerator"))
            {
                var n = P(obj, "name") ?? "";
                var sb = new StringBuilder();
                sb.Append("class: hkbPoseMatchingGenerator\n");
                sb.Append($"name: {n}\n");
                OptInt(sb, obj, "userData");
                sb.Append(Bindings(obj));

                foreach (var param in new[] {
                    "referencePoseWeightThreshold","blendParameter",
                    "minCyclicBlendParameter","maxCyclicBlendParameter" })
                    OptFloat(sb, obj, param, alwaysEmit: true);

                var ismc = P(obj, "indexOfSyncMasterChild");
                if (ismc is not null) sb.Append($"indexOfSyncMasterChild: {ismc}\n");
                var fl = P(obj, "flags");
                if (fl is not null) sb.Append($"flags: {fl}\n");
                var slc = P(obj, "subtractLastChild");
                if (slc is not null) sb.Append($"subtractLastChild: {slc}\n");

                var childRefs = RefList(P(obj, "children"));
                if (childRefs.Count > 0)
                {
                    sb.Append("children:\n");
                    foreach (var cref in childRefs)
                    {
                        if (!_byId.TryGetValue(cref, out var co))
                        { sb.Append("  - generator: MISSING\n"); continue; }
                        sb.Append($"  - generator: {Ref(P(co, "generator"))}\n");
                        sb.Append($"    weight: {Fmt(P(co, "weight") ?? "1.000000")}\n");
                        sb.Append($"    worldFromModelWeight: {Fmt(P(co, "worldFromModelWeight") ?? "0.000000")}\n");
                        sb.Append(BoneWeightsYaml(P(co, "boneWeights")));
                    }
                }

                foreach (var param in new[] {
                    "worldFromModelRotation","blendSpeed","minSpeedToSwitch",
                    "minSwitchTimeNoError","minSwitchTimeFullError" })
                {
                    var v = P(obj, param);
                    if (v is not null) sb.Append($"{param}: {(double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? Fmt(v) : v)}\n");
                }

                OptEvent(sb, obj, "startPlayingEventId",  "startPlayingEvent");
                OptEvent(sb, obj, "startMatchingEventId", "startMatchingEvent");
                foreach (var param in new[] { "rootBoneIndex","otherBoneIndex","anotherBoneIndex","pelvisIndex" })
                { var v = P(obj, param); if (v is not null) sb.Append($"{param}: {v}\n"); }
                var mode = P(obj, "mode");
                if (mode is not null) sb.Append($"mode: {mode}\n");

                Write(Path.Combine(_out, "generators", SafeName(n) + ".yaml"), sb);
            }
        }

        // ── data/*.yaml ───────────────────────────────────────────────────── //
        private int ExtractExpressionDataArrays()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbExpressionDataArray"))
            {
                var edaId = (string?)obj.Attribute("name") ?? "";
                string? ownerName = null;
                foreach (var m in _all.Where(o => Cls(o) == "hkbEvaluateExpressionModifier"))
                    if (P(m, "expressions") == edaId) { ownerName = P(m, "name"); break; }
                ownerName ??= $"expressionData_{edaId}";

                var eds = PNode(obj, "expressionsData")?.Elements("hkobject").ToList() ?? [];
                var sb  = new StringBuilder();
                sb.Append("class: hkbExpressionDataArray\n");
                sb.Append($"name: {ownerName}\n");
                if (eds.Count > 0)
                {
                    sb.Append("expressionsData:\n");
                    foreach (var ed in eds)
                    {
                        sb.Append($"  - expression: \"{P(ed, "expression")}\"\n");
                        var avi = P(ed, "assignmentVariableIndex");
                        if (avi is not null && int.TryParse(avi, out int a) && a != -1)
                        { if (_varByIdx.TryGetValue(a, out var vn)) sb.Append($"    assignmentVariable: {vn}\n"); else sb.Append($"    assignmentVariableIndex: {a}\n"); }
                        var aei = P(ed, "assignmentEventIndex");
                        if (aei is not null && int.TryParse(aei, out int ae) && ae != -1)
                        { if (_evByIdx.TryGetValue(ae, out var en)) sb.Append($"    assignmentEvent: {en}\n"); else sb.Append($"    assignmentEventIndex: {ae}\n"); }
                        var evMode = P(ed, "eventMode");
                        if (evMode is not null && evMode != "EVENT_MODE_SEND_ONCE") sb.Append($"    eventMode: {evMode}\n");
                    }
                }
                Write(Path.Combine(_out, "data", SafeName(ownerName) + "_expressions.yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractEventRangeDataArrays()
        {
            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbEventRangeDataArray"))
            {
                var erdId = (string?)obj.Attribute("name") ?? "";
                string? ownerName = null;
                foreach (var m in _all.Where(o => Cls(o) == "hkbEventsFromRangeModifier"))
                    if (P(m, "eventRanges") == erdId) { ownerName = P(m, "name"); break; }
                ownerName ??= $"eventRangeData_{erdId}";

                var eds = PNode(obj, "eventData")?.Elements("hkobject").ToList() ?? [];
                var sb  = new StringBuilder();
                sb.Append("class: hkbEventRangeDataArray\n");
                sb.Append($"name: {ownerName}\n");
                if (eds.Count > 0)
                {
                    sb.Append("eventData:\n");
                    foreach (var ed in eds)
                    {
                        var evInner = PNode(ed, "event")?.Element("hkobject");
                        var ep = evInner is not null ? EventProp(evInner) : (null, -1, (string?)null);
                        sb.Append($"  - upperBound: {Fmt(P(ed, "upperBound"))}\n");
                        if (ep.Name is not null) sb.Append($"    event: {ep.Name}\n"); else sb.Append($"    eventId: {ep.Id}\n");
                        if (ep.Payload is not null) sb.Append($"    payload: {ep.Payload}\n");
                        var evMode = P(ed, "eventMode");
                        if (evMode is not null && evMode != "EVENT_MODE_SEND_ONCE") sb.Append($"    eventMode: {evMode}\n");
                    }
                }
                Write(Path.Combine(_out, "data", SafeName(ownerName) + "_ranges.yaml"), sb);
                count++;
            }
            return count;
        }

        private int ExtractBoneIndexArrays()
        {
            var ownerLookup = new Dictionary<string, string>();
            foreach (var modObj in _all.Where(o =>
                (Cls(o) ?? "").Contains("Modifier") ||
                Cls(o) == "hkbGetUpModifier" || (Cls(o) ?? "").Contains("hkbFootIk")))
            {
                foreach (var p in modObj.Elements("hkparam"))
                {
                    var pText = p.Value.Trim();
                    if (pText.StartsWith('#') && _byId.TryGetValue(pText, out var rObj) &&
                        Cls(rObj) == "hkbBoneIndexArray")
                        ownerLookup[pText] = $"{P(modObj, "name") ?? ""}_{(string?)p.Attribute("name") ?? ""}";
                }
            }

            int count = 0;
            foreach (var obj in _all.Where(o => Cls(o) == "hkbBoneIndexArray"))
            {
                var biaId    = (string?)obj.Attribute("name") ?? "";
                var ownerKey = ownerLookup.TryGetValue(biaId, out var ok) ? ok : $"boneIndexArray_{biaId}";
                var biText   = P(obj, "boneIndices");
                var sb       = new StringBuilder();
                sb.Append("class: hkbBoneIndexArray\n");
                sb.Append($"name: {ownerKey}\n");
                if (!string.IsNullOrWhiteSpace(biText))
                {
                    var vals = biText.Trim().Split(new char[]{' ','\t','\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
                    if (_boneNames.Count > 0)
                    {
                        sb.Append("boneIndices:\n");
                        foreach (var v in vals)
                        {
                            if (int.TryParse(v, out int idx) && _boneByIdx.TryGetValue(idx, out var bn))
                                sb.Append($"  - \"{bn}\"\n");
                            else sb.Append($"  - bone{v}\n");
                        }
                    }
                    else sb.Append($"boneIndices: {biText.Trim()}\n");
                }
                else sb.Append("boneIndices: []\n");
                Write(Path.Combine(_out, "data", SafeName(ownerKey) + "_boneIndex.yaml"), sb);
                count++;
            }
            return count;
        }

        // ── Helpers ────────────────────────────────────────────────────────── //

        private string Ref(string? r) =>
            r is null || r == "null" ? "null"
            : _nameOf.TryGetValue(r, out var n) ? n : "null";

        private string Bindings(XElement obj)
        {
            var bsRef = P(obj, "variableBindingSet");
            if (bsRef is null || bsRef == "null") return "";
            if (!_byId.TryGetValue(bsRef, out var bsObj)) return "";
            var binds = PNode(bsObj, "bindings")?.Elements("hkobject").ToList() ?? [];
            if (binds.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append("bindings:\n");
            foreach (var b in binds)
            {
                var vi = int.TryParse(P(b, "variableIndex"), out int vi2) ? vi2 : 0;
                var bt = P(b, "bindingType") ?? "";
                var bi = int.TryParse(P(b, "bitIndex"), out int bi2) ? bi2 : -1;
                sb.Append($"  - memberPath: {P(b, "memberPath")}\n");
                if (_varByIdx.TryGetValue(vi, out var vn)) sb.Append($"    variable: {vn}\n");
                else sb.Append($"    variableIndex: {vi}\n");
                if (bt != "BINDING_TYPE_VARIABLE") sb.Append($"    bindingType: {bt}\n");
                if (bi != -1) sb.Append($"    bitIndex: {bi}\n");
            }
            return sb.ToString();
        }

        private string BoneWeightsYaml(string? bwRef)
        {
            if (bwRef is null || bwRef == "null") return "";
            if (!_byId.TryGetValue(bwRef, out var bwObj)) return "";
            var bwText = P(bwObj, "boneWeights");
            if (string.IsNullOrWhiteSpace(bwText)) return "";
            var vals = bwText.Trim().Split(new char[]{' ','\t','\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            sb.Append("    boneWeights:\n      named:\n");
            for (int i = 0; i < vals.Length; i++)
            {
                if (!double.TryParse(vals[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) || w == 0.0) continue;
                var bn = _boneByIdx.TryGetValue(i, out var name) ? name : $"bone{i}";
                sb.Append($"        \"{bn}\": {Fmt(vals[i])}\n");
            }
            return sb.ToString();
        }

        private (string? Name, int Id, string? Payload) EventProp(XElement evObj)
        {
            var eid    = int.TryParse(P(evObj, "id"), out int id) ? id : -1;
            var payRef = P(evObj, "payload");
            var evName = _evByIdx.TryGetValue(eid, out var n) ? n : null;
            string? payload = null;
            if (payRef is not null && payRef != "null" && _byId.TryGetValue(payRef, out var payObj))
                payload = P(payObj, "data");
            return (evName, eid, payload);
        }

        private string NotifyEvents(XElement obj, string paramName)
        {
            var eaRef = P(obj, paramName);
            if (eaRef is null || eaRef == "null") return "";
            if (!_byId.TryGetValue(eaRef, out var eaObj)) return "";
            var evs = PNode(eaObj, "events")?.Elements("hkobject").ToList() ?? [];
            if (evs.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append($"{paramName}:\n");
            foreach (var ev in evs)
            {
                var ep = EventProp(ev);
                if (ep.Name is not null) sb.Append($"  - event: {ep.Name}\n");
                else sb.Append($"  - eventId: {ep.Id}\n");
                if (ep.Payload is not null) sb.Append($"    payload: {ep.Payload}\n");
            }
            return sb.ToString();
        }

        private string InlineEventYaml(XElement obj, string paramName, string indent)
        {
            var inner = PNode(obj, paramName)?.Element("hkobject");
            if (inner is null) return "";
            var eid    = int.TryParse(P(inner, "id"), out int id) ? id : -1;
            var payRef = P(inner, "payload");
            var evName = _evByIdx.TryGetValue(eid, out var n) ? n : null;
            var sb = new StringBuilder();
            sb.Append($"{indent}{paramName}:\n");
            if (eid == -1) sb.Append($"{indent}  id: -1\n");
            else if (evName is not null) sb.Append($"{indent}  event: {evName}\n");
            else sb.Append($"{indent}  id: {eid}\n");
            if (payRef is not null && payRef != "null" && _byId.TryGetValue(payRef, out var payObj))
            {
                var pd = P(payObj, "data");
                if (pd is not null) sb.Append($"{indent}  payload: {pd}\n");
            }
            return sb.ToString();
        }

        private string Transitions(XElement obj, string paramName, string? ownerSM)
        {
            var transRef = P(obj, paramName);
            if (transRef is null || transRef == "null") return "";
            if (!_byId.TryGetValue(transRef, out var transObj)) return "";
            var transList = PNode(transObj, "transitions")?.Elements("hkobject").ToList() ?? [];
            if (transList.Count == 0) return "";

            var stateMap = ownerSM is not null && _smStates.TryGetValue(ownerSM, out var sm)
                           ? sm : new Dictionary<int, string>();
            var sb = new StringBuilder();
            sb.Append("transitions:\n");
            foreach (var t in transList)
            {
                var tEvId       = int.TryParse(P(t, "eventId"),           out int tei)  ? tei  : -1;
                var tToSId      = int.TryParse(P(t, "toStateId"),         out int ttsi) ? ttsi : 0;
                var tTransRef   = P(t, "transition");
                var tFlags      = P(t, "flags");
                var tFromNested = int.TryParse(P(t, "fromNestedStateId"), out int tfn)  ? tfn  : 0;
                var tToNested   = int.TryParse(P(t, "toNestedStateId"),   out int ttn)  ? ttn  : 0;
                var tPrio       = int.TryParse(P(t, "priority"),          out int tp)   ? tp   : 0;
                var tCondRef    = P(t, "condition");

                var toStateName = stateMap.TryGetValue(tToSId, out var tsn) ? tsn : null;
                var evName      = _evByIdx.TryGetValue(tEvId,  out var en)  ? en  : null;

                if (evName is not null) sb.Append($"  - event: {evName}\n");
                else sb.Append($"  - eventId: {tEvId}\n");
                if (toStateName is not null) sb.Append($"    toState: {toStateName}\n");
                else sb.Append($"    toStateId: {tToSId}\n");
                sb.Append($"    transition: {Ref(tTransRef)}\n");
                if (!IsDefault(tFlags)) sb.Append($"    flags: {tFlags}\n");
                if (tFromNested != 0) sb.Append($"    fromNestedStateId: {tFromNested}\n");
                if (tToNested   != 0) sb.Append($"    toNestedStateId: {tToNested}\n");
                if (tPrio       != 0) sb.Append($"    priority: {tPrio}\n");

                foreach (var intName in new[] { "triggerInterval", "initiateInterval" })
                {
                    var intNode = PNode(t, intName)?.Element("hkobject");
                    if (intNode is null) continue;
                    var eeid = int.TryParse(P(intNode, "enterEventId"), out int ee) ? ee : -1;
                    var xeid = int.TryParse(P(intNode, "exitEventId"),  out int xe) ? xe : -1;
                    var et   = P(intNode, "enterTime");
                    var xt   = P(intNode, "exitTime");
                    bool etNZ = et is not null && double.TryParse(et, NumberStyles.Float, CultureInfo.InvariantCulture, out double etd) && etd != 0.0;
                    bool xtNZ = xt is not null && double.TryParse(xt, NumberStyles.Float, CultureInfo.InvariantCulture, out double xtd) && xtd != 0.0;
                    if (eeid == -1 && xeid == -1 && !etNZ && !xtNZ) continue;
                    sb.Append($"    {intName}:\n");
                    if (eeid != -1) { if (_evByIdx.TryGetValue(eeid, out var een)) sb.Append($"      enterEvent: {een}\n"); else sb.Append($"      enterEventId: {eeid}\n"); }
                    if (xeid != -1) { if (_evByIdx.TryGetValue(xeid, out var xen)) sb.Append($"      exitEvent: {xen}\n");  else sb.Append($"      exitEventId: {xeid}\n"); }
                    if (etNZ) sb.Append($"      enterTime: {Fmt(et)}\n");
                    if (xtNZ) sb.Append($"      exitTime: {Fmt(xt)}\n");
                }

                if (tCondRef is not null && tCondRef != "null" && _byId.TryGetValue(tCondRef, out var condObj))
                {
                    if (Cls(condObj) == "hkbExpressionCondition")
                        sb.Append($"    condition: \"{P(condObj, "expression")}\"\n");
                    else if (Cls(condObj) == "hkbStringCondition")
                        sb.Append($"    conditionString: \"{P(condObj, "conditionString")}\"\n");
                }
            }
            return sb.ToString();
        }

        private void OptEvent(StringBuilder sb, XElement obj, string idParamName, string namedKey)
        {
            var raw = P(obj, idParamName);
            if (raw is null || !int.TryParse(raw, out int eid) || eid == -1) return;
            if (_evByIdx.TryGetValue(eid, out var en)) sb.Append($"{namedKey}: {en}\n");
            else sb.Append($"{idParamName}: {eid}\n");
        }

        private static void OptInt(StringBuilder sb, XElement obj, string param,
                                   int defaultVal = 0, string? key = null)
        {
            var v = P(obj, param);
            if (v is null || (int.TryParse(v, out int i) && i == defaultVal)) return;
            sb.Append($"{key ?? param}: {v}\n");
        }

        private static void OptFloat(StringBuilder sb, XElement obj, string param,
                                     double defaultVal = 0.0, string? key = null,
                                     bool alwaysEmit = false)
        {
            var v = P(obj, param);
            if (v is null) return;
            if (!alwaysEmit && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == defaultVal) return;
            sb.Append($"{key ?? param}: {Fmt(v)}\n");
        }

        private string? FindOwnerSM(string stateName)
        {
            foreach (var (smName, map) in _smStates)
                if (map.ContainsValue(stateName)) return smName;
            return null;
        }

        // ── Static helpers ──────────────────────────────────────────────── //
        private static string? P(XElement obj, string pname) =>
            obj.Elements("hkparam")
               .FirstOrDefault(e => (string?)e.Attribute("name") == pname)
               ?.Value.Trim();

        private static XElement? PNode(XElement obj, string pname) =>
            obj.Elements("hkparam")
               .FirstOrDefault(e => (string?)e.Attribute("name") == pname);

        private static string? Cls(XElement? obj) => (string?)obj?.Attribute("class");

        private static string Fmt(string? v)
        {
            if (v is null) return "0.000000";
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d.ToString("F6", CultureInfo.InvariantCulture) : "0.000000";
        }

        private static string Bool(string? v) => v == "true" ? "true" : "false";

        private static string SafeName(string name) =>
            Regex.Replace(name, @"[<>:""/\\|?*]", "_");

        private static bool IsDefault(string? v) =>
            v is null || v == "0" || v == "FLAG_NONE" || v == "FLAG_DISABLE_CONDITION";

        private static List<string> RefList(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];
            return [.. text.Trim()
                           .Split(new char[]{' ','\t','\r','\n'}, StringSplitOptions.RemoveEmptyEntries)
                           .Where(r => r.StartsWith('#'))];
        }

        private static void Write(string path, StringBuilder sb)
        {
            File.WriteAllText(path, sb.ToString().TrimEnd(),
                              new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// Locate and load skeleton bone names for the behavior being extracted.
        ///
        /// Search order:
        ///   1. Walk up the output directory tree looking for a sibling
        ///      "character assets/skeleton.yaml" (same logic as SkeletonReader.FindAndLoad).
        ///      This resolves creature skeletons automatically — e.g. extracting to
        ///      behavior_src/_vanilla/dragon/behaviors/dragonbehavior.hkx/ will find
        ///      behavior_src/_vanilla/dragon/character assets/skeleton.yaml.
        ///   2. Fall back to the hardcoded human skeleton path for backwards compatibility.
        /// </summary>
        private static List<string> LoadBoneNames(string outputDir)
        {
            // Walk up the output directory tree.
            var dir = Path.GetFullPath(outputDir);
            while (dir != null)
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent == null) break;

                var candidate = Path.Combine(parent, "character assets", "skeleton.yaml");
                if (File.Exists(candidate))
                    return ParseBoneNames(candidate);

                dir = parent;
            }

            // Fallback: hardcoded human skeleton (covers extraction without an out-base).
            var fallbacks = new[]
            {
                Path.Combine("behavior_src", "vanilla", "character", "character assets", "skeleton.yaml"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                    "behavior_src", "vanilla", "character", "character assets", "skeleton.yaml"),
            };
            foreach (var path in fallbacks)
            {
                if (File.Exists(path)) return ParseBoneNames(path);
            }

            return [];
        }

        private static List<string> ParseBoneNames(string path) =>
            [.. Regex.Matches(File.ReadAllText(path), "- \"([^\"]+)\"")
                     .Select(m => m.Groups[1].Value)];
    }
}
