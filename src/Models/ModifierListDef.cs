using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Modifier list YAML schema (modifiers/*.yaml with class hkbModifierList).
/// Contains an ordered list of modifier references that are all applied simultaneously.</summary>
public class ModifierListDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbModifierList";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    /// <summary>Ordered list of modifier names.</summary>
    [YamlMember(Alias = "modifiers")]
    public List<string> Modifiers { get; set; } = [];

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
