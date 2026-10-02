using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformDirectionalMotion
    {
        public static void Create(SoftDeformPBSetup setup, Transform motionRoot, Transform rotationRoot,
            Vector3 restPosition, Quaternion basis, VRCPositionConstraint position,
            VRCRotationConstraint rotation, string side)
        {
            var translation = new Vector3(setup.rootHorizontalTranslation, setup.rootVerticalTranslation,
                setup.rootLongitudinalTranslation);
            if (translation != Vector3.one)
            {
                var frame = Node("SoftDeformPB Translation Basis " + side, motionRoot.parent);
                frame.localRotation = basis;
                var sample = Node("Sample", frame);
                // The original world-space sampler now writes displacement in the anatomical frame.
                position.TargetTransform = sample;
                position.PositionOffset = -(Quaternion.Inverse(basis) * restPosition);
                Transform parent = frame;
                for (int axis = 0; axis < 3; axis++)
                {
                    var target = Node("Translation " + axis, parent);
                    var filter = target.gameObject.AddComponent<VRCPositionConstraint>();
                    filter.Sources.Add(new VRCConstraintSource(sample, 1, Vector3.zero, Vector3.zero));
                    filter.AffectsPositionX = axis == 0;
                    filter.AffectsPositionY = axis == 1;
                    filter.AffectsPositionZ = axis == 2;
                    filter.GlobalWeight = translation[axis];
                    filter.SolveInLocalSpace = true;
                    filter.IsActive = filter.Locked = true;
                    parent = target;
                }
                motionRoot.SetParent(parent, false);
                motionRoot.localRotation = Quaternion.Inverse(basis);
            }

            var rotations = new Vector3(setup.rootVerticalRotation, setup.rootHorizontalRotation,
                setup.rootTwistRotation);
            if (rotations == Vector3.one) return;
            var raw = Node("SoftDeformPB Rotation Sample " + side, rotationRoot.parent);
            rotation.TargetTransform = raw;
            var oriented = Node("Anatomical orientation", raw);
            oriented.localRotation = basis;
            var rotationFrame = Node("SoftDeformPB Rotation Basis " + side, rotationRoot.parent);
            rotationFrame.localRotation = basis;
            var localSample = Node("Sample", rotationFrame);
            var sampler = localSample.gameObject.AddComponent<VRCRotationConstraint>();
            sampler.Sources.Add(new VRCConstraintSource(oriented, 1, Vector3.zero, Vector3.zero));
            sampler.AffectsRotationX = sampler.AffectsRotationY = sampler.AffectsRotationZ = true;
            sampler.GlobalWeight = 1;
            sampler.IsActive = sampler.Locked = true;
            Transform rotationParent = rotationFrame;
            // Unity composes Euler angles as Y * X * Z. The three local filters follow that order.
            foreach (int axis in new[] { 1, 0, 2 })
            {
                var target = Node("Rotation " + axis, rotationParent);
                var filter = target.gameObject.AddComponent<VRCRotationConstraint>();
                filter.Sources.Add(new VRCConstraintSource(localSample, 1, Vector3.zero, Vector3.zero));
                filter.AffectsRotationX = axis == 0;
                filter.AffectsRotationY = axis == 1;
                filter.AffectsRotationZ = axis == 2;
                filter.GlobalWeight = rotations[axis];
                filter.SolveInLocalSpace = true;
                filter.IsActive = filter.Locked = true;
                rotationParent = target;
            }
            rotationRoot.SetParent(rotationParent, false);
            rotationRoot.localRotation = Quaternion.Inverse(basis);
        }

        private static Transform Node(string name, Transform parent)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.gameObject.layer = parent.gameObject.layer;
            return node;
        }
    }
}
