using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>BSOffsetAnimationGenerator YAML schema (generators/*.yaml).
/// Blends a default generator with an offset clip based on a variable.</summary>
public class BSOffsetAnimationGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSOffsetAnimationGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "pDefaultGenerator")]
    public string PDefaultGenerator { get; set; } = "";

    [YamlMember(Alias = "pOffsetClipGenerator")]
    public string POffsetClipGenerator { get; set; } = "";

    [YamlMember(Alias = "fOffsetVariable")]
    public string FOffsetVariable { get; set; } = "0.000000";

    [YamlMember(Alias = "fOffsetRangeStart")]
    public string FOffsetRangeStart { get; set; } = "0.000000";

    [YamlMember(Alias = "fOffsetRangeEnd")]
    public string FOffsetRangeEnd { get; set; } = "1.000000";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
