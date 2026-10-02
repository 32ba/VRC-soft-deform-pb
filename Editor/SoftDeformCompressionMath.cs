using System;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformCompressionMath
    {
        // A conservative upper bound includes Bezier control points, not just key values.
        // This also covers overshoot and weighted curve tangents without resampling the curve.
        internal static float CurveUpperBound(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return 1;
            var keys = curve.keys;
            float maximum = 1;
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                if (float.IsNaN(key.value) || float.IsInfinity(key.value))
                    throw new InvalidOperationException("[Soft Deform PB] Max Squish curve has a non-finite value.");
                maximum = Mathf.Max(maximum, key.value);
                if (i + 1 == keys.Length) continue;
                var next = keys[i + 1];
                float dt = next.time - key.time;
                if (float.IsInfinity(key.outTangent) || float.IsInfinity(next.inTangent)) continue; // stepped segment
                float a = key.value + key.outTangent * dt * ((key.weightedMode & WeightedMode.Out) != 0 ? key.outWeight : 1f / 3);
                float b = next.value - next.inTangent * dt * ((next.weightedMode & WeightedMode.In) != 0 ? next.inWeight : 1f / 3);
                if (float.IsNaN(a) || float.IsNaN(b) || float.IsInfinity(a) || float.IsInfinity(b))
                    throw new InvalidOperationException("[Soft Deform PB] Max Squish curve has an invalid tangent.");
                maximum = Mathf.Max(maximum, a, b);
            }
            return maximum;
        }

        internal static float EffectiveMaxSquish(float requested, float minimum, float curveMaximum)
        {
            return minimum == 0 ? requested : Mathf.Min(requested, (1 - minimum) / Mathf.Max(1, curveMaximum));
        }

        internal static float GravityCompression(float requested, float minimum, float maximumPhysicalCompression)
        {
            if (minimum == 0) return requested;
            float physicalMinimum = Mathf.Max(minimum, 1 - maximumPhysicalCompression);
            return Mathf.Min(requested, Mathf.Max(0, 1 - minimum / physicalMinimum));
        }

        internal static Vector3 LateralScale(Vector3 scale, SoftDeformAxisMap axes, float amount,
            float depth, float retention, float verticalShare, float minimum)
        {
            float originalWidth = scale[axes.Horizontal];
            float width = Mathf.Max(minimum, originalWidth * (1 - depth * Mathf.Clamp01(amount)));
            float ratio = width / originalWidth;
            scale[axes.Horizontal] = width;
            scale[axes.Vertical] = Mathf.Max(minimum,
                scale[axes.Vertical] * Mathf.Pow(ratio, -retention * verticalShare));
            scale[axes.Longitudinal] = Mathf.Max(minimum,
                scale[axes.Longitudinal] * Mathf.Pow(ratio, -retention * (1 - verticalShare)));
            return scale;
        }
    }
}
