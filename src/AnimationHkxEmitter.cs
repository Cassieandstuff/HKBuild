using HKBuild.Models;
using HKX2E;

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
            type                    = (int)AnimationType.HK_SPLINE_COMPRESSED_ANIMATION,
            duration                = anim.Duration,
            numberOfTransformTracks = anim.Tracks.Count,
            numberOfFloatTracks     = anim.FloatTracks.Count,
            extractedMotion         = null,
            annotationTracks        = Array.Empty<hkaAnnotationTrack>(),

            numFrames               = compressed.NumFrames,
            numBlocks               = compressed.NumBlocks,
            maxFramesPerBlock       = compressed.MaxFramesPerBlock,
            maskAndQuantizationSize = compressed.MaskAndQuantizationSize,
            blockDuration           = compressed.BlockDuration,
            blockInverseDuration    = compressed.BlockInverseDuration,
            frameDuration           = compressed.FrameDuration,

            blockOffsets            = compressed.BlockOffsets,
            floatBlockOffsets       = Array.Empty<uint>(),
            transformOffsets        = Array.Empty<uint>(),
            floatOffsets            = Array.Empty<uint>(),

            data                    = compressed.Data,
            endian                  = 0, // little-endian
        };

        // Build binding — empty transformTrackToBoneIndices means identity mapping (track i → bone i)
        var binding = new hkaAnimationBinding
        {
            originalSkeletonName         = anim.Skeleton,
            animation                    = splineAnim,
            transformTrackToBoneIndices  = Array.Empty<short>(),
            floatTrackToFloatSlotIndices = Array.Empty<short>(),
            blendHint                    = 0,
        };

        // Build container
        var container = new hkaAnimationContainer
        {
            skeletons   = Array.Empty<hkaSkeleton>(),
            animations  = new hkaAnimation[] { splineAnim },
            bindings    = new hkaAnimationBinding[] { binding },
            attachments = Array.Empty<hkaBoneAttachment>(),
            skins       = Array.Empty<hkaMeshBinding>(),
        };

        // Wrap in root level container
        var root = new hkRootLevelContainer
        {
            namedVariants = new[]
            {
                new hkRootLevelContainerNamedVariant
                {
                    name      = "Merged Animation Container",
                    className = "hkaAnimationContainer",
                    variant   = container,
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
