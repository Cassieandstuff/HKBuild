using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>BSSynchronizedClipGenerator YAML schema (generators/*.yaml).
/// Synchronized paired animation generator.</summary>
public class BSSynchronizedClipGeneratorDef
{
    [YamlMember(Alias = "class")]
    public string Class { get; set; } = "BSSynchronizedClipGenerator";

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "userData")]
    public int UserData { get; set; } = 0;

    /// <summary>Name of the wrapped clip generator.</summary>
    [YamlMember(Alias = "pClipGenerator")]
    public string PClipGenerator { get; set; } = "";

    [YamlMember(Alias = "SyncAnimPrefix")]
    public string SyncAnimPrefix { get; set; } = "";

    [YamlMember(Alias = "bSyncClipIgnoreMarkPlacement")]
    public bool BSyncClipIgnoreMarkPlacement { get; set; } = false;

    [YamlMember(Alias = "fGetToMarkTime")]
    public string FGetToMarkTime { get; set; } = "0.000000";

    [YamlMember(Alias = "fMarkErrorThreshold")]
    public string FMarkErrorThreshold { get; set; } = "0.100000";

    [YamlMember(Alias = "bLeadCharacter")]
    public bool BLeadCharacter { get; set; } = false;

    [YamlMember(Alias = "bReorientSupportChar")]
    public bool BReorientSupportChar { get; set; } = false;

    [YamlMember(Alias = "bApplyMotionFromRoot")]
    public bool BApplyMotionFromRoot { get; set; } = false;

    [YamlMember(Alias = "sAnimationBindingIndex")]
    public int SAnimationBindingIndex { get; set; } = -1;

    [YamlMember(Alias = "bindings")]
    public List<BindingDef>? Bindings { get; set; }
}
