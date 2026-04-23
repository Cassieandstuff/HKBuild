using YamlDotNet.Serialization;

namespace HKBuild.Models;

/// <summary>Property YAML schema (properties/*.yaml).</summary>
public class PropertyDef
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "";

    [YamlMember(Alias = "role")]
    public string Role { get; set; } = "ROLE_DEFAULT";

    [YamlMember(Alias = "initial_value")]
    public long? InitialValue { get; set; }

    [YamlMember(Alias = "bone_weights")]
    public BoneWeightsDef? BoneWeights { get; set; }

    public bool IsPointer => Type == "VARIABLE_TYPE_POINTER";
}

public class BoneWeightsDef
{
    /// <summary>Raw format: bone count.</summary>
    [YamlMember(Alias = "count")]
    public int Count { get; set; }

    /// <summary>Raw format: space-separated float values.</summary>
    [YamlMember(Alias = "values")]
    public string Values { get; set; } = "";

    /// <summary>Named format: bone name → weight. Resolved at emit time using skeleton.</summary>
    [YamlMember(Alias = "named")]
    public Dictionary<string, string>? Named { get; set; }

    /// <summary>Preset name defined in behavior-local bone_presets.yaml.</summary>
    [YamlMember(Alias = "preset")]
    public string? Preset { get; set; }

    /// <summary>
    /// Optional explicit bone count for the emitted array.
    /// When set, Resolve() truncates the output to this many entries instead of the full skeleton.
    /// This preserves vanilla array lengths (e.g. 85 instead of 99).
    /// </summary>
    [YamlMember(Alias = "bone_count")]
    public int? BoneCount { get; set; }

    /// <summary>Whether this uses the named bone format.</summary>
    public bool IsNamed => Named != null;

    /// <summary>Whether this references a named preset.</summary>
    public bool IsPreset => !string.IsNullOrWhiteSpace(Preset);

    /// <summary>Whether this bone weight def has any data (raw or named).</summary>
    public bool HasData => IsPreset || IsNamed || (Count > 0 && !string.IsNullOrWhiteSpace(Values));

    /// <summary>
    /// Resolve to a flat weight array using skeleton bone names.
    /// Returns count and space-separated values matching the raw format.
    /// Bones not listed in Named default to the specified default weight.
    /// When BoneCount is set, the output is truncated to that length.
    /// </summary>
    public (int count, string values) Resolve(List<string> boneNames, string defaultWeight = "0.000000")
    {
        if (!IsNamed)
            return (Count, Values);

        int outputCount = BoneCount ?? boneNames.Count;
        var weights = new string[outputCount];
        for (int i = 0; i < outputCount; i++)
            weights[i] = defaultWeight;

        foreach (var (boneName, weight) in Named!)
        {
            int idx = boneNames.IndexOf(boneName);
            if (idx < 0)
            {
                Console.Error.WriteLine($"  WARNING: Unknown bone '{boneName}' in named bone weights");
                continue;
            }
            if (idx < outputCount)
                weights[idx] = weight;
        }

        return (outputCount, string.Join(' ', weights));
    }
}
