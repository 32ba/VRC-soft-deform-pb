using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformCompressionRig
    {
        internal Transform AnimationRoot;
        internal string ShapePath;
        internal string InwardParameter, TouchParameter;
        internal float StartThreshold, FullThreshold;
        internal Vector3 CorrectionEuler;

        internal static bool Enabled(SoftDeformPBSetup setup) =>
            setup.lateralCompressionDepth > 0 || setup.gatheringAngleCorrection > 0;

        internal static SoftDeformCompressionRig Create(BuildContext context, SoftDeformPBSetup setup,
            Transform physical, Transform visual, VRCPhysBone pb, Vector3 direction,
            SoftDeformAxisMap axes, Vector3 outwardInParent, string prefix, string side)
        {
            if (!Enabled(setup)) return null;
            float length = direction.magnitude;
            Vector3 outward = SoftDeformShapeMath.AxisVector(axes.Horizontal);
            if (Vector3.Dot(physical.localRotation * outward, outwardInParent) < 0) outward = -outward;
            var reference = Node("SoftDeformPB Compression Reference " + side, physical.parent);
            reference.localPosition = physical.localPosition;
            reference.localRotation = physical.localRotation;
            reference.localScale = physical.localScale;
            var probe = Node("SoftDeformPB Direction Probe " + side, physical);
            probe.localPosition = direction;
            var gather = Node("SoftDeformPB Gather " + side, visual.parent);
            visual.SetParent(gather, false);

            // Keep the signal outside the visual deformation, avoiding a shape/contact feedback loop.
            // Mirror authored pose curves after MA, but never mirror simulated PhysBone transforms.
            context.GetState<SoftDeformParallelMotion.BuildState>().Entries.Add(
                new SoftDeformParallelMotion.Entry
                {
                    Physical = physical, Reference = reference, Branch = probe, CopyFullPose = true,
                    ExtraIgnored = new[] { gather }
                });
            Exclude(context.AvatarRootObject, probe, reference, gather);

            string tag = "SoftDeformPB.Direction." + prefix + "." + side;
            var sender = probe.gameObject.AddComponent<VRCContactSender>();
            sender.shapeType = ContactBase.ShapeType.Sphere;
            sender.radius = length * 0.01f;
            sender.collisionTags = new List<string> { tag };
            sender.localOnly = false;

            var inward = Node("SoftDeformPB Inward " + side, reference).gameObject.AddComponent<VRCContactReceiver>();
            inward.shapeType = ContactBase.ShapeType.Box;
            inward.position = direction;
            inward.rotation = Quaternion.LookRotation(-outward, direction.normalized);
            inward.size = new Vector3(4 * length, 4 * length, 2 * length);
            inward.useFaceProximity = true;
            inward.receiverType = ContactReceiver.ReceiverType.Proximity;
            inward.parameter = prefix + "_Inward";
            inward.allowSelf = true; inward.allowOthers = false; inward.localOnly = false;
            inward.collisionTags = new List<string> { tag };

            var touch = Node("SoftDeformPB Outer Touch " + side, reference).gameObject.AddComponent<VRCContactReceiver>();
            touch.shapeType = ContactBase.ShapeType.Sphere;
            float radius = Mathf.Max(pb.radius, length * 0.3f);
            touch.position = direction * 0.65f + outward * radius * 0.6f;
            touch.radius = radius;
            touch.receiverType = ContactReceiver.ReceiverType.Proximity;
            touch.parameter = prefix + "_OuterTouch";
            touch.localOnly = false;
            touch.allowSelf = pb.allowCollision == VRCPhysBoneBase.AdvancedBool.True ||
                              (pb.allowCollision == VRCPhysBoneBase.AdvancedBool.Other && pb.collisionFilter.allowSelf);
            touch.allowOthers = pb.allowCollision == VRCPhysBoneBase.AdvancedBool.True ||
                                (pb.allowCollision == VRCPhysBoneBase.AdvancedBool.Other && pb.collisionFilter.allowOthers);
            touch.collisionTags = new List<string> { "Hand", "Finger" };

            float neutral = 0.5f + 0.01f / 2;
            // Positive correction opposes the rotation that points the chain inward.
            float sign = Mathf.Sign(Vector3.Dot(Vector3.Cross(direction.normalized, -outward),
                SoftDeformShapeMath.AxisVector(axes.Vertical)));
            float angle = Mathf.Min(setup.gatheringAngleCorrection,
                Mathf.Asin(setup.lateralResponseDistance) * Mathf.Rad2Deg);
            return new SoftDeformCompressionRig
            {
                AnimationRoot = gather, ShapePath = visual.name,
                InwardParameter = inward.parameter, TouchParameter = touch.parameter,
                StartThreshold = neutral + 0.1f / 2, FullThreshold = neutral + setup.lateralResponseDistance / 2,
                CorrectionEuler = SoftDeformShapeMath.AxisVector(axes.Vertical) * (-sign * angle)
            };
        }

        private static Transform Node(string name, Transform parent)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false); node.gameObject.layer = parent.gameObject.layer;
            return node;
        }

        internal static void Exclude(GameObject avatar, params Transform[] nodes)
        {
            foreach (var pb in avatar.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                var ignored = pb.ignoreTransforms != null ? new List<Transform>(pb.ignoreTransforms) : new List<Transform>();
                foreach (var node in nodes)
                    if (node.IsChildOf(root) && !ignored.Contains(node)) ignored.Add(node);
                pb.ignoreTransforms = ignored;
            }
        }
    }
}
