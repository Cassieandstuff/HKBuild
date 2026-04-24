using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Behavior graph data YAML schema (data/graphdata.yaml).</summary>
public class BehaviorGraphDataDef
{
    [YamlMember(Alias = "variables")]
    public List<VariableInfoDef> Variables { get; set; } = [];

    [YamlMember(Alias = "events")]
    public List<EventInfoDef> Events { get; set; } = [];

    [YamlMember(Alias = "characterPropertyInfos")]
    public int CharacterPropertyInfoCount { get; set; } = 0;

    [YamlMember(Alias = "characterPropertyNames")]
    public List<CharacterPropertyDef> CharacterPropertyNames { get; set; } = [];

    [YamlMember(Alias = "attributeDefaults")]
    public int AttributeDefaultCount { get; set; } = 0;

    [YamlMember(Alias = "eventNames")]
    public List<string> EventNames { get; set; } = [];

    [YamlMember(Alias = "variableNames")]
    public List<string> VariableNames { get; set; } = [];

    [YamlMember(Alias = "variableValues")]
    public List<int> VariableValues { get; set; } = [];

    [YamlMember(Alias = "quadVariableValues")]
    public List<string> QuadVariableValues { get; set; } = [];

    [YamlMember(Alias = "variantVariableValues")]
    public int VariantVariableValueCount { get; set; } = 0;

    [YamlMember(Alias = "wordMinVariableValues")]
    public int WordMinVariableValueCount { get; set; } = 0;

    [YamlMember(Alias = "wordMaxVariableValues")]
    public int WordMaxVariableValueCount { get; set; } = 0;
}

public class VariableInfoDef
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "VARIABLE_TYPE_REAL";

    [YamlMember(Alias = "role")]
    public string Role { get; set; } = "ROLE_DEFAULT";

    [YamlMember(Alias = "roleFlags")]
    public int RoleFlags { get; set; } = 0;

    [YamlMember(Alias = "value")]
    public int Value { get; set; } = 0;

    /// <summary>Quad initial value for VECTOR4/QUATERNION types (e.g. "(0.0 0.0 0.0 1.0)").</summary>
    [YamlMember(Alias = "quadValue")]
    public string? QuadValue { get; set; }
}

public class EventInfoDef
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "flags")]
    public string Flags { get; set; } = "0";
}

/// <summary>Character property definition in behavior graph data.</summary>
public class CharacterPropertyDef
{
    /// <summary>Property name (also used in characterPropertyNames string data).</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>Variable type (e.g. VARIABLE_TYPE_POINTER, VARIABLE_TYPE_BOOL).</summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "VARIABLE_TYPE_POINTER";

    /// <summary>Role flags from hkbRoleAttribute.</summary>
    [YamlMember(Alias = "flags")]
    public string Flags { get; set; } = "0";
}
