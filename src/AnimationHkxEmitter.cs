using HKBuild.Models;
using HKX2;

namespace HKBuild;

/// <summary>
/// Builds an hkRootLevelContainer → hkaAnimationContainer object graph from
/// a compressed animation result and serializes it to a Skyrim SE .hkx binary.
///
/// Pipeline:
///   AnimationDef → SplineAnimCompressor → CompressedResult
///   CompressedResult + AnimationDef → hkaSplineCompressedAnimation + hkaAnimationBinding
///   → hkaAnimationContainer → hkRootLevelContainer
///   → PackFileSerializer → .hkx binary
/// </summary>
public static class AnimationHkxEmitter
{
    public static void Emit(
        AnimationDef anim,
        SplineAnimCompressor.CompressedResult compressed,
        Stream output,
        int fps = 30)
    {
        // Build hkaSplineCompressedAnimation
        var splineAnim = new hkaSplineCompressedAnimation
        {
            m_type                    = (int)AnimationType.HK_SPLINE_COMPRESSED_ANIMATION,
            m_duration                = anim.Duration,
            m_numberOfTransformTracks = anim.Tracks.Count,
            m_numberOfFloatTracks     = anim.FloatTracks.Count,
            m_extractedMotion         = null,
            m_annotationTracks        = Array.Empty<hkaAnnotationTrack>(),

            m_numFrames               = compressed.NumFrames,
            m_numBlocks               = compressed.NumBlocks,
            m_maxFramesPerBlock       = compressed.MaxFramesPerBlock,
            m_maskAndQuantizationSize = compressed.MaskAndQuantizationSize,
            m_blockDuration           = compressed.BlockDuration,
            m_blockInverseDuration    = compressed.BlockInverseDuration,
            m_frameDuration           = compressed.FrameDuration,

            m_blockOffsets            = compressed.BlockOffsets,
            m_floatBlockOffsets       = Array.Empty<uint>(),
            m_transformOffsets        = Array.Empty<uint>(),
            m_floatOffsets            = Array.Empty<uint>(),

            m_data                    = compressed.Data,
            m_endian                  = 0, // little-endian
        };

        // Build binding — empty transformTrackToBoneIndices means identity mapping (track i → bone i)
        var binding = new hkaAnimationBinding
        {
            m_originalSkeletonName         = anim.Skeleton,
            m_animation                    = splineAnim,
            m_transformTrackToBoneIndices  = Array.Empty<short>(),
            m_floatTrackToFloatSlotIndices = Array.Empty<short>(),
            m_blendHint                    = 0,
        };

        // Build container
        var container = new hkaAnimationContainer
        {
            m_skeletons   = Array.Empty<hkaSkeleton>(),
            m_animations  = new hkaAnimation[] { splineAnim },
            m_bindings    = new hkaAnimationBinding[] { binding },
            m_attachments = Array.Empty<hkaBoneAttachment>(),
            m_skins       = Array.Empty<hkaMeshBinding>(),
        };

        // Wrap in root level container
        var root = new hkRootLevelContainer
        {
            m_namedVariants = new[]
            {
                new hkRootLevelContainerNamedVariant
                {
                    m_name      = "Merged Animation Container",
                    m_className = "hkaAnimationContainer",
                    m_variant   = container,
                }
            }
        };

        // Serialize to binary HKX
        var header = HKXHeader.SkyrimSE();
        var bw = new BinaryWriterEx(output);
        var ser = new PackFileSerializer();
        ser.Serialize(root, bw, header);
    }
}
