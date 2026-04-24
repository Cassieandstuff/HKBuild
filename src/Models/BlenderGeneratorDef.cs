using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Blender generator YAML schema (generators/*.yaml).</summary>
public class BlenderGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbBlenderGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "flags")]
    public int Flags { get; set; }

    [YamlMember(Alias = "subtractLastChild")]
    public bool SubtractLastChild { get; set; }

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "referencePoseWeightThreshold")]
    public string ReferencePoseWeightThreshold { get; set; } = "0.000000";

    [YamlMember(Alias = "blendParameter")]
    public string BlendParameter { get; set; } = "1.000000";

    [YamlMember(Alias = "minCyclicBlendParameter")]
    public string MinCyclicBlendParameter { get; set; } = "0.000000";

    [YamlMember(Alias = "maxCyclicBlendParameter")]
    public string MaxCyclicBlendParameter { get; set; } = "1.000000";

    [YamlMember(Alias = "indexOfSyncMasterChild")]
    public int IndexOfSyncMasterChild { get; set; } = -1;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }

    [YamlMember(Alias = "children")]
    public List<BlenderChildDef> Children { get; set; } = [];
}

public class BlenderChildDef
{
    [YamlMember(Alias = "generator")]
    public string Generator { get; set; } = "";

    [YamlMember(Alias = "weight")]
    public string Weight { get; set; } = "0.000000";

    [YamlMember(Alias = "worldFromModelWeight")]
    public string WorldFromModelWeight { get; set; } = "1.000000";

    [YamlMember(Alias = "boneWeights")]
    public BoneWeightsDef? BoneWeights { get; set; }
}

/// <summary>Variable binding — inlined in any node that has bindings.</summary>
public class BindingDef
{
    [YamlMember(Alias = "memberPath")]
    public string MemberPath { get; set; } = "";

    [YamlMember(Alias = "variableIndex")]
    public int VariableIndex { get; set; } = -1;

    /// <summary>Variable name (named format). Resolved to index at emit time.</summary>
    [YamlMember(Alias = "variable")]
    public string? Variable { get; set; }

    [YamlMember(Alias = "bitIndex")]
    public int BitIndex { get; set; } = -1;

    [YamlMember(Alias = "bindingType")]
    public string BindingType { get; set; } = "BINDING_TYPE_VARIABLE";
}
