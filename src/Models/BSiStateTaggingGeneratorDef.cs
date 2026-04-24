using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>BSiStateTaggingGenerator YAML schema (generators/*.yaml with class BSiStateTaggingGenerator).
/// Tags the current state with an integer for game-side queries (e.g., iState variable).</summary>
public class BSiStateTaggingGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSiStateTaggingGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    /// <summary>Name of the wrapped generator.</summary>
    [YamlMember(Alias = "pDefaultGenerator")]
    public string PDefaultGenerator { get; set; } = "";

    [YamlMember(Alias = "iStateToSetAs")]
    public int IStateToSetAs { get; set; }

    [YamlMember(Alias = "iPriority")]
    public int IPriority { get; set; }

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
