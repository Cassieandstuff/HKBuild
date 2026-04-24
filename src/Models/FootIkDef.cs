using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>foot_ik.yaml schema.</summary>
public class FootIkDef
{
    [YamlMember(Alias = "legs")]
    public List<FootIkLeg> Legs { get; set; } = [];

    [YamlMember(Alias = "raycastDistanceUp")]
    public float RaycastDistanceUp { get; set; }

    [YamlMember(Alias = "raycastDistanceDown")]
    public float RaycastDistanceDown { get; set; }

    [YamlMember(Alias = "originalGroundHeightMS")]
    public float OriginalGroundHeightMS { get; set; }

    [YamlMember(Alias = "verticalOffset")]
    public float VerticalOffset { get; set; }

    [YamlMember(Alias = "collisionFilterInfo")]
    public int CollisionFilterInfo { get; set; }

    [YamlMember(Alias = "forwardAlignFraction")]
    public float ForwardAlignFraction { get; set; }

    [YamlMember(Alias = "sidewaysAlignFraction")]
    public float SidewaysAlignFraction { get; set; }

    [YamlMember(Alias = "sidewaysSampleWidth")]
    public float SidewaysSampleWidth { get; set; }

    [YamlMember(Alias = "lockFeetWhenPlanted")]
    public bool LockFeetWhenPlanted { get; set; }

    [YamlMember(Alias = "useCharacterUpVector")]
    public bool UseCharacterUpVector { get; set; }

    [YamlMember(Alias = "isQuadrupedNarrow")]
    public bool IsQuadrupedNarrow { get; set; }
}

public class FootIkLeg
{
    [YamlMember(Alias = "kneeAxisLS")]
    public List<float> KneeAxisLS { get; set; } = [];

    [YamlMember(Alias = "footEndLS")]
    public List<float> FootEndLS { get; set; } = [];

    [YamlMember(Alias = "footPlantedAnkleHeightMS")]
    public float FootPlantedAnkleHeightMS { get; set; }

    [YamlMember(Alias = "footRaisedAnkleHeightMS")]
    public float FootRaisedAnkleHeightMS { get; set; }

    [YamlMember(Alias = "maxAnkleHeightMS")]
    public float MaxAnkleHeightMS { get; set; }

    [YamlMember(Alias = "minAnkleHeightMS")]
    public float MinAnkleHeightMS { get; set; }

    [YamlMember(Alias = "maxKneeAngleDegrees")]
    public float MaxKneeAngleDegrees { get; set; }

    [YamlMember(Alias = "minKneeAngleDegrees")]
    public float MinKneeAngleDegrees { get; set; }

    [YamlMember(Alias = "maxAnkleAngleDegrees")]
    public float MaxAnkleAngleDegrees { get; set; }

    [YamlMember(Alias = "hipIndex")]
    public int HipIndex { get; set; }

    [YamlMember(Alias = "kneeIndex")]
    public int KneeIndex { get; set; }

    [YamlMember(Alias = "ankleIndex")]
    public int AnkleIndex { get; set; }
}
