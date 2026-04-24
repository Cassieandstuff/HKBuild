using HKBuild.Models;
using YamlDotNet.Serialization;

namespace HKBuild;

public static class AnimationReader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static AnimationDef Load(string yamlPath)
    {
        var text = File.ReadAllText(yamlPath);
        var file = Yaml.Deserialize<AnimationFile>(text);
        return file.Animation;
    }
}
