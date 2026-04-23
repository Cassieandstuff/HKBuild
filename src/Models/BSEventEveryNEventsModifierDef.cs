using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>BSEventEveryNEventsModifier YAML schema (modifiers/*.yaml).
/// Fires an event after receiving N occurrences of another event.</summary>
public class BSEventEveryNEventsModifierDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSEventEveryNEventsModifier";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    /// <summary>Event to watch for.</summary>
    [YamlMember(Alias = "eventToCheckFor")]
    public InlineEventDef EventToCheckFor { get; set; } = new();

    /// <summary>Event to fire after N occurrences.</summary>
    [YamlMember(Alias = "eventToSend")]
    public InlineEventDef EventToSend { get; set; } = new();

    [YamlMember(Alias = "numberOfEventsBeforeSend")]
    public int NumberOfEventsBeforeSend { get; set; } = 1;

    [YamlMember(Alias = "minimumNumberOfEventsBeforeSend")]
    public int MinimumNumberOfEventsBeforeSend { get; set; } = 1;

    [YamlMember(Alias = "randomizeNumberOfEvents")]
    public bool RandomizeNumberOfEvents { get; set; } = false;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
