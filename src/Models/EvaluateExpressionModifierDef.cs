using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbEvaluateExpressionModifier YAML schema (modifiers/*.yaml).
/// Modifier that evaluates expressions — references an hkbExpressionDataArray.</summary>
public class EvaluateExpressionModifierDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbEvaluateExpressionModifier";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    /// <summary>Name of the referenced hkbExpressionDataArray (resolved to ID at emit time).</summary>
    [YamlMember(Alias = "expressions")]
    public string Expressions { get; set; } = "";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
