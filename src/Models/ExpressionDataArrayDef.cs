using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbExpressionDataArray YAML schema (data/*_expressions.yaml).
/// Array of expression entries evaluated by hkbEvaluateExpressionModifier.</summary>
public class ExpressionDataArrayDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbExpressionDataArray";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "expressionsData")]
    public List<ExpressionDataDef> ExpressionsData { get; set; } = [];
}

public class ExpressionDataDef
{
    [YamlMember(Alias = "expression")]
    public string Expression { get; set; } = "";

    [YamlMember(Alias = "assignmentVariableIndex")]
    public int AssignmentVariableIndex { get; set; } = -1;

    /// <summary>Assignment variable name (named format).</summary>
    [YamlMember(Alias = "assignmentVariable")]
    public string? AssignmentVariable { get; set; }

    [YamlMember(Alias = "assignmentEventIndex")]
    public int AssignmentEventIndex { get; set; } = -1;

    /// <summary>Assignment event name (named format).</summary>
    [YamlMember(Alias = "assignmentEvent")]
    public string? AssignmentEvent { get; set; }

    [YamlMember(Alias = "eventMode")]
    public string EventMode { get; set; } = "EVENT_MODE_SEND_ONCE";
}
