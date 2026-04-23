using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbPoseMatchingGenerator YAML schema (generators/*.yaml).
/// Blender variant that matches poses — uses hkbBlenderGeneratorChild children.</summary>
public class PoseMatchingGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbPoseMatchingGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    // Blender base params
    [YamlMember(Alias = "referencePoseWeightThreshold")]
    public string ReferencePoseWeightThreshold { get; set; } = "0.000000";

    [YamlMember(Alias = "blendParameter")]
    public string BlendParameter { get; set; } = "0.000000";

    [YamlMember(Alias = "minCyclicBlendParameter")]
    public string MinCyclicBlendParameter { get; set; } = "0.000000";

    [YamlMember(Alias = "maxCyclicBlendParameter")]
    public string MaxCyclicBlendParameter { get; set; } = "1.000000";

    [YamlMember(Alias = "indexOfSyncMasterChild")]
    public int IndexOfSyncMasterChild { get; set; } = -1;

    [YamlMember(Alias = "flags")]
    public string Flags { get; set; } = "0";

    [YamlMember(Alias = "subtractLastChild")]
    public bool SubtractLastChild { get; set; } = false;

    /// <summary>Children — same type as hkbBlenderGenerator.</summary>
    [YamlMember(Alias = "children")]
    public List<BlenderChildDef>? Children { get; set; }

    // Pose matching params
    [YamlMember(Alias = "worldFromModelRotation")]
    public string WorldFromModelRotation { get; set; } = "(0.000000 0.000000 0.000000 1.000000)";

    [YamlMember(Alias = "blendSpeed")]
    public string BlendSpeed { get; set; } = "1.000000";

    [YamlMember(Alias = "minSpeedToSwitch")]
    public string MinSpeedToSwitch { get; set; } = "0.200000";

    [YamlMember(Alias = "minSwitchTimeNoError")]
    public string MinSwitchTimeNoError { get; set; } = "0.200000";

    [YamlMember(Alias = "minSwitchTimeFullError")]
    public string MinSwitchTimeFullError { get; set; } = "0.000000";

    [YamlMember(Alias = "startPlayingEventId")]
    public int StartPlayingEventId { get; set; } = -1;

    [YamlMember(Alias = "startPlayingEvent")]
    public string? StartPlayingEvent { get; set; }

    [YamlMember(Alias = "startMatchingEventId")]
    public int StartMatchingEventId { get; set; } = -1;

    [YamlMember(Alias = "startMatchingEvent")]
    public string? StartMatchingEvent { get; set; }

    [YamlMember(Alias = "rootBoneIndex")]
    public int RootBoneIndex { get; set; } = 0;

    [YamlMember(Alias = "otherBoneIndex")]
    public int OtherBoneIndex { get; set; } = 0;

    [YamlMember(Alias = "anotherBoneIndex")]
    public int AnotherBoneIndex { get; set; } = 0;

    [YamlMember(Alias = "pelvisIndex")]
    public int PelvisIndex { get; set; } = 0;

    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "MODE_MATCH";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
