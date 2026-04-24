using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbBoneIndexArray YAML schema (data/boneIndexArray_*.yaml).
/// List of bone indices referenced by modifiers like hkbKeyframeBonesModifier.</summary>
public class BoneIndexArrayDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbBoneIndexArray";

    /// <summary>Name identifying this bone index array (set by extraction to ownerModifier_paramName).</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>Bone indices as a list of bone names (named format) or raw indices.</summary>
    [YamlMember(Alias = "boneIndices")]
    public object? BoneIndices { get; set; }
}
