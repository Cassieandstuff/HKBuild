using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Clip generator YAML schema (clips/*.yaml).</summary>
public class ClipGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbClipGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "animationName")]
    public string AnimationName { get; set; } = "";

    [YamlMember(Alias = "mode")]
    public string Mode { get; set; } = "MODE_SINGLE_PLAY";

    [YamlMember(Alias = "playbackSpeed")]
    public string PlaybackSpeed { get; set; } = "1.000000";

    [YamlMember(Alias = "cropStartAmountLocalTime")]
    public string CropStartAmountLocalTime { get; set; } = "0.000000";

    [YamlMember(Alias = "cropEndAmountLocalTime")]
    public string CropEndAmountLocalTime { get; set; } = "0.000000";

    [YamlMember(Alias = "startTime")]
    public string StartTime { get; set; } = "0.000000";

    [YamlMember(Alias = "enforcedDuration")]
    public string EnforcedDuration { get; set; } = "0.000000";

    [YamlMember(Alias = "userControlledTimeFraction")]
    public string UserControlledTimeFraction { get; set; } = "0.000000";

    [YamlMember(Alias = "animationBindingIndex")]
    public int AnimationBindingIndex { get; set; } = -1;

    [YamlMember(Alias = "flags")]
    public int Flags { get; set; } = 0;

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "triggers")]
    public List<ClipTriggerDef>? Triggers { get; set; }
}

public class ClipTriggerDef
{
    [YamlMember(Alias = "localTime")]
    public string LocalTime { get; set; } = "0.000000";

    /// <summary>Numeric event ID (raw format). Use event name instead when graph data is available.</summary>
    [YamlMember(Alias = "eventId")]
    public int EventId { get; set; } = -1;

    /// <summary>Event name (named format). Resolved to ID at emit time using graph data.</summary>
    [YamlMember(Alias = "event")]
    public string? Event { get; set; }

    [YamlMember(Alias = "payload")]
    public string Payload { get; set; } = "null";

    [YamlMember(Alias = "relativeToEndOfClip")]
    public bool RelativeToEndOfClip { get; set; }

    [YamlMember(Alias = "acyclic")]
    public bool Acyclic { get; set; }

    [YamlMember(Alias = "isAnnotation")]
    public bool IsAnnotation { get; set; }
}
