using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbFootIkControlsModifier YAML schema.
/// This is a dedicated model (not generic) because the class has deeply nested
/// inline structs (controlData → gains with 12 floats) that the generic modifier
/// path cannot handle.</summary>
public class FootIkControlsModifierDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "hkbFootIkControlsModifier";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    [YamlMember(Alias = "enable")]
    public bool Enable { get; set; } = true;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }

    [YamlMember(Alias = "controlData")]
    public FootIkControlDataDef ControlData { get; set; } = new();

    [YamlMember(Alias = "legs")]
    public List<FootIkControlsModifierLegDef>? Legs { get; set; }

    [YamlMember(Alias = "errorOutTranslation")]
    public string ErrorOutTranslation { get; set; } = "(0.000000 0.000000 0.000000 0.000000)";

    [YamlMember(Alias = "alignWithGroundRotation")]
    public string AlignWithGroundRotation { get; set; } = "(0.000000 0.000000 0.000000 0.000000)";
}

/// <summary>hkbFootIkControlData — wraps hkbFootIkGains.</summary>
public class FootIkControlDataDef
{
    [YamlMember(Alias = "gains")]
    public FootIkGainsDef Gains { get; set; } = new();
}

/// <summary>hkbFootIkGains — 12 float gains controlling foot IK behavior.</summary>
public class FootIkGainsDef
{
    [YamlMember(Alias = "onOffGain")]
    public float OnOffGain { get; set; }

    [YamlMember(Alias = "groundAscendingGain")]
    public float GroundAscendingGain { get; set; }

    [YamlMember(Alias = "groundDescendingGain")]
    public float GroundDescendingGain { get; set; }

    [YamlMember(Alias = "footPlantedGain")]
    public float FootPlantedGain { get; set; }

    [YamlMember(Alias = "footRaisedGain")]
    public float FootRaisedGain { get; set; }

    [YamlMember(Alias = "footUnlockGain")]
    public float FootUnlockGain { get; set; }

    [YamlMember(Alias = "worldFromModelFeedbackGain")]
    public float WorldFromModelFeedbackGain { get; set; }

    [YamlMember(Alias = "errorUpDownBias")]
    public float ErrorUpDownBias { get; set; }

    [YamlMember(Alias = "alignWorldFromModelGain")]
    public float AlignWorldFromModelGain { get; set; }

    [YamlMember(Alias = "hipOrientationGain")]
    public float HipOrientationGain { get; set; }

    [YamlMember(Alias = "maxKneeAngleDifference")]
    public float MaxKneeAngleDifference { get; set; }

    [YamlMember(Alias = "ankleOrientationGain")]
    public float AnkleOrientationGain { get; set; }
}

/// <summary>hkbFootIkControlsModifierLeg — per-leg runtime data.</summary>
public class FootIkControlsModifierLegDef
{
    [YamlMember(Alias = "groundPosition")]
    public string GroundPosition { get; set; } = "(0.000000 0.000000 0.000000 0.000000)";

    [YamlMember(Alias = "ungroundedEvent")]
    public InlineEventDef? UngroundedEvent { get; set; }

    [YamlMember(Alias = "verticalError")]
    public float VerticalError { get; set; }

    [YamlMember(Alias = "hitSomething")]
    public bool HitSomething { get; set; }

    [YamlMember(Alias = "isPlantedMS")]
    public bool IsPlantedMS { get; set; }
}
