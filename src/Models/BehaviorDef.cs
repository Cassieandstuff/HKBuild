using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Top-level behavior.yaml schema.</summary>
public class BehaviorFile
{
    [YamlMember(Alias = "packfile")]
    public PackfileDef Packfile { get; set; } = new();

    [YamlMember(Alias = "behavior")]
    public BehaviorDef Behavior { get; set; } = new();
}

public class BehaviorDef
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "variableMode")]
    public string VariableMode { get; set; } = "VARIABLE_MODE_DISCARD_WHEN_INACTIVE";

    [YamlMember(Alias = "rootGenerator")]
    public string RootGenerator { get; set; } = "";

    [YamlMember(Alias = "data")]
    public string? Data { get; set; } = "null";
}
