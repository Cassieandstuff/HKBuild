using YamlDotNet.Serialization;

namespace HKBuild;

/// <summary>
/// Loads a skeleton bone list from character assets/skeleton.yaml.
/// Used to resolve named bone weights and bone indices at compile time.
/// </summary>
public static class SkeletonReader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Load skeleton from a skeleton.yaml file path.
    /// Returns an ordered list of bone names (index = bone index).
    /// </summary>
    public static List<string> Load(string skeletonYamlPath)
    {
        if (!File.Exists(skeletonYamlPath))
            throw new FileNotFoundException($"Skeleton file not found: {skeletonYamlPath}");

        var text = File.ReadAllText(skeletonYamlPath);
        var skeleton = Yaml.Deserialize<Models.SkeletonDef>(text)
            ?? throw new InvalidDataException($"Failed to deserialize: {skeletonYamlPath}");

        if (skeleton.Bones.Count == 0)
            throw new InvalidDataException($"Skeleton has no bones: {skeletonYamlPath}");

        Console.WriteLine($"  Skeleton: {skeleton.Bones.Count} bones from {Path.GetFileName(skeletonYamlPath)}");
        return skeleton.Bones;
    }

    /// <summary>
    /// Search upward from a source directory to find character assets/skeleton.yaml.
    /// Walks parent directories looking for a sibling "character assets" folder.
    /// </summary>
    public static List<string>? FindAndLoad(string sourceDir)
    {
        // Walk up from the source directory (e.g. bashbehavior.hkx/)
        // looking for a sibling "character assets" directory with skeleton.yaml.
        var dir = Path.GetFullPath(sourceDir);
        while (dir != null)
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent == null) break;

            var assetsDir = Path.Combine(parent, "character assets");
            var skeletonPath = Path.Combine(assetsDir, "skeleton.yaml");
            if (File.Exists(skeletonPath))
                return Load(skeletonPath);

            dir = parent;
        }

        return null;
    }
}
