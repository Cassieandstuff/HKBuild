using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Generic modifier YAML schema for simple modifier types that share the
/// standard hkbModifier base (variableBindingSet, userData, name, enable) plus
/// arbitrary extra params (scalars, inline events, references, ref-lists).
///
/// The extraction script emits these as flat YAML with the base fields plus
/// all additional hkparam values. This model captures everything dynamically
/// by deserializing into a dictionary and picking out known fields.</summary>
public class GenericModifierDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }

    /// <summary>All extra parameters beyond the base hkbModifier fields.
    /// Populated by BehaviorReader after YAML deserialization by diff-ing
    /// the raw YAML dictionary against the known base fields.</summary>
    [YamlIgnore]
    public List<GenericParam> ExtraParams { get; set; } = [];
}

/// <summary>A single extra parameter on a generic modifier.</summary>
public class GenericParam
{
    public string Name { get; set; } = "";
    public GenericParamKind Kind { get; set; }

    /// <summary>Scalar value (string representation of int/float/bool/vector/enum).</summary>
    public string? ScalarValue { get; set; }

    /// <summary>Reference to another node by name.</summary>
    public string? RefValue { get; set; }

    /// <summary>Inline event (id + payload). Resolved to indices at emit time.</summary>
    public InlineEventDef? EventValue { get; set; }

    /// <summary>List of references (for ref-list params like ChildrenA).</summary>
    public List<string>? RefListValue { get; set; }

    /// <summary>List of inline objects (for array params like BSLookAtModifier.bones).</summary>
    public List<InlineObjectEntry>? InlineObjectListValue { get; set; }
}

/// <summary>A single inline object in an array param. Stores key-value pairs
/// representing the hkparam fields of the anonymous hkobject.</summary>
public class InlineObjectEntry
{
    public List<KeyValuePair<string, string>> Fields { get; set; } = [];
}

public enum GenericParamKind
{
    Scalar,
    Reference,
    InlineEvent,
    RefList,
    InlineObjectList
}
