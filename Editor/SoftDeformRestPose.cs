using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformRestPose
    {
        internal static void Apply(VRCPhysBone bone, float support)
        {
            support = Mathf.Clamp01(support);
            if (support <= 0 || bone.gravity == 0) return;

            // Blend the effective falloff along the whole chain, including authored curves.
            // Raising only the base value leaves sag where its curve is below one.
            float original = Mathf.Clamp01(bone.gravityFalloff);
            float supported = Mathf.Lerp(original, 1, support);
            AnimationCurve curve = bone.gravityFalloffCurve;
            if (curve == null || curve.length == 0)
            {
                bone.gravityFalloffCurve = AnimationCurve.Linear(0, 1, 1, 1);
            }
            else
            {
                var keys = curve.keys;
                float slope = (1 - support) * original / supported;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value = (support + (1 - support) * original * keys[i].value) / supported;
                    keys[i].inTangent = slope == 0 ? 0 : keys[i].inTangent * slope;
                    keys[i].outTangent = slope == 0 ? 0 : keys[i].outTangent * slope;
                }
                bone.gravityFalloffCurve = new AnimationCurve(keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode
                };
            }
            bone.gravityFalloff = supported;
        }
    }
}
