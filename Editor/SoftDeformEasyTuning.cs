using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal enum SoftDeformEasyPad { Motion, Travel, Shape, Posture, Angle, Touch }

    // The pads edit the existing configuration. There is no second saved configuration or build path.
    internal static class SoftDeformEasyTuning
    {
        private const float SquishLimit = .6f;
        private const float StretchLimit = SquishLimit * 3 / 7;
        internal static Vector2 Read(SoftDeformEasyPad pad, SerializedObject settings,
            VRCPhysBone left = null, VRCPhysBone right = null)
        {
            float F(string name) => settings.FindProperty(name).floatValue;
            switch (pad)
            {
                case SoftDeformEasyPad.Motion:
                    float pull = Force(settings, "motionPull", SoftDeformMotionForceOverrides.Pull, left, right);
                    float spring = Force(settings, "motionSpring", SoftDeformMotionForceOverrides.Spring, left, right);
                    return new Vector2(1 - Root((pull - .02f) / .98f), Root(spring));
                case SoftDeformEasyPad.Travel:
                    return new Vector2(Root(F("rootMotionMaxOffset") / .05f), Root(F("secondaryMotionStrength") / .4f));
                case SoftDeformEasyPad.Shape:
                    return new Vector2(Root(F("maxSquish") / SquishLimit), Unit(F("volumeRetention")));
                case SoftDeformEasyPad.Posture:
                    return new Vector2(Root(F("gravitySupineSpread") / .35f),
                        Root(Mathf.Max(F("gravitySideElongation"), F("gravityInvertedElongation")) / .3f));
                case SoftDeformEasyPad.Angle:
                    return new Vector2(Unit(F("horizontalAngleRetention")), Unit(F("verticalAngleRetention")));
                default:
                    return new Vector2(Root(F("lateralCompressionDepth") / .5f), Unit(F("minimumCompressionRatio")));
            }
        }

        internal static bool Write(SoftDeformEasyPad pad, SerializedObject settings, Vector2 previous, Vector2 next)
        {
            if (!Finite(next.x) || !Finite(next.y)) throw new ArgumentOutOfRangeException(nameof(next));
            next = new Vector2(Mathf.Clamp01(next.x), Mathf.Clamp01(next.y));
            bool x = !Mathf.Approximately(previous.x, next.x), y = !Mathf.Approximately(previous.y, next.y);
            if (!x && !y) return false;
            void F(string name, float value) => settings.FindProperty(name).floatValue = value;
            float xx = next.x * next.x, yy = next.y * next.y;
            switch (pad)
            {
                case SoftDeformEasyPad.Motion:
                    if (x) { F("motionPull", .02f + .98f * (1 - next.x) * (1 - next.x)); SelectForce(settings, SoftDeformMotionForceOverrides.Pull); }
                    if (y) { F("motionSpring", yy); SelectForce(settings, SoftDeformMotionForceOverrides.Spring); }
                    break;
                case SoftDeformEasyPad.Travel:
                    if (x) F("rootMotionMaxOffset", .05f * xx);
                    if (y) F("secondaryMotionStrength", .4f * yy);
                    break;
                case SoftDeformEasyPad.Shape:
                    if (x)
                    {
                        float squish = SquishLimit * xx, stretch = StretchLimit * xx;
                        F("maxSquish", squish); F("maxStretch", stretch);
                        F("squashDepth", Mathf.Max(.05f, squish)); F("stretchDepth", stretch);
                        F("stretchMotion", .65f);
                    }
                    if (y) F("volumeRetention", next.y);
                    break;
                case SoftDeformEasyPad.Posture:
                    if (x)
                    {
                        F("gravitySupineSpread", .35f * xx);
                        F("gravitySupineCompression", .175f * xx);
                        F("gravitySupineRootSpread", .01575f * xx);
                    }
                    if (y) { F("gravitySideElongation", .3f * yy); F("gravityInvertedElongation", .3f * yy); }
                    break;
                case SoftDeformEasyPad.Angle:
                    if (x) F("horizontalAngleRetention", next.x);
                    if (y) F("verticalAngleRetention", next.y);
                    break;
                case SoftDeformEasyPad.Touch:
                    if (x) F("lateralCompressionDepth", .5f * xx);
                    if (y) F("minimumCompressionRatio", next.y);
                    break;
            }
            return true;
        }

        internal static bool IsApproximate(SoftDeformEasyPad pad, SerializedObject settings, Vector2 point,
            VRCPhysBone left = null, VRCPhysBone right = null)
        {
            bool Different(float actual, float expected) => !Finite(actual) || Mathf.Abs(actual - expected) > .00001f;
            float F(string name) => settings.FindProperty(name).floatValue;
            float xx = point.x * point.x, yy = point.y * point.y;
            switch (pad)
            {
                case SoftDeformEasyPad.Motion:
                    return Different(Force(settings, "motionPull", SoftDeformMotionForceOverrides.Pull, left, right), .02f + .98f * (1 - point.x) * (1 - point.x)) ||
                        Different(Force(settings, "motionSpring", SoftDeformMotionForceOverrides.Spring, left, right), yy) ||
                        Mixed(settings, left, right, SoftDeformMotionForceOverrides.Pull) || Mixed(settings, left, right, SoftDeformMotionForceOverrides.Spring);
                case SoftDeformEasyPad.Shape:
                    return Different(F("maxSquish"), SquishLimit * xx) || Different(F("maxStretch"), StretchLimit * xx) ||
                        Different(F("squashDepth"), Mathf.Max(.05f, SquishLimit * xx)) || Different(F("stretchDepth"), StretchLimit * xx) ||
                        Different(F("stretchMotion"), .65f);
                case SoftDeformEasyPad.Posture:
                    return Different(F("gravitySupineCompression"), .175f * xx) || Different(F("gravitySupineRootSpread"), .01575f * xx) ||
                        Different(F("gravitySideElongation"), .3f * yy) || Different(F("gravityInvertedElongation"), .3f * yy);
                default: return false;
            }
        }

        private static float Force(SerializedObject settings, string name, SoftDeformMotionForceOverrides flag,
            VRCPhysBone left, VRCPhysBone right)
        {
            if (!settings.FindProperty("preserveExistingMotion").boolValue ||
                (settings.FindProperty("motionForceOverrides").intValue & (int)flag) != 0 || (left == null && right == null))
                return settings.FindProperty(name).floatValue;
            float Value(VRCPhysBone bone) => flag == SoftDeformMotionForceOverrides.Pull ? bone.pull : bone.spring;
            if (left == null) return Value(right);
            if (right == null) return Value(left);
            return (Value(left) + Value(right)) * .5f;
        }

        private static bool Mixed(SerializedObject settings, VRCPhysBone left, VRCPhysBone right, SoftDeformMotionForceOverrides flag)
        {
            if (left == null || right == null || !settings.FindProperty("preserveExistingMotion").boolValue ||
                (settings.FindProperty("motionForceOverrides").intValue & (int)flag) != 0) return false;
            return flag == SoftDeformMotionForceOverrides.Pull ? left.pull != right.pull : left.spring != right.spring;
        }

        private static void SelectForce(SerializedObject settings, SoftDeformMotionForceOverrides flag)
        {
            if (settings.FindProperty("preserveExistingMotion").boolValue)
                settings.FindProperty("motionForceOverrides").intValue |= (int)flag;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Unit(float value) => Finite(value) ? Mathf.Clamp01(value) : 0;
        private static float Root(float value) => Mathf.Sqrt(Unit(value));
    }
}
