using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>BSBoneSwitchGenerator YAML schema (generators/*.yaml).
/// Bone-masked generator switch — selects generators per bone weight region.</summary>
public class BSBoneSwitchGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSBoneSwitchGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    /// <summary>Default generator when no bone weight match.</summary>
    [YamlMember(Alias = "pDefaultGenerator")]
    public string PDefaultGenerator { get; set; } = "";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }

    /// <summary>Bone data children (inline BSBoneSwitchGeneratorBoneData).</summary>
    [YamlMember(Alias = "children")]
    public List<BoneSwitchChildDef>? Children { get; set; }
}

/// <summary>Inline child for BSBoneSwitchGenerator.</summary>
public class BoneSwitchChildDef
{
    [YamlMember(Alias = "pGenerator")]
    public string PGenerator { get; set; } = "";

    /// <summary>Bone weights — nested structure with a 'named' sub-dict (reuses BoneWeightsDef from PropertyDef).</summary>
    [YamlMember(Alias = "boneWeights")]
    public BoneWeightsDef? BoneWeights { get; set; }

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
