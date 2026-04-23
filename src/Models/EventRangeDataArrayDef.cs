using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbEventRangeDataArray YAML schema (data/*_ranges.yaml).
/// Array of event ranges used by hkbEventsFromRangeModifier.</summary>
public class EventRangeDataArrayDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbEventRangeDataArray";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "eventData")]
    public List<EventRangeDef> EventData { get; set; } = [];
}

public class EventRangeDef
{
    [YamlMember(Alias = "upperBound")]
    public string UpperBound { get; set; } = "0.000000";

    [YamlMember(Alias = "eventId")]
    public int EventId { get; set; } = -1;

    /// <summary>Event name (named format).</summary>
    [YamlMember(Alias = "event")]
    public string? Event { get; set; }

    [YamlMember(Alias = "payload")]
    public string? Payload { get; set; }

    [YamlMember(Alias = "eventMode")]
    public string EventMode { get; set; } = "EVENT_MODE_SEND_ONCE";
}
