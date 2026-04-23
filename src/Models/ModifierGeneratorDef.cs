using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Modifier generator YAML schema (modifiers/*.yaml with class hkbModifierGenerator).
/// Pairs a generator with a modifier — the modifier runs while the generator plays.</summary>
public class ModifierGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbModifierGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    /// <summary>Name of the modifier node.</summary>
    [YamlMember(Alias = "modifier")]
    public string Modifier { get; set; } = "";

    /// <summary>Name of the child generator.</summary>
    [YamlMember(Alias = "generator")]
    public string Generator { get; set; } = "";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
