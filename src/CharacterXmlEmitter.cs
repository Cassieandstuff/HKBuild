using System.Globalization;
using System.Text;
using HKBuild.Models;

namespace HKBuild;

/// <summary>
/// Assigns hkobject IDs and emits Havok packfile XML from loaded CharacterData.
///
/// ID assignment order (matches vanilla defaultmale.xml):
///   #0001 = hkRootLevelContainer
///   #0002 = hkbCharacterData
///   #0003 = hkbVariableValueSet
///   #0004..#NNNN = hkbBoneWeightArray (one per POINTER property, in property order)
///   next  = hkbFootIkDriverInfo
///   next  = hkbCharacterStringData
///   next  = hkbMirroredSkeletonInfo
/// </summary>
public class CharacterXmlEmitter
{
    private readonly CharacterData _data;

    // Assigned IDs.
    private string _idRoot = "";
    private string _idCharData = "";
    private string _idVarValues = "";
    private string _idFootIk = "";
    private string _idStringData = "";
    private string _idMirror = "";
    private readonly List<string> _idBoneWeights = [];

    // Class signatures (Havok hk_2010.2.0-r1).
    private const string SigRootLevelContainer = "0x2772c11e";
    private const string SigCharacterData = "0x300d6808";
    private const string SigVariableValueSet = "0x27812d8d";
    private const string SigBoneWeightArray = "0xcd902b77";
    private const string SigFootIkDriverInfo = "0xc6a09dbf";
    private const string SigCharStringData = "0x655b42bc";
    private const string SigMirroredSkeletonInfo = "0xc6c2da4f";

    public CharacterXmlEmitter(CharacterData data)
    {
        _data = data;
    }

    public string Emit()
    {
        AssignIds();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"ascii\"?>");
        sb.AppendLine($"<hkpackfile classversion=\"{_data.Character.Packfile.ClassVersion}\" contentsversion=\"{_data.Character.Packfile.ContentsVersion}\" toplevelobject=\"{_idRoot}\">");
        sb.AppendLine();
        sb.AppendLine("\t<hksection name=\"__data__\">");
        sb.AppendLine();

        // Match vanilla emission order: Root → CharData → VarValues → BoneWeights → FootIk → StringData → Mirror
        EmitRootLevelContainer(sb);
        EmitCharacterData(sb);
        EmitVariableValueSet(sb);
        EmitBoneWeightArrays(sb);
        EmitFootIkDriverInfo(sb);
        EmitCharacterStringData(sb);
        EmitMirroredSkeletonInfo(sb);

        sb.AppendLine("\t</hksection>");
        sb.AppendLine();
        sb.AppendLine("</hkpackfile>");

        return sb.ToString();
    }

    private void AssignIds()
    {
        // Match vanilla object order: Root → CharData → VarValues → BoneWeights → FootIk → StringData → Mirror
        int nextId = 1;
        _idRoot = Id(nextId++);        // hkRootLevelContainer
        _idCharData = Id(nextId++);    // hkbCharacterData
        _idVarValues = Id(nextId++);   // hkbVariableValueSet

        // Bone weight arrays: one per POINTER property, in property order.
        foreach (var prop in _data.Properties)
        {
            if (prop.IsPointer)
                _idBoneWeights.Add(Id(nextId++));
        }

        _idFootIk = Id(nextId++);
        _idStringData = Id(nextId++);
        _idMirror = Id(nextId++);

        Console.WriteLine($"  Assigned IDs: #0001..#{nextId - 1:D4} ({nextId - 1} objects)");
    }

    private static string Id(int n) => $"#{n:D4}";

    private static string F(float v)
    {
        // Preserve negative zero for vanilla-exact output.
        if (v == 0f && float.IsNegative(v))
            return "-0.000000";
        return v.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string Vec4(IList<float> v) =>
        $"({F(v[0])} {F(v[1])} {F(v[2])} {F(v[3])})";

    private void EmitBoneWeightArrays(StringBuilder sb)
    {
        int bwIndex = 0;
        foreach (var prop in _data.Properties)
        {
            if (!prop.IsPointer) continue;

            var id = _idBoneWeights[bwIndex++];
            var bw = prop.BoneWeights;

            // Resolve named bone weights if available.
            int count;
            string? values;
            if (bw != null && bw.IsNamed)
            {
                if (_data.BoneNames == null)
                    throw new InvalidOperationException(
                        "Named bone weights used but no skeleton.yaml found.");
                (count, values) = bw.Resolve(_data.BoneNames);
            }
            else
            {
                count = bw?.Count ?? 0;
                values = bw?.Values;
            }

            sb.AppendLine($"\t\t<hkobject name=\"{id}\" class=\"hkbBoneWeightArray\" signature=\"{SigBoneWeightArray}\">");
            sb.AppendLine("\t\t\t<!-- memSizeAndFlags SERIALIZE_IGNORED -->");
            sb.AppendLine("\t\t\t<!-- referenceCount SERIALIZE_IGNORED -->");
            sb.AppendLine("\t\t\t<hkparam name=\"variableBindingSet\">null</hkparam>");
            sb.AppendLine("\t\t\t<!-- cachedBindables SERIALIZE_IGNORED -->");
            sb.AppendLine("\t\t\t<!-- areBindablesCached SERIALIZE_IGNORED -->");

            if (count == 0 || bw == null || string.IsNullOrWhiteSpace(values))
            {
                sb.AppendLine($"\t\t\t<hkparam name=\"boneWeights\" numelements=\"0\"></hkparam>");
            }
            else
            {
                sb.AppendLine($"\t\t\t<hkparam name=\"boneWeights\" numelements=\"{count}\">");
                EmitFloatArray(sb, values, count);
                sb.AppendLine("\t\t\t</hkparam>");
            }

            sb.AppendLine("\t\t</hkobject>");
            sb.AppendLine();
        }
    }

    private void EmitVariableValueSet(StringBuilder sb)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{_idVarValues}\" class=\"hkbVariableValueSet\" signature=\"{SigVariableValueSet}\">");
        sb.AppendLine("\t\t\t<!-- memSizeAndFlags SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<!-- referenceCount SERIALIZE_IGNORED -->");

        // wordVariableValues: one entry per property.
        // For scalar types: the initial_value.
        // For POINTER types: the index into the variant array.
        int propCount = _data.Properties.Count;
        sb.AppendLine($"\t\t\t<hkparam name=\"wordVariableValues\" numelements=\"{propCount}\">");

        int pointerIndex = 0;
        foreach (var prop in _data.Properties)
        {
            long value;
            if (prop.IsPointer)
                value = pointerIndex++;
            else
                value = prop.InitialValue ?? 0;

            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"value\">{value}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }

        sb.AppendLine("\t\t\t</hkparam>");

        // quadVariableValues: always empty for characters.
        sb.AppendLine("\t\t\t<hkparam name=\"quadVariableValues\" numelements=\"0\"></hkparam>");

        // variantVariableValues: references to bone weight arrays.
        int bwCount = _idBoneWeights.Count;
        sb.AppendLine($"\t\t\t<hkparam name=\"variantVariableValues\" numelements=\"{bwCount}\">");

        // Emit as space-separated ID refs, 16 per line.
        var bwRefs = new StringBuilder("\t\t\t\t");
        for (int i = 0; i < bwCount; i++)
        {
            if (i > 0 && i % 16 == 0)
            {
                sb.AppendLine(bwRefs.ToString());
                bwRefs.Clear().Append("\t\t\t\t");
            }
            else if (i > 0)
            {
                bwRefs.Append(' ');
            }
            bwRefs.Append(_idBoneWeights[i]);
        }
        sb.AppendLine(bwRefs.ToString());

        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitFootIkDriverInfo(StringBuilder sb)
    {
        var ik = _data.FootIk;

        sb.AppendLine($"\t\t<hkobject name=\"{_idFootIk}\" class=\"hkbFootIkDriverInfo\" signature=\"{SigFootIkDriverInfo}\">");
        sb.AppendLine("\t\t\t<!-- memSizeAndFlags SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<!-- referenceCount SERIALIZE_IGNORED -->");
        sb.AppendLine($"\t\t\t<hkparam name=\"legs\" numelements=\"{ik.Legs.Count}\">");

        foreach (var leg in ik.Legs)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine("\t\t\t\t\t<!-- prevAnkleRotLS SERIALIZE_IGNORED -->");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"kneeAxisLS\">{Vec4(leg.KneeAxisLS)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"footEndLS\">{Vec4(leg.FootEndLS)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"footPlantedAnkleHeightMS\">{F(leg.FootPlantedAnkleHeightMS)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"footRaisedAnkleHeightMS\">{F(leg.FootRaisedAnkleHeightMS)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"maxAnkleHeightMS\">{F(leg.MaxAnkleHeightMS)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"minAnkleHeightMS\">{F(leg.MinAnkleHeightMS)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"maxKneeAngleDegrees\">{F(leg.MaxKneeAngleDegrees)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"minKneeAngleDegrees\">{F(leg.MinKneeAngleDegrees)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"maxAnkleAngleDegrees\">{F(leg.MaxAnkleAngleDegrees)}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"hipIndex\">{leg.HipIndex}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"kneeIndex\">{leg.KneeIndex}</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"ankleIndex\">{leg.AnkleIndex}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }

        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"raycastDistanceUp\">{F(ik.RaycastDistanceUp)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"raycastDistanceDown\">{F(ik.RaycastDistanceDown)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"originalGroundHeightMS\">{F(ik.OriginalGroundHeightMS)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"verticalOffset\">{F(ik.VerticalOffset)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"collisionFilterInfo\">{ik.CollisionFilterInfo}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"forwardAlignFraction\">{F(ik.ForwardAlignFraction)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"sidewaysAlignFraction\">{F(ik.SidewaysAlignFraction)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"sidewaysSampleWidth\">{F(ik.SidewaysSampleWidth)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"lockFeetWhenPlanted\">{Bool(ik.LockFeetWhenPlanted)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"useCharacterUpVector\">{Bool(ik.UseCharacterUpVector)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"isQuadrupedNarrow\">{Bool(ik.IsQuadrupedNarrow)}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitCharacterStringData(StringBuilder sb)
    {
        var ch = _data.Character.Character;

        sb.AppendLine($"\t\t<hkobject name=\"{_idStringData}\" class=\"hkbCharacterStringData\" signature=\"{SigCharStringData}\">");
        sb.AppendLine("\t\t\t<!-- memSizeAndFlags SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<!-- referenceCount SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<hkparam name=\"deformableSkinNames\" numelements=\"0\"></hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"rigidSkinNames\" numelements=\"0\"></hkparam>");

        // Animation names.
        sb.AppendLine($"\t\t\t<hkparam name=\"animationNames\" numelements=\"{_data.Animations.Count}\">");
        foreach (var anim in _data.Animations)
            sb.AppendLine($"\t\t\t\t<hkcstring>{anim}</hkcstring>");
        sb.AppendLine("\t\t\t</hkparam>");

        sb.AppendLine("\t\t\t<hkparam name=\"animationFilenames\" numelements=\"0\"></hkparam>");

        // Character property names.
        sb.AppendLine($"\t\t\t<hkparam name=\"characterPropertyNames\" numelements=\"{_data.Properties.Count}\">");
        foreach (var prop in _data.Properties)
            sb.AppendLine($"\t\t\t\t<hkcstring>{prop.Name}</hkcstring>");
        sb.AppendLine("\t\t\t</hkparam>");

        sb.AppendLine("\t\t\t<hkparam name=\"retargetingSkeletonMapperFilenames\" numelements=\"0\"></hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"lodNames\" numelements=\"0\"></hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"mirroredSyncPointSubstringsA\" numelements=\"0\"></hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"mirroredSyncPointSubstringsB\" numelements=\"0\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"name\">{ch.Name}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"rigName\">{ch.Rig}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"ragdollName\">{ch.Ragdoll}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"behaviorFilename\">{ch.Behavior}</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitMirroredSkeletonInfo(StringBuilder sb)
    {
        var m = _data.Mirror;

        sb.AppendLine($"\t\t<hkobject name=\"{_idMirror}\" class=\"hkbMirroredSkeletonInfo\" signature=\"{SigMirroredSkeletonInfo}\">");
        sb.AppendLine("\t\t\t<!-- memSizeAndFlags SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<!-- referenceCount SERIALIZE_IGNORED -->");
        sb.AppendLine($"\t\t\t<hkparam name=\"mirrorAxis\">{Vec4(m.MirrorAxis)}</hkparam>");

        // Resolve named bone pair map if skeleton is available.
        int pairCount = m.BonePairMap.Count;
        string pairValues = m.BonePairMap.Values;
        if (m.BonePairMap.IsNamed)
        {
            if (_data.BoneNames == null)
                throw new InvalidOperationException(
                    "Named bone pair map used but no skeleton.yaml found.");
            (pairCount, pairValues) = m.BonePairMap.Resolve(_data.BoneNames);
        }

        sb.AppendLine($"\t\t\t<hkparam name=\"bonePairMap\" numelements=\"{pairCount}\">");
        EmitIntArray(sb, pairValues, pairCount);
        sb.AppendLine("\t\t\t</hkparam>");

        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitCharacterData(StringBuilder sb)
    {
        var ch = _data.Character.Character;

        sb.AppendLine($"\t\t<hkobject name=\"{_idCharData}\" class=\"hkbCharacterData\" signature=\"{SigCharacterData}\">");
        sb.AppendLine("\t\t\t<!-- memSizeAndFlags SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<!-- referenceCount SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<hkparam name=\"characterControllerInfo\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"capsuleHeight\">{F(ch.Controller.CapsuleHeight)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"capsuleRadius\">{F(ch.Controller.CapsuleRadius)}</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"collisionFilterInfo\">{ch.Controller.CollisionFilterInfo}</hkparam>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"characterControllerCinfo\">null</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"modelUpMS\">{Vec4(ch.Model.Up)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"modelForwardMS\">{Vec4(ch.Model.Forward)}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"modelRightMS\">{Vec4(ch.Model.Right)}</hkparam>");

        // characterPropertyInfos — parallel to characterPropertyNames.
        sb.AppendLine($"\t\t\t<hkparam name=\"characterPropertyInfos\" numelements=\"{_data.Properties.Count}\">");
        foreach (var prop in _data.Properties)
        {
            sb.AppendLine("\t\t\t\t<hkobject>");
            sb.AppendLine("\t\t\t\t\t<hkparam name=\"role\">");
            sb.AppendLine("\t\t\t\t\t\t<hkobject>");
            sb.AppendLine($"\t\t\t\t\t\t\t<hkparam name=\"role\">{prop.Role}</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t\t<hkparam name=\"flags\">0</hkparam>");
            sb.AppendLine("\t\t\t\t\t\t</hkobject>");
            sb.AppendLine("\t\t\t\t\t</hkparam>");
            sb.AppendLine($"\t\t\t\t\t<hkparam name=\"type\">{prop.Type}</hkparam>");
            sb.AppendLine("\t\t\t\t</hkobject>");
        }
        sb.AppendLine("\t\t\t</hkparam>");

        sb.AppendLine("\t\t\t<hkparam name=\"numBonesPerLod\" numelements=\"0\"></hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"characterPropertyValues\">{_idVarValues}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"footIkDriverInfo\">{_idFootIk}</hkparam>");
        sb.AppendLine("\t\t\t<hkparam name=\"handIkDriverInfo\">null</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"stringData\">{_idStringData}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"mirroredSkeletonInfo\">{_idMirror}</hkparam>");
        sb.AppendLine($"\t\t\t<hkparam name=\"scale\">{F(ch.Scale)}</hkparam>");
        sb.AppendLine("\t\t\t<!-- numHands SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t\t<!-- numFloatSlots SERIALIZE_IGNORED -->");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    private void EmitRootLevelContainer(StringBuilder sb)
    {
        sb.AppendLine($"\t\t<hkobject name=\"{_idRoot}\" class=\"hkRootLevelContainer\" signature=\"{SigRootLevelContainer}\">");
        sb.AppendLine("\t\t\t<hkparam name=\"namedVariants\" numelements=\"1\">");
        sb.AppendLine("\t\t\t\t<hkobject>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"name\">hkbCharacterData</hkparam>");
        sb.AppendLine("\t\t\t\t\t<hkparam name=\"className\">hkbCharacterData</hkparam>");
        sb.AppendLine($"\t\t\t\t\t<hkparam name=\"variant\">{_idCharData}</hkparam>");
        sb.AppendLine("\t\t\t\t</hkobject>");
        sb.AppendLine("\t\t\t</hkparam>");
        sb.AppendLine("\t\t</hkobject>");
        sb.AppendLine();
    }

    /// <summary>Emit float values in rows of 16, matching vanilla formatting.</summary>
    private static void EmitFloatArray(StringBuilder sb, string values, int count)
    {
        var floats = values.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (floats.Length != count)
            Console.Error.WriteLine($"  WARNING: expected {count} float values, got {floats.Length}");

        for (int i = 0; i < floats.Length; i += 16)
        {
            var row = floats.Skip(i).Take(16);
            sb.AppendLine($"\t\t\t\t{string.Join(' ', row)}");
        }
    }

    /// <summary>Emit integer values in rows of 16, matching vanilla formatting.</summary>
    private static void EmitIntArray(StringBuilder sb, string values, int count)
    {
        var ints = values.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ints.Length != count)
            Console.Error.WriteLine($"  WARNING: expected {count} int values, got {ints.Length}");

        for (int i = 0; i < ints.Length; i += 16)
        {
            var row = ints.Skip(i).Take(16);
            sb.AppendLine($"\t\t\t\t{string.Join(' ', row)}");
        }
    }

    private static string Bool(bool v) => v ? "true" : "false";
}
