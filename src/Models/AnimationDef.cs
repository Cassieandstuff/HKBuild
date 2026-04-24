using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace HKBuild.Models
{
    public class AnimationFile
    {
        [YamlMember(Alias = "animation")]
        public AnimationDef Animation { get; set; } = new();
    }

    public class AnimationDef
    {
        [YamlMember(Alias = "name")]
        public string Name { get; set; } = "";

        [YamlMember(Alias = "duration")]
        public float Duration { get; set; }

        [YamlMember(Alias = "skeleton")]
        public string Skeleton { get; set; } = "";

        [YamlMember(Alias = "compression")]
        public AnimCompressionParams Compression { get; set; } = new();

        [YamlMember(Alias = "tracks")]
        public List<AnimTrackDef> Tracks { get; set; } = new();

        [YamlMember(Alias = "floatTracks")]
        public List<AnimFloatTrackDef> FloatTracks { get; set; } = new();
    }

    public class AnimCompressionParams
    {
        [YamlMember(Alias = "rotationTolerance")]
        public float RotationTolerance { get; set; } = 0.001f;

        [YamlMember(Alias = "translationTolerance")]
        public float TranslationTolerance { get; set; } = 0.001f;

        [YamlMember(Alias = "scaleTolerance")]
        public float ScaleTolerance { get; set; } = 0.001f;

        [YamlMember(Alias = "rotationDegree")]
        public int RotationDegree { get; set; } = 3;

        [YamlMember(Alias = "translationDegree")]
        public int TranslationDegree { get; set; } = 1;

        [YamlMember(Alias = "scaleDegree")]
        public int ScaleDegree { get; set; } = 1;

        [YamlMember(Alias = "maxFramesPerBlock")]
        public int MaxFramesPerBlock { get; set; } = 256;
    }

    public class AnimTrackDef
    {
        [YamlMember(Alias = "bone")]
        public string Bone { get; set; } = "";

        [YamlMember(Alias = "translation")]
        public List<Vec3Keyframe>? Translation { get; set; }

        [YamlMember(Alias = "rotation")]
        public List<QuatKeyframe>? Rotation { get; set; }

        [YamlMember(Alias = "scale")]
        public List<Vec3Keyframe>? Scale { get; set; }
    }

    public class Vec3Keyframe
    {
        [YamlMember(Alias = "time")]
        public float Time { get; set; }

        // [x, y, z]
        [YamlMember(Alias = "value")]
        public List<float> Value { get; set; } = new() { 0f, 0f, 0f };
    }

    public class QuatKeyframe
    {
        [YamlMember(Alias = "time")]
        public float Time { get; set; }

        // [x, y, z, w]
        [YamlMember(Alias = "value")]
        public List<float> Value { get; set; } = new() { 0f, 0f, 0f, 1f };
    }

    public class AnimFloatTrackDef
    {
        [YamlMember(Alias = "name")]
        public string Name { get; set; } = "";

        [YamlMember(Alias = "keyframes")]
        public List<FloatKeyframe> Keyframes { get; set; } = new();
    }

    public class FloatKeyframe
    {
        [YamlMember(Alias = "time")]
        public float Time { get; set; }

        [YamlMember(Alias = "value")]
        public float Value { get; set; }
    }
}
