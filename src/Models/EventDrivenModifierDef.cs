using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Event-driven modifier YAML schema (modifiers/*.yaml with class hkbEventDrivenModifier).
/// Activates/deactivates a child modifier based on events.</summary>
public class EventDrivenModifierDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbEventDrivenModifier";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    /// <summary>Name of the child modifier to activate/deactivate.</summary>
    [YamlMember(Alias = "modifier")]
    public string Modifier { get; set; } = "";

    [YamlMember(Alias = "activateEventId")]
    public int ActivateEventId { get; set; } = -1;

    /// <summary>Activate event name (named format).</summary>
    [YamlMember(Alias = "activateEvent")]
    public string? ActivateEvent { get; set; }

    [YamlMember(Alias = "deactivateEventId")]
    public int DeactivateEventId { get; set; } = -1;

    /// <summary>Deactivate event name (named format).</summary>
    [YamlMember(Alias = "deactivateEvent")]
    public string? DeactivateEvent { get; set; }

    [YamlMember(Alias = "activeByDefault")]
    public bool ActiveByDefault { get; set; } = false;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
