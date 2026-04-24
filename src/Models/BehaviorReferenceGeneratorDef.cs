using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Behavior reference generator YAML schema (references/*.yaml).
/// References another behavior file by path.</summary>
public class BehaviorReferenceGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbBehaviorReferenceGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    /// <summary>Relative path to the referenced behavior file (e.g. "Behaviors\SprintBehavior.hkx").</summary>
    [YamlMember(Alias = "behaviorName")]
    public string BehaviorName { get; set; } = "";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
