using System;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    public readonly struct SoftDeformAxisMap
    {
        public readonly int Longitudinal;
        public readonly int Horizontal;
        public readonly int Vertical;

        public SoftDeformAxisMap(int longitudinal, int horizontal, int vertical)
        {
            Longitudinal = longitudinal;
            Horizontal = horizontal;
            Vertical = vertical;
        }
    }

    public static class SoftDeformShapeMath
    {
        public static float RootLeverLength(float maxOffset, float maxAngleDegrees)
        {
            if (float.IsNaN(maxOffset) || float.IsInfinity(maxOffset) || maxOffset < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(maxOffset));
            if (float.IsNaN(maxAngleDegrees) || float.IsInfinity(maxAngleDegrees) ||
                maxAngleDegrees < 1.0f || maxAngleDegrees > 30.0f)
                throw new ArgumentOutOfRangeException(nameof(maxAngleDegrees));
            return maxOffset / (2.0f * Mathf.Sin(maxAngleDegrees * Mathf.Deg2Rad * 0.5f));
        }

        public static SoftDeformAxisMap InferAxes(Transform target, Vector3 localDirection)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            int longitudinal = DominantAxis(localDirection.sqrMagnitude > 1e-8f
                ? localDirection.normalized
                : Vector3.forward);

            int vertical = -1;
            float bestUp = float.NegativeInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == longitudinal) continue;
                float up = Mathf.Abs(Vector3.Dot(target.TransformDirection(AxisVector(axis)), Vector3.up));
                if (up > bestUp)
                {
                    bestUp = up;
                    vertical = axis;
                }
            }

            int horizontal = 3 - longitudinal - vertical;
            return new SoftDeformAxisMap(longitudinal, horizontal, vertical);
        }

        public static Vector3 EvaluateScale(
            Vector3 restScale,
            SoftDeformAxisMap axes,
            float squash,
            float stretch,
            float squashDepth,
            float stretchDepth,
            float volumeRetention,
            float horizontalShare)
        {
            float lengthRatio = EvaluateLengthRatio(
                squash,
                stretch,
                squashDepth,
                stretchDepth);

            float retention = Mathf.Clamp01(volumeRetention);
            float horizontalExponent = retention * Mathf.Clamp01(horizontalShare);
            float verticalExponent = retention * (1.0f - Mathf.Clamp01(horizontalShare));
            float horizontal = Mathf.Pow(lengthRatio, -horizontalExponent);
            float vertical = Mathf.Pow(lengthRatio, -verticalExponent);

            var result = restScale;
            SetAxis(ref result, axes.Horizontal, GetAxis(restScale, axes.Horizontal) * horizontal);
            SetAxis(ref result, axes.Vertical, GetAxis(restScale, axes.Vertical) * vertical);
            return result;
        }

        public static float EvaluateLengthRatio(
            float squash,
            float stretch,
            float squashDepth,
            float stretchDepth)
        {
            return Mathf.Clamp(
                (1.0f - Mathf.Clamp01(squash) * Mathf.Clamp01(squashDepth)) *
                (1.0f + Mathf.Clamp01(stretch) * Mathf.Max(0.0f, stretchDepth)),
                0.2f,
                2.0f);
        }

        public static Vector3 EvaluateGravityScale(
            Vector3 restScale,
            SoftDeformAxisMap axes,
            float horizontalGravity,
            float longitudinalGravity,
            float longitudinalOutwardSign,
            float supineSpread)
        {
            return EvaluateGravityScale(restScale, axes, horizontalGravity, longitudinalGravity,
                longitudinalOutwardSign, supineSpread, 0.0f);
        }

        public static Vector3 EvaluateGravityScale(
            Vector3 restScale,
            SoftDeformAxisMap axes,
            float horizontalGravity,
            float longitudinalGravity,
            float longitudinalOutwardSign,
            float supineSpread,
            float supineCompression)
        {
            var gravity = new Vector2(horizontalGravity, longitudinalGravity);
            if (gravity.sqrMagnitude > 1.0f) gravity.Normalize();

            float longitudinal = gravity.y;
            float outward = longitudinal * NonZeroSign(longitudinalOutwardSign);
            float supine = Mathf.Clamp01(-outward);

            float aspect = 1.0f + Mathf.Max(0.0f, supineSpread) * supine;
            aspect = Mathf.Clamp(aspect, 0.5f, 2.0f);
            float length = 1.0f - Mathf.Clamp(supineCompression, 0.0f, 0.3f) * supine;

            var scale = restScale;
            SetAxis(ref scale, axes.Horizontal, GetAxis(restScale, axes.Horizontal) * aspect);
            SetAxis(ref scale, axes.Longitudinal, GetAxis(restScale, axes.Longitudinal) * length);
            SetAxis(ref scale, axes.Vertical, GetAxis(restScale, axes.Vertical) / (aspect * length));
            return scale;
        }

        public static Vector3 EvaluatePostureScale(
            Vector3 restScale, SoftDeformAxisMap axes,
            float horizontalGravity, float longitudinalGravity, float upwardGravity,
            float longitudinalOutwardSign, float supineSpread, float supineCompression,
            float sideElongation, float invertedElongation)
        {
            var scale = EvaluateGravityScale(restScale, axes, horizontalGravity, longitudinalGravity,
                longitudinalOutwardSign, supineSpread, supineCompression);
            float side = 1 + Mathf.Clamp(sideElongation, 0, 0.3f) * Mathf.Clamp01(Mathf.Abs(horizontalGravity));
            float inverted = 1 + Mathf.Clamp(invertedElongation, 0, 0.3f) * Mathf.Clamp01(upwardGravity);
            // Only the visual transverse aspect changes; primary PhysBone motion and length remain owned by physics.
            scale[axes.Horizontal] *= side / inverted;
            scale[axes.Vertical] *= inverted / side;
            return scale;
        }

        public static int DominantAxis(Vector3 direction)
        {
            var a = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (a.x >= a.y && a.x >= a.z) return 0;
            return a.y >= a.z ? 1 : 2;
        }

        public static Vector3 AxisVector(int axis)
        {
            switch (axis)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        private static float GetAxis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private static void SetAxis(ref Vector3 value, int axis, float component)
        {
            if (axis == 0) value.x = component;
            else if (axis == 1) value.y = component;
            else value.z = component;
        }

        private static float NonZeroSign(float value)
        {
            return value < 0.0f ? -1.0f : 1.0f;
        }
    }
}
