using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Top-level character.yaml schema.</summary>
public class CharacterFile
{
    [YamlMember(Alias = "packfile")]
    public PackfileDef Packfile { get; set; } = new();

    [YamlMember(Alias = "character")]
    public CharacterDef Character { get; set; } = new();
}

public class PackfileDef
{
    [YamlMember(Alias = "classversion")]
    public int ClassVersion { get; set; } = 8;

    [YamlMember(Alias = "contentsversion")]
    public string ContentsVersion { get; set; } = "hk_2010.2.0-r1";
}

public class CharacterDef
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "rig")]
    public string Rig { get; set; } = "";

    [YamlMember(Alias = "ragdoll")]
    public string Ragdoll { get; set; } = "";

    [YamlMember(Alias = "behavior")]
    public string Behavior { get; set; } = "";

    [YamlMember(Alias = "scale")]
    public float Scale { get; set; } = 1.0f;

    [YamlMember(Alias = "controller")]
    public ControllerDef Controller { get; set; } = new();

    [YamlMember(Alias = "model")]
    public ModelAxesDef Model { get; set; } = new();
}

public class ControllerDef
{
    [YamlMember(Alias = "capsuleHeight")]
    public float CapsuleHeight { get; set; }

    [YamlMember(Alias = "capsuleRadius")]
    public float CapsuleRadius { get; set; }

    [YamlMember(Alias = "collisionFilterInfo")]
    public int CollisionFilterInfo { get; set; }
}

public class ModelAxesDef
{
    [YamlMember(Alias = "up")]
    public List<float> Up { get; set; } = [];

    [YamlMember(Alias = "forward")]
    public List<float> Forward { get; set; } = [];

    [YamlMember(Alias = "right")]
    public List<float> Right { get; set; } = [];
}
