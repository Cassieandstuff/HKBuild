using System;
using System.Collections.Generic;
using System.IO;
using HKBuild.Models;

namespace HKBuild;

/// <summary>
/// Compresses an AnimationDef into the hkaSplineCompressedAnimation binary blob.
///
/// Binary layout per block (from HavokLib hka_spline_decompressor.cpp):
///   [mask section]  — numTransformTracks×4 bytes + numFloatTracks×1 byte
///   [align to 4]
///   [per-track data, ordered: position, rotation, scale]
///     For each transform track (in track index order):
///       if posType == SPLINE:  BITS16 spline data for X,Y,Z
///       if rotType == SPLINE:  THREECOMP40 spline data
///       if rotType == STATIC:  5 bytes
///       [align to 4]
///       if scaleType == SPLINE: BITS16 spline data for X,Y,Z
///       if scaleType == STATIC: 6 bytes (3×BITS16 single value)
///   [per-float-track data]
///     if floatType == SPLINE: BITS8/BITS16 spline data
///     if floatType == STATIC: 1 or 2 bytes
///
/// Spline data layout (for BITS16, per component X/Y/Z):
///   float min     (4 bytes)
///   float max     (4 bytes)
///   uint16 numCtrlPts (must be >= degree+1)
///   uint8  degree
///   uint8[numKnots] knots  where numKnots = numCtrlPts + degree + 1
///   uint16[numCtrlPts] quantized control points
///
/// THREECOMP40 spline data:
///   uint16 numCtrlPts
///   uint8  degree
///   uint8[numKnots] knots
///   uint8[numCtrlPts×5] control points (5 bytes each)
///
/// We use degree=1 (linear) for exact interpolation at keyframe times.
/// Control points == sampled values at uniformly-spaced parameter positions.
/// Knot vector is clamped uniform: [0,0, 1/(n-1), 2/(n-1), ..., 1, 1].
/// </summary>
public static class SplineAnimCompressor
{
    public struct CompressedResult
    {
        public int NumFrames;
        public int NumBlocks;
        public int MaxFramesPerBlock;
        public float BlockDuration;
        public float BlockInverseDuration;
        public float FrameDuration;
        public int MaskAndQuantizationSize;
        public List<uint> BlockOffsets;
        public byte[] Data;
    }

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    public static CompressedResult Compress(AnimationDef anim, int fps = 30)
    {
        int numTransform = anim.Tracks.Count;
        int numFloat = anim.FloatTracks.Count;
        int maxFpb = anim.Compression.MaxFramesPerBlock;

        // Total frames (inclusive of frame 0 and the final frame).
        int numFrames = (int)Math.Round(anim.Duration * fps) + 1;
        float frameDuration = 1.0f / fps;

        int numBlocks = (numFrames + maxFpb - 2) / (maxFpb - 1);
        if (numBlocks < 1) numBlocks = 1;
        int framesPerBlock = maxFpb; // actual block uses maxFramesPerBlock frames

        float blockDuration = frameDuration * (framesPerBlock - 1);
        float blockInvDuration = 1.0f / blockDuration;

        // maskAndQuantizationSize: exactly numTransform×4 + numFloat×1
        int maskAndQuantSize = numTransform * 4 + numFloat * 1;

        // Pre-sample all tracks at each frame time
        var transFrames = SampleTransformTracks(anim, numFrames, frameDuration);
        var floatFrames = SampleFloatTracks(anim, numFrames, frameDuration);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        var blockOffsets = new List<uint>();

        for (int b = 0; b < numBlocks; b++)
        {
            int blockStart = b * (framesPerBlock - 1);
            int blockEnd = Math.Min(blockStart + framesPerBlock - 1, numFrames - 1);
            int framesInBlock = blockEnd - blockStart + 1;

            blockOffsets.Add((uint)ms.Position);
            WriteBlock(bw, transFrames, floatFrames, numTransform, numFloat,
                       blockStart, framesInBlock, anim.Compression);
        }

        bw.Flush();
        byte[] data = ms.ToArray();

        return new CompressedResult
        {
            NumFrames    = numFrames,
            NumBlocks    = numBlocks,
            MaxFramesPerBlock = maxFpb,
            BlockDuration       = blockDuration,
            BlockInverseDuration = blockInvDuration,
            FrameDuration = frameDuration,
            MaskAndQuantizationSize = maskAndQuantSize,
            BlockOffsets = blockOffsets,
            Data = data
        };
    }

    // -------------------------------------------------------------------------
    // Frame sampling
    // -------------------------------------------------------------------------

    private struct TransformFrame
    {
        public float Tx, Ty, Tz;
        public float Rx, Ry, Rz, Rw;
        public float Sx, Sy, Sz;
    }

    private static TransformFrame[][] SampleTransformTracks(AnimationDef anim, int numFrames, float frameDuration)
    {
        var result = new TransformFrame[anim.Tracks.Count][];
        for (int t = 0; t < anim.Tracks.Count; t++)
        {
            var track = anim.Tracks[t];
            result[t] = new TransformFrame[numFrames];
            for (int f = 0; f < numFrames; f++)
            {
                float time = f * frameDuration;
                var pos   = SampleVec3(track.Translation, time);
                var rot   = SampleQuat(track.Rotation, time);
                var scale = SampleVec3(track.Scale, time, defaultVal: 1f);
                result[t][f] = new TransformFrame
                {
                    Tx = pos[0], Ty = pos[1], Tz = pos[2],
                    Rx = rot[0], Ry = rot[1], Rz = rot[2], Rw = rot[3],
                    Sx = scale[0], Sy = scale[1], Sz = scale[2]
                };
            }
        }
        return result;
    }

    private static float[][] SampleFloatTracks(AnimationDef anim, int numFrames, float frameDuration)
    {
        var result = new float[anim.FloatTracks.Count][];
        for (int t = 0; t < anim.FloatTracks.Count; t++)
        {
            var track = anim.FloatTracks[t];
            result[t] = new float[numFrames];
            for (int f = 0; f < numFrames; f++)
            {
                float time = f * frameDuration;
                result[t][f] = SampleFloat(track.Keyframes, time);
            }
        }
        return result;
    }

    // -------------------------------------------------------------------------
    // Keyframe interpolation
    // -------------------------------------------------------------------------

    private static float[] SampleVec3(List<Vec3Keyframe>? kfs, float time, float defaultVal = 0f)
    {
        if (kfs == null || kfs.Count == 0)
            return new[] { defaultVal, defaultVal, defaultVal };
        if (kfs.Count == 1 || time <= kfs[0].Time)
            return new[] { kfs[0].Value[0], kfs[0].Value[1], kfs[0].Value[2] };
        if (time >= kfs[^1].Time)
            return new[] { kfs[^1].Value[0], kfs[^1].Value[1], kfs[^1].Value[2] };

        int i = 0;
        while (i < kfs.Count - 2 && kfs[i + 1].Time <= time) i++;
        float t = (time - kfs[i].Time) / (kfs[i + 1].Time - kfs[i].Time);
        return new[]
        {
            Lerp(kfs[i].Value[0], kfs[i+1].Value[0], t),
            Lerp(kfs[i].Value[1], kfs[i+1].Value[1], t),
            Lerp(kfs[i].Value[2], kfs[i+1].Value[2], t)
        };
    }

    private static float[] SampleQuat(List<QuatKeyframe>? kfs, float time)
    {
        if (kfs == null || kfs.Count == 0)
            return new[] { 0f, 0f, 0f, 1f };
        if (kfs.Count == 1 || time <= kfs[0].Time)
            return new[] { kfs[0].Value[0], kfs[0].Value[1], kfs[0].Value[2], kfs[0].Value[3] };
        if (time >= kfs[^1].Time)
            return new[] { kfs[^1].Value[0], kfs[^1].Value[1], kfs[^1].Value[2], kfs[^1].Value[3] };

        int i = FindKeyframePairQuat(kfs, time);
        float t = (time - kfs[i].Time) / (kfs[i + 1].Time - kfs[i].Time);
        return NLerp(
            kfs[i].Value[0], kfs[i].Value[1], kfs[i].Value[2], kfs[i].Value[3],
            kfs[i+1].Value[0], kfs[i+1].Value[1], kfs[i+1].Value[2], kfs[i+1].Value[3],
            t);
    }

    private static float SampleFloat(List<FloatKeyframe> kfs, float time)
    {
        if (kfs.Count == 0) return 0f;
        if (kfs.Count == 1 || time <= kfs[0].Time) return kfs[0].Value;
        if (time >= kfs[^1].Time) return kfs[^1].Value;
        int i = 0;
        while (i < kfs.Count - 2 && kfs[i + 1].Time <= time) i++;
        float t = (time - kfs[i].Time) / (kfs[i + 1].Time - kfs[i].Time);
        return Lerp(kfs[i].Value, kfs[i + 1].Value, t);
    }

    private static int FindKeyframePairQuat(List<QuatKeyframe> kfs, float time)
    {
        int i = 0;
        while (i < kfs.Count - 2 && kfs[i + 1].Time <= time) i++;
        return i;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float[] NLerp(float ax, float ay, float az, float aw,
                                  float bx, float by, float bz, float bw, float t)
    {
        // Ensure shortest path
        float dot = ax*bx + ay*by + az*bz + aw*bw;
        if (dot < 0f) { bx = -bx; by = -by; bz = -bz; bw = -bw; }
        float rx = ax + (bx - ax) * t;
        float ry = ay + (by - ay) * t;
        float rz = az + (bz - az) * t;
        float rw = aw + (bw - aw) * t;
        float len = MathF.Sqrt(rx*rx + ry*ry + rz*rz + rw*rw);
        if (len < 1e-6f) return new[] { 0f, 0f, 0f, 1f };
        return new[] { rx/len, ry/len, rz/len, rw/len };
    }

    // -------------------------------------------------------------------------
    // Block writer
    // -------------------------------------------------------------------------

    private static void WriteBlock(
        BinaryWriter bw,
        TransformFrame[][] transFrames,
        float[][] floatFrames,
        int numTransform,
        int numFloat,
        int blockStart,
        int framesInBlock,
        AnimCompressionParams comp)
    {
        // Determine channel types for each track in this block
        var posMask    = new ChannelType[numTransform];
        var rotMask    = new ChannelType[numTransform];
        var scaleMask  = new ChannelType[numTransform];
        var floatMask  = new FloatChannelType[numFloat];

        for (int t = 0; t < numTransform; t++)
        {
            posMask[t]   = ClassifyTranslation(transFrames[t], blockStart, framesInBlock, comp.TranslationTolerance);
            rotMask[t]   = ClassifyRotation(transFrames[t], blockStart, framesInBlock, comp.RotationTolerance);
            scaleMask[t] = ClassifyScale(transFrames[t], blockStart, framesInBlock, comp.ScaleTolerance);
        }
        for (int t = 0; t < numFloat; t++)
            floatMask[t] = ClassifyFloat(floatFrames[t], blockStart, framesInBlock);

        // Write mask section (4 bytes per transform track, 1 byte per float track)
        for (int t = 0; t < numTransform; t++)
            WriteMask(bw, posMask[t], rotMask[t], scaleMask[t]);
        for (int t = 0; t < numFloat; t++)
            WriteFloatMask(bw, floatMask[t]);

        // Align to 4
        AlignTo(bw, 4);

        // Write per-track data
        for (int t = 0; t < numTransform; t++)
        {
            WriteTranslationData(bw, transFrames[t], blockStart, framesInBlock, posMask[t], comp.TranslationDegree);
            WriteRotationData(bw, transFrames[t], blockStart, framesInBlock, rotMask[t], comp.RotationDegree);
            AlignTo(bw, 4);
            WriteScaleData(bw, transFrames[t], blockStart, framesInBlock, scaleMask[t], comp.ScaleDegree);
        }

        for (int t = 0; t < numFloat; t++)
            WriteFloatData(bw, floatFrames[t], blockStart, framesInBlock, floatMask[t]);
    }

    // -------------------------------------------------------------------------
    // Channel classification
    // -------------------------------------------------------------------------

    private enum ChannelType { Identity, Static, Dynamic }
    private enum FloatChannelType { Identity, Static, Dynamic }

    private static bool IsIdentityTranslation(TransformFrame f) =>
        MathF.Abs(f.Tx) < 1e-5f && MathF.Abs(f.Ty) < 1e-5f && MathF.Abs(f.Tz) < 1e-5f;

    private static bool IsIdentityRotation(TransformFrame f) =>
        MathF.Abs(f.Rx) < 1e-5f && MathF.Abs(f.Ry) < 1e-5f && MathF.Abs(f.Rz) < 1e-5f &&
        MathF.Abs(f.Rw - 1f) < 1e-5f;

    private static bool IsIdentityScale(TransformFrame f) =>
        MathF.Abs(f.Sx - 1f) < 1e-5f && MathF.Abs(f.Sy - 1f) < 1e-5f && MathF.Abs(f.Sz - 1f) < 1e-5f;

    private static ChannelType ClassifyTranslation(TransformFrame[] frames, int start, int count, float tol)
    {
        var f0 = frames[start];
        bool allIdentity = true;
        bool allSame = true;
        for (int i = 0; i < count; i++)
        {
            var f = frames[start + i];
            if (!IsIdentityTranslation(f)) allIdentity = false;
            if (MathF.Abs(f.Tx - f0.Tx) > tol || MathF.Abs(f.Ty - f0.Ty) > tol || MathF.Abs(f.Tz - f0.Tz) > tol)
                allSame = false;
        }
        if (allIdentity) return ChannelType.Identity;
        if (allSame) return ChannelType.Static;
        return ChannelType.Dynamic;
    }

    private static ChannelType ClassifyRotation(TransformFrame[] frames, int start, int count, float tol)
    {
        var f0 = frames[start];
        bool allIdentity = true;
        bool allSame = true;
        for (int i = 0; i < count; i++)
        {
            var f = frames[start + i];
            if (!IsIdentityRotation(f)) allIdentity = false;
            float dot = f.Rx*f0.Rx + f.Ry*f0.Ry + f.Rz*f0.Rz + f.Rw*f0.Rw;
            if (MathF.Abs(dot) < 1f - tol) allSame = false;
        }
        if (allIdentity) return ChannelType.Identity;
        if (allSame) return ChannelType.Static;
        return ChannelType.Dynamic;
    }

    private static ChannelType ClassifyScale(TransformFrame[] frames, int start, int count, float tol)
    {
        var f0 = frames[start];
        bool allIdentity = true;
        bool allSame = true;
        for (int i = 0; i < count; i++)
        {
            var f = frames[start + i];
            if (!IsIdentityScale(f)) allIdentity = false;
            if (MathF.Abs(f.Sx - f0.Sx) > tol || MathF.Abs(f.Sy - f0.Sy) > tol || MathF.Abs(f.Sz - f0.Sz) > tol)
                allSame = false;
        }
        if (allIdentity) return ChannelType.Identity;
        if (allSame) return ChannelType.Static;
        return ChannelType.Dynamic;
    }

    private static FloatChannelType ClassifyFloat(float[] frames, int start, int count)
    {
        float v0 = frames[start];
        if (MathF.Abs(v0) < 1e-5f)
        {
            bool allZero = true;
            for (int i = 1; i < count; i++)
                if (MathF.Abs(frames[start + i]) > 1e-5f) { allZero = false; break; }
            if (allZero) return FloatChannelType.Identity;
        }
        bool allSame = true;
        for (int i = 1; i < count; i++)
            if (MathF.Abs(frames[start + i] - v0) > 1e-5f) { allSame = false; break; }
        return allSame ? FloatChannelType.Static : FloatChannelType.Dynamic;
    }

    // -------------------------------------------------------------------------
    // Mask encoding
    //
    // TransformMask layout (4 bytes):
    //   byte 0: quantizationTypes
    //     bits 1-0: pos quantization   (0=BITS8, 1=BITS16)
    //     bits 5-2: rot quantization   (stored as rawVal; actual = rawVal+2,
    //               so THREECOMP40=1 → rawVal=1 → stored bits = 0001 → shifted = 0100 = 4 → 0x04<<2)
    //               HavokLib formula: qType = (quantizationTypes >> 2) & 0xF; actual = qType + 2
    //               So to store THREECOMP40 (enum=1): rawVal = 1-2 = -1? No — let's use the actual values:
    //               Looking at HavokLib: qType = (quantizationTypes >> 2) & 0xF
    //               actual RotationQuantization = qType + 2 (HavokLib adds 2 as offset)
    //               THREECOMP40 has enum value 1, so qType = 1-2 = -1 → that can't be right
    //               Re-reading: HavokLib uses "RotationQuantizationType::THREECOMP40" which is enum value 1.
    //               The stored qType+2 = actual. So actual=1 means qType=−1, impossible.
    //               Actually reading the decompressor more carefully:
    //               bits 5-2 store the RotationQuantization enum value directly (not offset by 2).
    //               The "+2" in HavokLib is for ScalarQuantization (BITS8=0 → min range start).
    //               Let's store the raw enum value in bits 5-2.
    //     bits 7-6: scale quantization (0=BITS8, 1=BITS16)
    //
    //   byte 1: positionTypes (FlagOffset bits)
    //     bit 0: staticX, bit 1: staticY, bit 2: staticZ, bit 3: staticW
    //     bit 4: splineX, bit 5: splineY, bit 6: splineZ, bit 7: splineW
    //
    //   byte 2: rotationTypes
    //     bits 7-4 nonzero → DYNAMIC; bits 3-0 nonzero → STATIC; both 0 → IDENTITY
    //     For DYNAMIC: set bit 4 (0x10)
    //     For STATIC:  set bit 0 (0x01)
    //
    //   byte 3: scaleTypes (same FlagOffset layout as positionTypes)
    //
    // From vanilla 0x45: quantizationTypes=0x45=0b01000101
    //   bits 1-0 = 01 = BITS16 for pos
    //   bits 5-2 = 0001 = 1 → +2 offset in HavokLib means 3? No.
    //   0x45 = 69 = 0b01000101
    //   bits 7-6 = 01 = BITS16 for scale
    //   bits 5-2 = 0001 = 1... but THREECOMP40 enum = 1, stored raw = 1? Then 1<<2 = 4
    //   Let me re-examine: 0x45 = 0100 0101
    //   [7:6] = 01 → BITS16 scale
    //   [5:2] = 0001 → raw rot qtype stored = 1 = THREECOMP40
    //   [1:0] = 01 → BITS16 pos
    //   So yes: raw enum value stored directly in bits 5-2. No offset.
    // -------------------------------------------------------------------------

    private static void WriteMask(BinaryWriter bw, ChannelType pos, ChannelType rot, ChannelType scale)
    {
        // byte 0: quantizationTypes
        // Use BITS16 (=1) for pos and scale; THREECOMP40 (=1) for rot stored in bits 5-2
        byte posQ   = 1; // BITS16
        byte rotQ   = 1; // THREECOMP40 enum value = 1, stored raw in bits[5:2]
        byte scaleQ = 1; // BITS16
        byte qTypes = (byte)((posQ & 0x3) | ((rotQ & 0xF) << 2) | ((scaleQ & 0x3) << 6));

        // byte 1: positionTypes
        byte posTypes = 0;
        if (pos == ChannelType.Static)  posTypes = 0x07; // staticX|staticY|staticZ = bits 0,1,2
        if (pos == ChannelType.Dynamic) posTypes = 0x70; // splineX|splineY|splineZ = bits 4,5,6

        // byte 2: rotationTypes
        byte rotTypes = 0;
        if (rot == ChannelType.Static)  rotTypes = 0x01; // bits 3-0 nonzero
        if (rot == ChannelType.Dynamic) rotTypes = 0x10; // bits 7-4 nonzero

        // byte 3: scaleTypes
        byte scaleTypes = 0;
        if (scale == ChannelType.Static)  scaleTypes = 0x07;
        if (scale == ChannelType.Dynamic) scaleTypes = 0x70;

        bw.Write(qTypes);
        bw.Write(posTypes);
        bw.Write(rotTypes);
        bw.Write(scaleTypes);
    }

    private static void WriteFloatMask(BinaryWriter bw, FloatChannelType ft)
    {
        // 1 byte: bits 7-4 nonzero = DYNAMIC, bits 3-0 nonzero = STATIC, 0 = IDENTITY
        // Use BITS16 quantization (stored in upper nibble for dynamic, lower for static)
        // Float mask encoding: bit0=static, bit4=spline, bits 6-7 = quantization (0=BITS8,1=BITS16)
        // We use BITS16 for dynamic, single value for static.
        // IDENTITY=0x00, STATIC=0x01, DYNAMIC=0x50 (splineX=0x10 | BITS16<<6=0x40)
        byte mask = ft switch
        {
            FloatChannelType.Identity => 0x00,
            FloatChannelType.Static   => 0x01,
            FloatChannelType.Dynamic  => 0x50,
            _ => 0x00
        };
        bw.Write(mask);
    }

    // -------------------------------------------------------------------------
    // Track data writers
    // -------------------------------------------------------------------------

    private static void WriteTranslationData(BinaryWriter bw, TransformFrame[] frames,
        int start, int count, ChannelType type, int degree)
    {
        if (type == ChannelType.Identity) return;

        if (type == ChannelType.Static)
        {
            // 3 × (float min + float max + uint16 = 0) — but for static we write a single quantized value
            // Static translation: write 3×BITS16 (min, max, single ctrl pt 0)
            WriteStaticBits16Vec3(bw, frames[start].Tx, frames[start].Ty, frames[start].Tz);
            return;
        }

        // Dynamic: full BITS16 spline per component
        float[] xs = new float[count];
        float[] ys = new float[count];
        float[] zs = new float[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = frames[start + i].Tx;
            ys[i] = frames[start + i].Ty;
            zs[i] = frames[start + i].Tz;
        }
        WriteBits16Spline(bw, xs, degree);
        WriteBits16Spline(bw, ys, degree);
        WriteBits16Spline(bw, zs, degree);
    }

    private static void WriteRotationData(BinaryWriter bw, TransformFrame[] frames,
        int start, int count, ChannelType type, int degree)
    {
        if (type == ChannelType.Identity) return;

        if (type == ChannelType.Static)
        {
            WriteThreeComp40Single(bw, frames[start].Rx, frames[start].Ry,
                                       frames[start].Rz, frames[start].Rw);
            return;
        }

        // Dynamic: THREECOMP40 spline
        WriteThreeComp40Spline(bw, frames, start, count, degree);
    }

    private static void WriteScaleData(BinaryWriter bw, TransformFrame[] frames,
        int start, int count, ChannelType type, int degree)
    {
        if (type == ChannelType.Identity) return;

        if (type == ChannelType.Static)
        {
            WriteStaticBits16Vec3(bw, frames[start].Sx, frames[start].Sy, frames[start].Sz);
            return;
        }

        float[] xs = new float[count];
        float[] ys = new float[count];
        float[] zs = new float[count];
        for (int i = 0; i < count; i++)
        {
            xs[i] = frames[start + i].Sx;
            ys[i] = frames[start + i].Sy;
            zs[i] = frames[start + i].Sz;
        }
        WriteBits16Spline(bw, xs, degree);
        WriteBits16Spline(bw, ys, degree);
        WriteBits16Spline(bw, zs, degree);
    }

    private static void WriteFloatData(BinaryWriter bw, float[] frames,
        int start, int count, FloatChannelType type)
    {
        if (type == FloatChannelType.Identity) return;

        if (type == FloatChannelType.Static)
        {
            // Write a BITS16 single value: min=max=value, quantized=0
            float v = frames[start];
            bw.Write(v);      // min
            bw.Write(v);      // max
            bw.Write((ushort)1);  // numCtrlPts=1
            bw.Write((byte)1);    // degree=1
            bw.Write((byte)0);    // knot[0]=0
            bw.Write((byte)1);    // knot[1]=1  (numKnots = 1+1+1=3? no, numKnots=numCtrl+degree+1=1+1+1=3)
            bw.Write((byte)255);  // knot[2]=255
            bw.Write((ushort)0);  // ctrl pt = 0 (quantized at min)
            return;
        }

        // Dynamic float: BITS16 spline
        float[] vals = new float[count];
        for (int i = 0; i < count; i++) vals[i] = frames[start + i];
        WriteBits16Spline(bw, vals, 1);
    }

    // -------------------------------------------------------------------------
    // Static value helpers
    // -------------------------------------------------------------------------

    private static void WriteStaticBits16Vec3(BinaryWriter bw, float x, float y, float z)
    {
        WriteBits16Single(bw, x);
        WriteBits16Single(bw, y);
        WriteBits16Single(bw, z);
    }

    private static void WriteBits16Single(BinaryWriter bw, float v)
    {
        // A single control-point spline: min=max=v, 1 ctrl pt, degree=1
        // knot vector for degree=1, 1 ctrl pt: [0,0,1,1] → 4 bytes? Actually numKnots = 1+1+1 = 3
        // Wait — for a single control point with degree 1: numKnots = numCtrlPts + degree + 1 = 1+1+1 = 3
        // But a valid B-spline requires numCtrlPts >= degree+1, so 1 >= 2 is false.
        // Use degree=1, numCtrlPts=2, both ctrl pts at the same value.
        bw.Write(v);        // min
        bw.Write(v);        // max
        bw.Write((ushort)2); // numCtrlPts=2 (minimum valid for degree=1)
        bw.Write((byte)1);   // degree=1
        // numKnots = 2+1+1 = 4: clamped [0,0,255,255]
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((byte)255);
        bw.Write((byte)255);
        bw.Write((ushort)0); // ctrl[0] = min
        bw.Write((ushort)0); // ctrl[1] = min (same value)
    }

    // -------------------------------------------------------------------------
    // BITS16 spline writer
    //
    // Layout:
    //   float min
    //   float max
    //   uint16 numCtrlPts
    //   uint8  degree
    //   uint8[numCtrlPts + degree + 1] knots  (clamped uniform, 0–255)
    //   uint16[numCtrlPts] quantized control points
    // -------------------------------------------------------------------------

    private static void WriteBits16Spline(BinaryWriter bw, float[] values, int degree)
    {
        // For degree=1 linear B-spline, control points = the sampled values themselves.
        // numCtrlPts = values.Length (one per frame in the block).
        // Ensure minimum of degree+1 control points.
        int n = Math.Max(values.Length, degree + 1);
        float[] ctrlPts = new float[n];
        for (int i = 0; i < values.Length; i++) ctrlPts[i] = values[i];
        for (int i = values.Length; i < n; i++) ctrlPts[i] = values[^1];

        float minV = ctrlPts[0], maxV = ctrlPts[0];
        for (int i = 1; i < n; i++)
        {
            if (ctrlPts[i] < minV) minV = ctrlPts[i];
            if (ctrlPts[i] > maxV) maxV = ctrlPts[i];
        }
        float range = maxV - minV;
        if (range < 1e-7f) range = 1e-7f;

        bw.Write(minV);
        bw.Write(maxV);
        bw.Write((ushort)n);
        bw.Write((byte)degree);

        // Clamped uniform knot vector: degree+1 zeros, then interior, then degree+1 ones (255)
        int numKnots = n + degree + 1;
        byte[] knots = BuildKnotVector(n, degree);
        for (int i = 0; i < numKnots; i++) bw.Write(knots[i]);

        // Quantize control points
        for (int i = 0; i < n; i++)
        {
            float normalized = (ctrlPts[i] - minV) / range;
            ushort q = (ushort)Math.Clamp((int)(normalized * 65535f + 0.5f), 0, 65535);
            bw.Write(q);
        }
    }

    // -------------------------------------------------------------------------
    // THREECOMP40 quaternion encoding
    //
    // 40-bit layout (5 bytes, little-endian):
    //   bits  0-11: Va (12-bit component a, 0–4095)
    //   bits 12-23: Vb (12-bit component b, 0–4095)
    //   bits 24-35: Vc (12-bit component c, 0–4095)
    //   bits 36-37: resultShift (which component was dropped: 0=W,1=X,2=Y,3=Z)
    //   bit  38:    sign of the dropped component (0=positive)
    //   bit  39:    unused (0)
    //
    // The three stored components are the three smallest, each mapped [-1/√2, 1/√2] → [0, 4095].
    // The dropped (largest abs) component is reconstructed as sqrt(1 - a² - b² - c²).
    // -------------------------------------------------------------------------

    private static void WriteThreeComp40Spline(BinaryWriter bw, TransformFrame[] frames,
        int start, int count, int degree)
    {
        int n = Math.Max(count, degree + 1);
        bw.Write((ushort)n);
        bw.Write((byte)degree);

        int numKnots = n + degree + 1;
        byte[] knots = BuildKnotVector(n, degree);
        for (int i = 0; i < numKnots; i++) bw.Write(knots[i]);

        for (int i = 0; i < n; i++)
        {
            int fi = start + Math.Min(i, count - 1);
            var f = frames[fi];
            WriteThreeComp40Single(bw, f.Rx, f.Ry, f.Rz, f.Rw);
        }
    }

    private static void WriteThreeComp40Single(BinaryWriter bw, float rx, float ry, float rz, float rw)
    {
        // Normalize quaternion
        float len = MathF.Sqrt(rx*rx + ry*ry + rz*rz + rw*rw);
        if (len < 1e-6f) { rx = 0f; ry = 0f; rz = 0f; rw = 1f; }
        else { rx /= len; ry /= len; rz /= len; rw /= len; }

        // Find component with largest absolute value to drop
        float[] comps = { rx, ry, rz, rw };
        float[] absComps = { MathF.Abs(rx), MathF.Abs(ry), MathF.Abs(rz), MathF.Abs(rw) };
        int dropIdx = 0;
        for (int i = 1; i < 4; i++)
            if (absComps[i] > absComps[dropIdx]) dropIdx = i;

        // Ensure dropped component is positive (flip quaternion if needed)
        if (comps[dropIdx] < 0f) { rx = -rx; ry = -ry; rz = -rz; rw = -rw; }
        comps = new[] { rx, ry, rz, rw };

        // The sign bit: 0 = positive dropped component (after possible flip, always positive)
        byte signBit = 0;
        byte resultShift = (byte)dropIdx; // 0=W,1=X,2=Y,3=Z

        // Collect the three non-dropped components in order
        float[] stored = new float[3];
        int si = 0;
        for (int i = 0; i < 4; i++)
            if (i != dropIdx) stored[si++] = comps[i];

        // Quantize each to 12 bits: range [-1/√2, 1/√2] → [0, 4095]
        const float kScale = 1f / 1.41421356f; // 1/sqrt(2)
        int Va = QuantTo12Bit(stored[0], kScale);
        int Vb = QuantTo12Bit(stored[1], kScale);
        int Vc = QuantTo12Bit(stored[2], kScale);

        // Pack 40 bits little-endian
        // bits 0-11 = Va, 12-23 = Vb, 24-35 = Vc, 36-37 = resultShift, 38 = signBit, 39 = 0
        byte b0 = (byte)(Va & 0xFF);
        byte b1 = (byte)(((Va >> 8) & 0xF) | ((Vb & 0xF) << 4));
        byte b2 = (byte)((Vb >> 4) & 0xFF);
        byte b3 = (byte)(Vc & 0xFF);
        byte b4 = (byte)(((Vc >> 8) & 0xF) | ((resultShift & 0x3) << 4) | ((signBit & 0x1) << 6));

        bw.Write(b0); bw.Write(b1); bw.Write(b2); bw.Write(b3); bw.Write(b4);
    }

    private static int QuantTo12Bit(float v, float halfRange)
    {
        // Map [-halfRange, +halfRange] to [0, 4095]
        float normalized = (v + halfRange) / (2f * halfRange);
        return Math.Clamp((int)(normalized * 4095f + 0.5f), 0, 4095);
    }

    // -------------------------------------------------------------------------
    // Knot vector builder
    //
    // Clamped uniform knot vector, mapped to byte range [0, 255].
    // numKnots = numCtrlPts + degree + 1
    // First degree+1 values = 0, last degree+1 values = 255,
    // interior values uniformly spaced.
    // -------------------------------------------------------------------------

    private static byte[] BuildKnotVector(int numCtrlPts, int degree)
    {
        int numKnots = numCtrlPts + degree + 1;
        byte[] knots = new byte[numKnots];

        // First degree+1 clamped at 0
        for (int i = 0; i <= degree; i++)
            knots[i] = 0;

        // Last degree+1 clamped at 255
        for (int i = numKnots - degree - 1; i < numKnots; i++)
            knots[i] = 255;

        // Interior knots uniformly spaced
        int numInterior = numCtrlPts - degree - 1;
        if (numInterior > 0)
        {
            for (int i = 0; i < numInterior; i++)
            {
                float t = (float)(i + 1) / (numInterior + 1);
                knots[degree + 1 + i] = (byte)Math.Clamp((int)(t * 255f), 1, 254);
            }
        }

        return knots;
    }

    // -------------------------------------------------------------------------
    // Alignment helper
    // -------------------------------------------------------------------------

    private static void AlignTo(BinaryWriter bw, int alignment)
    {
        long pos = bw.BaseStream.Position;
        long rem = pos % alignment;
        if (rem != 0)
        {
            for (long i = 0; i < alignment - rem; i++)
                bw.Write((byte)0);
        }
    }
}
