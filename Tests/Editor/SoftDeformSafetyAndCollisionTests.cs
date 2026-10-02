using System;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformSafetyAndCollisionTests
    {
        private static SoftDeformPBSetup Avatar(bool children = true)
        {
            var avatar = new GameObject("Safety collision test");
            avatar.AddComponent<Animator>();
            avatar.AddComponent<VRCAvatarDescriptor>();
            var setup = avatar.AddComponent<SoftDeformPBSetup>();
            setup.responseSamples = 3;
            Transform Side(string name, float x)
            {
                var root = new GameObject(name).transform;
                root.SetParent(avatar.transform, false);
                root.localPosition = new Vector3(x, 1, 0.1f);
                root.gameObject.AddComponent<VRCPhysBone>().radius = 0.01f;
                if (children)
                {
                    var tip = new GameObject("Tip").transform;
                    tip.SetParent(root, false);
                    tip.localPosition = Vector3.forward * 0.1f;
                }
                return root;
            }
            setup.leftBreast = Side("Left", -0.1f);
            setup.rightBreast = Side("Right", 0.1f);
            return setup;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EndpointOnlyRig_CompletesFinalBuildAndKeepsPhysicalEndpoint(int axis)
        {
            var setup = Avatar(false);
            var avatar = setup.gameObject;
            try
            {
                var endpoint = SoftDeformShapeMath.AxisVector(axis) * 0.1f;
                var physBones = avatar.GetComponentsInChildren<VRCPhysBone>();
                foreach (var pb in physBones) pb.endpointPosition = endpoint;
                using (new OverrideTemporaryDirectoryScope(null))
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                foreach (var pb in physBones) Assert.That(pb.endpointPosition, Is.EqualTo(endpoint));
                Assert.That(avatar.GetComponentsInChildren<VRCPhysBoneCollider>().Length, Is.GreaterThanOrEqualTo(3));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void AxisLookup_SkipsIgnoredAndZeroChildrenAndRejectsMissingDirection()
        {
            var setup = Avatar(false);
            try
            {
                var root = setup.leftBreast;
                var pb = root.GetComponent<VRCPhysBone>();
                var ignored = new GameObject("Ignored").transform;
                ignored.SetParent(root, false);
                ignored.localPosition = Vector3.right;
                pb.ignoreTransforms.Add(ignored);
                new GameObject("Zero helper").transform.SetParent(root, false);
                Assert.Throws<InvalidOperationException>(() => SoftDeformRigGeometry.InferLocalDirection(root, pb));
                pb.endpointPosition = Vector3.up;
                Assert.That(SoftDeformRigGeometry.InferLocalDirection(root, pb), Is.EqualTo(Vector3.up));
                var valid = new GameObject("Physical child").transform;
                valid.SetParent(root, false);
                valid.localPosition = Vector3.forward * 0.1f;
                Assert.That(SoftDeformRigGeometry.InferLocalDirection(root, pb), Is.EqualTo(valid.localPosition));
            }
            finally { Object.DestroyImmediate(setup.gameObject); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateParameters_AreRejectedBeforeMutatingBones(bool unrelatedWriter)
        {
            var setup = Avatar();
            try
            {
                var left = setup.leftBreast.GetComponent<VRCPhysBone>();
                var right = setup.rightBreast.GetComponent<VRCPhysBone>();
                left.parameter = "Duplicate";
                if (unrelatedWriter)
                {
                    var writer = new GameObject("Other writer");
                    writer.transform.SetParent(setup.transform, false);
                    writer.AddComponent<VRCPhysBone>().parameter = "Duplicate";
                }
                else right.parameter = "Duplicate";
                var parent = setup.leftBreast.parent;
                Assert.Throws<InvalidOperationException>(() => SoftDeformPBGeneratePass.ValidateParameterPrefixes(setup.transform, new[] { setup }));
                Assert.That(setup.leftBreast.parent, Is.SameAs(parent));
                Assert.That(left.parameter, Is.EqualTo("Duplicate"));
            }
            finally { Object.DestroyImmediate(setup.gameObject); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TorsoSupport_ReusesOnlyClearExistingGeometryAndPreservesAuthoredLists(bool overlapsAtRest)
        {
            var setup = Avatar();
            var avatar = setup.gameObject;
            try
            {
                var left = setup.leftBreast;
                var right = setup.rightBreast;
                var leftPb = left.GetComponent<VRCPhysBone>();
                var rightPb = right.GetComponent<VRCPhysBone>();
                var support = new GameObject("Authored torso").AddComponent<VRCPhysBoneCollider>();
                support.transform.SetParent(avatar.transform, false);
                support.transform.position = new Vector3(0, 1, overlapsAtRest ? 0.11f : 0.07f);
                support.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                support.shapeType = VRCPhysBoneColliderBase.ShapeType.Plane;
                support.globalCollision = VRCPhysBoneBase.AdvancedBool.False;
                var preserved = new GameObject("Authored distant collider").AddComponent<VRCPhysBoneCollider>();
                preserved.transform.SetParent(avatar.transform, false);
                preserved.transform.position = Vector3.down * 5;
                leftPb.colliders.Add(preserved);
                var origin = left.position;
                var radius = leftPb.radius;
                var permission = leftPb.allowCollision;
                using (new OverrideTemporaryDirectoryScope(null))
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                Assert.That(leftPb.colliders, Does.Contain(preserved));
                Assert.That(leftPb.colliders.Contains(support), Is.EqualTo(!overlapsAtRest));
                Assert.That(rightPb.colliders.Contains(support), Is.EqualTo(!overlapsAtRest));
                Assert.That(left.position, Is.EqualTo(origin));
                Assert.That(leftPb.radius, Is.EqualTo(radius));
                Assert.That(leftPb.allowCollision, Is.EqualTo(permission));
                var opposing = leftPb.colliders.OfType<VRCPhysBoneCollider>().Single(c => c.name == "SoftDeformPB Opposing Surface Right");
                Assert.That(opposing.globalCollision, Is.EqualTo(VRCPhysBoneBase.AdvancedBool.False));
                Assert.That(opposing.transform.IsChildOf(right), Is.False);
                Assert.That(leftPb.colliders.Distinct().Count(), Is.EqualTo(leftPb.colliders.Count));
                foreach (var sample in SoftDeformRigGeometry.Capture(left, leftPb).Samples)
                    Assert.That(Vector3.Distance(sample.Position, opposing.transform.position) - sample.Radius,
                        Is.GreaterThan(opposing.radius * SoftDeformRigGeometry.MaximumScale(opposing.transform)));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(0.0f)]
        [TestCase(0.15f)]
        [TestCase(0.4f)]
        public void SecondaryMotion_PreservesRestAndUsesOnlyIsolatedLocalRotation(float strength)
        {
            var setup = Avatar();
            var avatar = setup.gameObject;
            try
            {
                setup.secondaryMotionStrength = strength;
                setup.secondaryMotionPull = 0.09f;
                setup.secondaryMotionSpring = 0.49f;
                var left = setup.leftBreast;
                var originalPosition = left.position;
                var originalRotation = left.rotation;
                var originalScale = left.localScale;
                using (new OverrideTemporaryDirectoryScope(null))
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                var helpers = avatar.GetComponentsInChildren<VRCPhysBone>().Where(p => p.name.StartsWith("SoftDeformPB Secondary Driver ")).ToArray();
                Assert.That(helpers.Length, Is.EqualTo(strength == 0 ? 0 : 2));
                Assert.That(left.position, Is.EqualTo(originalPosition));
                Assert.That(left.rotation, Is.EqualTo(originalRotation));
                Assert.That(left.localScale, Is.EqualTo(originalScale));
                foreach (var helper in helpers)
                {
                    Assert.That(helper.transform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(helper.transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(helper.gravity, Is.Zero);
                    Assert.That(helper.CalcPull(1), Is.EqualTo(0.09f));
                    Assert.That(helper.CalcSpring(1), Is.EqualTo(0.49f));
                    Assert.That(helper.radius, Is.Zero);
                    Assert.That(helper.maxSquish + helper.maxStretch + helper.stretchMotion, Is.Zero);
                    Assert.That(helper.allowCollision, Is.EqualTo(VRCPhysBoneBase.AdvancedBool.False));
                    Assert.That(helper.allowGrabbing, Is.EqualTo(VRCPhysBoneBase.AdvancedBool.False));
                    Assert.That(helper.allowPosing, Is.EqualTo(VRCPhysBoneBase.AdvancedBool.False));
                    Assert.That(helper.GetComponent<VRCConstraintBase>(), Is.Null);
                    var primary = helper.transform.parent.GetComponent<VRCPhysBone>();
                    Assert.That(primary.ignoreTransforms, Does.Contain(helper.transform));
                    var visual = primary.transform.Cast<Transform>().Single(t => t.name.StartsWith("SoftDeformPB Visual "));
                    var rotation = visual.GetComponent<VRCRotationConstraint>();
                    Assert.That(rotation.Sources[0].SourceTransform, Is.SameAs(helper.transform));
                    Assert.That(rotation.SolveInLocalSpace, Is.True);
                    Assert.That(rotation.GlobalWeight, Is.EqualTo(strength));
                    Assert.That(rotation.RotationOffset, Is.EqualTo(Vector3.zero));
                    Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void SanitizedSetupPrefixes_MustRemainDistinct()
        {
            var setup = Avatar();
            try
            {
                setup.parameterPrefix = "Test-A";
                var host = new GameObject("Second setup");
                host.transform.SetParent(setup.transform, false);
                var second = host.AddComponent<SoftDeformPBSetup>();
                second.parameterPrefix = "Test A";
                second.leftBreast = setup.leftBreast;
                second.rightBreast = setup.rightBreast;
                var error = Assert.Throws<InvalidOperationException>(() => SoftDeformPBGeneratePass.ValidateParameterPrefixes(setup.transform, new[] { setup, second }));
                Assert.That(error.Message, Does.Contain("Duplicate generated parameter prefix"));
            }
            finally { Object.DestroyImmediate(setup.gameObject); }
        }

        [Test]
        public void FinalOwnershipCheck_RejectsExplicitPhysicalTargetButAllowsPhysicalSource()
        {
            var setup = Avatar();
            try
            {
                var child = new GameObject("Visual target");
                child.transform.SetParent(setup.transform, false);
                var constraint = child.AddComponent<VRCRotationConstraint>();
                constraint.Sources.Add(new VRCConstraintSource(setup.leftBreast, 1, Vector3.zero, Vector3.zero));
                Assert.DoesNotThrow(() => SoftDeformFinalValidationPass.ValidateConstraintOwnership(setup.leftBreast, new[] { constraint }));
                constraint.TargetTransform = setup.leftBreast;
                Assert.Throws<InvalidOperationException>(() => SoftDeformFinalValidationPass.ValidateConstraintOwnership(setup.leftBreast, new[] { constraint }));
            }
            finally { Object.DestroyImmediate(setup.gameObject); }
        }
    }
}
