using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Manual selector generator YAML schema (selectors/*.yaml).</summary>
public class ManualSelectorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbManualSelectorGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "selectedGeneratorIndex")]
    public int SelectedGeneratorIndex { get; set; }

    [YamlMember(Alias = "currentGeneratorIndex")]
    public int CurrentGeneratorIndex { get; set; }

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }

    [YamlMember(Alias = "generators")]
    public List<string> Generators { get; set; } = [];
}
