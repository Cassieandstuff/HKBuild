using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Skeleton YAML schema (character assets/skeleton.yaml).</summary>
public class SkeletonDef
{
    [YamlMember(Alias = "bones")]
    public List<string> Bones { get; set; } = [];
}
