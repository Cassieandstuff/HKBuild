using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>bone_presets.yaml schema for behavior-local bone weight presets.</summary>
public class BonePresetsDef
{
    [YamlMember(Alias = "presets")]
    public Dictionary<string, Dictionary<string, string>> Presets { get; set; } = new();
}
