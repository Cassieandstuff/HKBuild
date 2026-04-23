using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>State machine YAML schema (states/Root.yaml).</summary>
public class StateMachineDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbStateMachine";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "startStateId")]
    public int StartStateId { get; set; }

    [YamlMember(Alias = "returnToPreviousStateEventId")]
    public int ReturnToPreviousStateEventId { get; set; } = -1;

    [YamlMember(Alias = "returnToPreviousStateEvent")]
    public string? ReturnToPreviousStateEvent { get; set; }

    [YamlMember(Alias = "randomTransitionEventId")]
    public int RandomTransitionEventId { get; set; } = -1;

    [YamlMember(Alias = "randomTransitionEvent")]
    public string? RandomTransitionEvent { get; set; }

    [YamlMember(Alias = "transitionToNextHigherStateEventId")]
    public int TransitionToNextHigherStateEventId { get; set; } = -1;

    [YamlMember(Alias = "transitionToNextHigherStateEvent")]
    public string? TransitionToNextHigherStateEvent { get; set; }

    [YamlMember(Alias = "transitionToNextLowerStateEventId")]
    public int TransitionToNextLowerStateEventId { get; set; } = -1;

    [YamlMember(Alias = "transitionToNextLowerStateEvent")]
    public string? TransitionToNextLowerStateEvent { get; set; }

    [YamlMember(Alias = "syncVariableIndex")]
    public int SyncVariableIndex { get; set; } = -1;

    [YamlMember(Alias = "syncVariable")]
    public string? SyncVariable { get; set; }

    [YamlMember(Alias = "wrapAroundStateId")]
    public bool WrapAroundStateId { get; set; }

    [YamlMember(Alias = "maxSimultaneousTransitions")]
    public int MaxSimultaneousTransitions { get; set; } = 32;

    [YamlMember(Alias = "startStateMode")]
    public string StartStateMode { get; set; } = "START_STATE_MODE_DEFAULT";

    [YamlMember(Alias = "selfTransitionMode")]
    public string SelfTransitionMode { get; set; } = "SELF_TRANSITION_MODE_NO_TRANSITION";

    [YamlMember(Alias = "wildcardTransitions")]
    public string? WildcardTransitions { get; set; } = "null";

    /// <summary>Parsed wildcard transitions (populated by BehaviorReader from inline transitions: block).</summary>
    [YamlIgnore]
    public List<TransitionInfoDef>? ParsedWildcardTransitions { get; set; }

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }

    [YamlMember(Alias = "states")]
    public List<string> States { get; set; } = [];
}

/// <summary>State info YAML schema (states/*.yaml).</summary>
public class StateDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbStateMachineStateInfo";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "stateId")]
    public int StateId { get; set; }

    [YamlMember(Alias = "generator")]
    public string Generator { get; set; } = "";

    [YamlMember(Alias = "probability")]
    public string Probability { get; set; } = "1.000000";

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlIgnore]
    public string? Transitions { get; set; }

    /// <summary>Parsed inline transitions (populated by BehaviorReader).</summary>
    [YamlIgnore]
    public List<TransitionInfoDef>? ParsedTransitions { get; set; }

    [YamlMember(Alias = "enterNotifyEvents")]
    public object? EnterNotifyEvents { get; set; }

    [YamlMember(Alias = "exitNotifyEvents")]
    public object? ExitNotifyEvents { get; set; }
}

/// <summary>Event property used in enter/exit notify event arrays.</summary>
public class EventPropertyDef
{
    [YamlMember(Alias = "id")]
    public int Id { get; set; } = -1;

    /// <summary>Event name (named format). Resolved to ID at emit time.</summary>
    [YamlMember(Alias = "event")]
    public string? Event { get; set; }

    [YamlMember(Alias = "payload")]
    public string Payload { get; set; } = "null";
}
