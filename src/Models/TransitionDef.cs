using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Blending transition effect YAML schema (transitions/*.yaml).</summary>
public class TransitionEffectDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbBlendingTransitionEffect";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "selfTransitionMode")]
    public string SelfTransitionMode { get; set; } = "SELF_TRANSITION_MODE_CONTINUE_IF_CYCLIC_BLEND_IF_ACYCLIC";

    [YamlMember(Alias = "eventMode")]
    public string EventMode { get; set; } = "EVENT_MODE_DEFAULT";

    [YamlMember(Alias = "duration")]
    public string Duration { get; set; } = "0.200000";

    [YamlMember(Alias = "toGeneratorStartTimeFraction")]
    public string ToGeneratorStartTimeFraction { get; set; } = "0.000000";

    [YamlMember(Alias = "flags")]
    public string Flags { get; set; } = "0";

    [YamlMember(Alias = "endMode")]
    public string EndMode { get; set; } = "END_MODE_NONE";

    [YamlMember(Alias = "blendCurve")]
    public string BlendCurve { get; set; } = "BLEND_CURVE_SMOOTH";

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}

/// <summary>State machine transition info array YAML schema.</summary>
public class TransitionInfoArrayDef
{
    [YamlMember(Alias = "transitions")]
    public List<TransitionInfoDef> Transitions { get; set; } = [];
}

public class TransitionInfoDef
{
    [YamlMember(Alias = "triggerInterval")]
    public TransitionIntervalDef TriggerInterval { get; set; } = new();

    [YamlMember(Alias = "initiateInterval")]
    public TransitionIntervalDef InitiateInterval { get; set; } = new();

    [YamlMember(Alias = "transition")]
    public string Transition { get; set; } = "";

    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>hkbStringCondition value (alternative to expression condition).</summary>
    [YamlMember(Alias = "conditionString")]
    public string? ConditionString { get; set; }

    [YamlMember(Alias = "eventId")]
    public int EventId { get; set; } = -1;

    /// <summary>Event name (named format). Resolved to ID at emit time.</summary>
    [YamlMember(Alias = "event")]
    public string? Event { get; set; }

    [YamlMember(Alias = "toStateId")]
    public int ToStateId { get; set; }

    /// <summary>Target state name (named format). Resolved to toStateId at load time using state machine.</summary>
    [YamlMember(Alias = "toState")]
    public string? ToState { get; set; }

    [YamlMember(Alias = "fromNestedStateId")]
    public int FromNestedStateId { get; set; }

    [YamlMember(Alias = "toNestedStateId")]
    public int ToNestedStateId { get; set; }

    [YamlMember(Alias = "priority")]
    public int Priority { get; set; }

    [YamlMember(Alias = "flags")]
    public string Flags { get; set; } = "FLAG_DISABLE_CONDITION";
}

public class TransitionIntervalDef
{
    [YamlMember(Alias = "enterEventId")]
    public int EnterEventId { get; set; } = -1;

    /// <summary>Enter event name (named format). Resolved to ID at emit time.</summary>
    [YamlMember(Alias = "enterEvent")]
    public string? EnterEvent { get; set; }

    [YamlMember(Alias = "exitEventId")]
    public int ExitEventId { get; set; } = -1;

    /// <summary>Exit event name (named format). Resolved to ID at emit time.</summary>
    [YamlMember(Alias = "exitEvent")]
    public string? ExitEvent { get; set; }

    [YamlMember(Alias = "enterTime")]
    public string EnterTime { get; set; } = "0.000000";

    [YamlMember(Alias = "exitTime")]
    public string ExitTime { get; set; } = "0.000000";
}
