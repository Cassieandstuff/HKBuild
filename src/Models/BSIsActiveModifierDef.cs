using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>BSIsActiveModifier YAML schema (modifiers/*.yaml with class BSIsActiveModifier).
/// Sets variables based on whether the behavior node is active.</summary>
public class BSIsActiveModifierDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSIsActiveModifier";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "bIsActive0")]
    public bool BIsActive0 { get; set; }

    [YamlMember(Alias = "bInvertActive0")]
    public bool BInvertActive0 { get; set; }

    [YamlMember(Alias = "bIsActive1")]
    public bool BIsActive1 { get; set; }

    [YamlMember(Alias = "bInvertActive1")]
    public bool BInvertActive1 { get; set; }

    [YamlMember(Alias = "bIsActive2")]
    public bool BIsActive2 { get; set; }

    [YamlMember(Alias = "bInvertActive2")]
    public bool BInvertActive2 { get; set; }

    [YamlMember(Alias = "bIsActive3")]
    public bool BIsActive3 { get; set; }

    [YamlMember(Alias = "bInvertActive3")]
    public bool BInvertActive3 { get; set; }

    [YamlMember(Alias = "bIsActive4")]
    public bool BIsActive4 { get; set; }

    [YamlMember(Alias = "bInvertActive4")]
    public bool BInvertActive4 { get; set; }

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
