using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformRigGeometry
    {
        internal readonly struct Sample
        {
            public readonly Vector3 Position;
            public readonly float Radius;
            public Sample(Vector3 position, float radius) { Position = position; Radius = radius; }
        }

        internal sealed class Chain
        {
            public Transform Root;
            public Transform Frame;
            public VRCPhysBone PhysBone;
            public Vector3 Origin;
            public Vector3 Direction;
            public float Reach;
            public float Length;
            public int SegmentCount;
            public Sample[] Samples;
        }

        public static Vector3 InferLocalDirection(Transform root, VRCPhysBone physBone)
        {
            foreach (Transform child in root)
                if (!Ignored(physBone, child) && Finite(child.localPosition) && child.localPosition.sqrMagnitude > 1e-10f)
                    return child.localPosition;
            if (Finite(physBone.endpointPosition) && physBone.endpointPosition.sqrMagnitude > 1e-10f)
                return physBone.endpointPosition;
            throw new InvalidOperationException($"[Soft Deform PB] '{root.name}' has no usable child direction or PhysBone Endpoint Position. Assign a non-zero endpoint or a physical child bone.");
        }

        public static bool Ignored(VRCPhysBone physBone, Transform bone)
        {
            return physBone.ignoreTransforms != null && physBone.ignoreTransforms.Any(t => t != null && bone.IsChildOf(t));
        }

        public static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        public static float MaximumScale(Transform transform)
        {
            var scale = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        public static Chain Capture(Transform root, VRCPhysBone physBone)
        {
            var segments = new List<(Vector3 from, Vector3 to, float distance, float length)>();
            void Visit(Transform bone, float distance)
            {
                var children = bone.Cast<Transform>().Where(t => !Ignored(physBone, t)).ToArray();
                foreach (var child in children)
                {
                    float length = Vector3.Distance(bone.position, child.position);
                    if (length > 1e-6f) segments.Add((bone.position, child.position, distance, length));
                    Visit(child, distance + length);
                }
                if (children.Length == 0 && physBone.endpointPosition.sqrMagnitude > 1e-10f)
                {
                    var end = bone.TransformPoint(physBone.endpointPosition);
                    segments.Add((bone.position, end, distance, Vector3.Distance(bone.position, end)));
                }
            }
            Visit(root, 0);
            Vector3 direction = root.TransformVector(InferLocalDirection(root, physBone)).normalized;
            float maximumDistance = segments.Count == 0 ? 0 : segments.Max(s => s.distance + s.length);
            if (maximumDistance <= 1e-6f)
                throw new InvalidOperationException($"[Soft Deform PB] '{root.name}' has no physical chain length.");
            float scale = MaximumScale(root);
            var samples = new List<Sample>();
            foreach (var segment in segments)
                for (int step = 0; step <= 16; step++)
                {
                    float t = step / 16.0f;
                    float normalizedLength = (segment.distance + t * segment.length) / maximumDistance;
                    samples.Add(new Sample(Vector3.Lerp(segment.from, segment.to, t),
                        Mathf.Max(0, physBone.CalcRadius(normalizedLength)) * scale));
                }
            return new Chain
            {
                Root = root, Frame = root.parent, PhysBone = physBone, Origin = root.position,
                Direction = direction, Samples = samples.ToArray(),
                Length = maximumDistance, SegmentCount = segments.Count,
                Reach = Mathf.Max(1e-6f, samples.Max(s => Vector3.Dot(s.Position - root.position, direction)))
            };
        }
    }
}
