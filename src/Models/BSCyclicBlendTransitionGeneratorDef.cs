using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Cyclic blend transition generator YAML schema (generators/*.yaml with class BSCyclicBlendTransitionGenerator).
/// Wraps a blender generator with event-driven blend freeze and crossblend control.</summary>
public class BSCyclicBlendTransitionGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSCyclicBlendTransitionGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    /// <summary>Name of the blender generator to wrap.</summary>
    [YamlMember(Alias = "pBlenderGenerator")]
    public string PBlenderGenerator { get; set; } = "";

    /// <summary>Inline event: freezes the blend parameter at its current value.</summary>
    [YamlMember(Alias = "EventToFreezeBlendValue")]
    public InlineEventDef EventToFreezeBlendValue { get; set; } = new();

    /// <summary>Inline event: triggers a crossblend to the next child.</summary>
    [YamlMember(Alias = "EventToCrossBlend")]
    public InlineEventDef EventToCrossBlend { get; set; } = new();

    [YamlMember(Alias = "fBlendParameter")]
    public string FBlendParameter { get; set; } = "0.000000";

    [YamlMember(Alias = "fTransitionDuration")]
    public string FTransitionDuration { get; set; } = "0.200000";

    [YamlMember(Alias = "eBlendCurve")]
    public string EBlendCurve { get; set; } = "BLEND_CURVE_SMOOTH";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}

/// <summary>Inline event definition used within generator/modifier YAML (id + optional payload).</summary>
public class InlineEventDef
{
    [YamlMember(Alias = "id")]
    public int Id { get; set; } = -1;

    /// <summary>Event name (named format). Resolved to ID at emit time.</summary>
    [YamlMember(Alias = "event")]
    public string? Event { get; set; }

    [YamlMember(Alias = "payload")]
    public string? Payload { get; set; }
}
