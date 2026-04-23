using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>hkbStringEventPayload YAML schema.
/// A string payload that can be attached to clip trigger events or state notify events.</summary>
public class StringEventPayloadDef
{
    [YamlMember(Alias = "data")]
    public string Data { get; set; } = "";
}
