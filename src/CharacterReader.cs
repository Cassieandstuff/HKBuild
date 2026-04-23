using HKBuild.Models;
using YamlDotNet.Serialization;

namespace HKBuild;

/// <summary>
/// Loaded character data — everything the emitter needs.
/// </summary>
public class CharacterData
{
    public required CharacterFile Character { get; init; }
    public required List<string> Animations { get; init; }
    public required List<PropertyDef> Properties { get; init; }
    public required FootIkDef FootIk { get; init; }
    public required MirrorDef Mirror { get; init; }

    /// <summary>Skeleton bone names in index order (null if no skeleton.yaml found).</summary>
    public List<string>? BoneNames { get; init; }
}

/// <summary>
/// Reads a character source directory and returns a fully loaded CharacterData.
/// </summary>
public static class CharacterReader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static CharacterData Load(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Character directory not found: {directory}");

        var character = LoadYaml<CharacterFile>(Path.Combine(directory, "character.yaml"));
        var animations = LoadAnimations(Path.Combine(directory, "animations.txt"));
        var properties = LoadProperties(Path.Combine(directory, "properties"));
        var footIk = LoadYaml<FootIkDef>(Path.Combine(directory, "foot_ik.yaml"));
        var mirror = LoadYaml<MirrorDef>(Path.Combine(directory, "mirror.yaml"));

        // Load skeleton bone names (search upward for character assets/skeleton.yaml).
        var boneNames = SkeletonReader.FindAndLoad(directory);

        Console.WriteLine($"Loaded character '{character.Character.Name}':");
        Console.WriteLine($"  {animations.Count} animations");
        Console.WriteLine($"  {properties.Count} properties ({properties.Count(p => p.IsPointer)} with bone weights)");
        Console.WriteLine($"  {footIk.Legs.Count} foot IK legs");
        var pairCount = mirror.BonePairMap.IsNamed ? mirror.BonePairMap.Named!.Count : mirror.BonePairMap.Count;
        Console.WriteLine($"  {pairCount} mirror bone pairs");

        return new CharacterData
        {
            Character = character,
            Animations = animations,
            Properties = properties,
            FootIk = footIk,
            Mirror = mirror,
            BoneNames = boneNames
        };
    }

    private static T LoadYaml<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required file not found: {path}");

        var text = File.ReadAllText(path);
        return Yaml.Deserialize<T>(text)
            ?? throw new InvalidDataException($"Failed to deserialize: {path}");
    }

    private static List<string> LoadAnimations(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required file not found: {path}");

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();
    }

    private static List<PropertyDef> LoadProperties(string propsDir)
    {
        if (!Directory.Exists(propsDir))
            throw new DirectoryNotFoundException($"Properties directory not found: {propsDir}");

        // Check for explicit ordering file.
        var orderFile = Path.Combine(propsDir, "_order.txt");
        List<string> order;

        if (File.Exists(orderFile))
        {
            order = File.ReadAllLines(orderFile)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Trim())
                .ToList();
        }
        else
        {
            // Fallback: alphabetical by filename.
            order = Directory.GetFiles(propsDir, "*.yaml")
                .Select(Path.GetFileNameWithoutExtension)
                .Order()
                .ToList()!;
        }

        var properties = new List<PropertyDef>();
        foreach (var name in order)
        {
            // Property names may contain + characters; filenames use _ instead.
            var safeName = name.Replace('+', '_');
            var path = Path.Combine(propsDir, safeName + ".yaml");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Property file not found for '{name}': {path}");

            var prop = LoadYaml<PropertyDef>(path);

            // Ensure the name matches what _order.txt says (the YAML name field
            // may contain + characters that the filename doesn't).
            if (string.IsNullOrEmpty(prop.Name))
                prop.Name = name;

            properties.Add(prop);
        }

        return properties;
    }
}
