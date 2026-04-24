using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>mirror.yaml schema.</summary>
public class MirrorDef
{
    [YamlMember(Alias = "mirrorAxis")]
    public List<float> MirrorAxis { get; set; } = [];

    [YamlMember(Alias = "bonePairMap")]
    public BonePairMapDef BonePairMap { get; set; } = new();
}

public class BonePairMapDef
{
    /// <summary>Raw format: bone count.</summary>
    [YamlMember(Alias = "count")]
    public int Count { get; set; }

    /// <summary>Raw format: space-separated int indices.</summary>
    [YamlMember(Alias = "values")]
    public string Values { get; set; } = "";

    /// <summary>Named format: bone name → mirror bone name. Unlisted bones mirror to themselves.</summary>
    [YamlMember(Alias = "named")]
    public Dictionary<string, string>? Named { get; set; }

    /// <summary>Whether this uses the named bone pair format.</summary>
    public bool IsNamed => Named != null && Named.Count > 0;

    /// <summary>Whether this has any data (raw or named).</summary>
    public bool HasData => IsNamed || (Count > 0 && !string.IsNullOrWhiteSpace(Values));

    /// <summary>
    /// Resolve to a flat index array using skeleton bone names.
    /// Bones not listed in Named map to themselves (identity).
    /// </summary>
    public (int count, string values) Resolve(List<string> boneNames)
    {
        if (!IsNamed)
            return (Count, Values);

        // Build name → index lookup.
        var nameToIndex = new Dictionary<string, int>();
        for (int i = 0; i < boneNames.Count; i++)
            nameToIndex[boneNames[i]] = i;

        // Start with identity mapping (every bone mirrors to itself).
        var map = new int[boneNames.Count];
        for (int i = 0; i < map.Length; i++)
            map[i] = i;

        foreach (var (fromBone, toBone) in Named!)
        {
            if (!nameToIndex.TryGetValue(fromBone, out int fromIdx))
            {
                Console.Error.WriteLine($"  WARNING: Unknown bone '{fromBone}' in mirror bone pair map");
                continue;
            }
            if (!nameToIndex.TryGetValue(toBone, out int toIdx))
            {
                Console.Error.WriteLine($"  WARNING: Unknown bone '{toBone}' in mirror bone pair map");
                continue;
            }
            map[fromIdx] = toIdx;
        }

        return (boneNames.Count, string.Join(' ', map));
    }
}
