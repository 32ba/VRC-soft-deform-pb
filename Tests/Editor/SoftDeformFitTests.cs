using System;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformFitTests
    {
        private static SoftDeformPBSetup Avatar(int axis = 2, float sign = 1)
        {
            var avatar = new GameObject("Fit test"); avatar.AddComponent<Animator>(); avatar.AddComponent<VRCAvatarDescriptor>();
            var setup = avatar.AddComponent<SoftDeformPBSetup>(); setup.responseSamples = 3;
            var chest = new GameObject("Chest").transform; chest.SetParent(avatar.transform, false); chest.localPosition = Vector3.up;
            Transform Bone(string name, float x)
            {
                var t = new GameObject(name).transform; t.SetParent(chest, false); t.localPosition = new Vector3(x, 0, .1f);
                var pb = t.gameObject.AddComponent<VRCPhysBone>(); pb.endpointPosition = SoftDeformShapeMath.AxisVector(axis) * (.1f * sign); pb.radius = .01f;
                return t;
            }
            setup.leftBreast = Bone("Left", -.2f); setup.rightBreast = Bone("Right", .2f);
            return setup;
        }

        [TestCase(0, 1f)] [TestCase(0, -1f)]
        [TestCase(1, 1f)] [TestCase(1, -1f)]
        [TestCase(2, 1f)] [TestCase(2, -1f)]
        public void PreviewSupports_MatchFullBuildOnEachSignedAxis(int axis, float sign)
        {
            var setup = Avatar(axis, sign); var avatar = setup.gameObject;
            try
            {
                avatar.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(10, 20, 30));
                avatar.transform.localScale = Vector3.one * 1.7f;
                setup.reuseTorsoColliders = false; setup.torsoSupportClearance = .12f;
                setup.opposingColliderCenter = .7f; setup.opposingColliderCoverage = .4f;
                var report = SoftDeformFitDiagnostics.Analyze(setup);
                Assert.That(report.Valid, Is.True, string.Join("\n", report.Notices.Select(n => n.Text)));
                using (new OverrideTemporaryDirectoryScope(null)) Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                for (int i = 0; i < 2; i++)
                {
                    string side = i == 0 ? "Left" : "Right";
                    var torso = avatar.GetComponentsInChildren<VRCPhysBoneCollider>().Single(c => c.name == "SoftDeformPB Torso Support " + side);
                    var opposing = avatar.GetComponentsInChildren<VRCPhysBoneCollider>().Single(c => c.name == "SoftDeformPB Opposing Surface " + side);
                    Assert.That(Vector3.Distance(torso.transform.position, report.Torso[i].Center), Is.LessThan(.00001f));
                    Assert.That(Vector3.Angle(torso.transform.up, report.Torso[i].Direction), Is.LessThan(.1f));
                    Assert.That(Vector3.Distance(opposing.transform.position, report.Opposing[i].Center), Is.LessThan(.00001f));
                    Assert.That(opposing.radius * SoftDeformRigGeometry.MaximumScale(opposing.transform), Is.EqualTo(report.Opposing[i].Radius).Within(.00001f));
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void DefaultFit_PreservesSharedGeneratedPlaneAndExistingColliders()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            try
            {
                var left = setup.leftBreast.GetComponent<VRCPhysBone>(); var right = setup.rightBreast.GetComponent<VRCPhysBone>();
                var report = SoftDeformFitDiagnostics.Analyze(setup);
                Assert.That(report.Torso[1].SharedWithLeft, Is.True);
                using (new OverrideTemporaryDirectoryScope(null)) Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                var planes = avatar.GetComponentsInChildren<VRCPhysBoneCollider>().Where(c => c.name.StartsWith("SoftDeformPB Torso Support")).ToArray();
                Assert.That(planes.Length, Is.EqualTo(1));
                Assert.That(left.colliders, Does.Contain(planes[0])); Assert.That(right.colliders, Does.Contain(planes[0]));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void InactiveLeftFrame_DoesNotSupplyTheRightTorsoSupport()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            try
            {
                var frame = new GameObject("Inactive left frame").transform;
                frame.SetParent(setup.leftBreast.parent, false); setup.leftBreast.SetParent(frame, false); frame.gameObject.SetActive(false);
                var right = setup.rightBreast.GetComponent<VRCPhysBone>();
                var report = SoftDeformFitDiagnostics.Analyze(setup);
                Assert.That(report.Valid, Is.True);
                Assert.That(report.Torso[1].SharedWithLeft, Is.False);
                using (new OverrideTemporaryDirectoryScope(null)) Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                var support = right.colliders.OfType<VRCPhysBoneCollider>().Single(c => c.name.StartsWith("SoftDeformPB Torso Support"));
                Assert.That(support.gameObject.activeInHierarchy, Is.True);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void Analysis_IsReadOnlyAndReportsMissingRootsBranchesAndRestOverlap()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            try
            {
                var pb = setup.leftBreast.GetComponent<VRCPhysBone>();
                foreach (var direction in new[] { new Vector3(.04f, .04f, .04f), Vector3.forward * .1f })
                { var branch = new GameObject("Branch").transform; branch.SetParent(pb.transform, false); branch.localPosition = direction; }
                var collider = new GameObject("Overlapping source").AddComponent<VRCPhysBoneCollider>();
                collider.transform.SetParent(avatar.transform, false); collider.transform.position = pb.transform.position; collider.radius = .05f; pb.colliders.Add(collider);
                string before = EditorJsonUtility.ToJson(setup); string pbBefore = EditorJsonUtility.ToJson(pb);
                int children = avatar.GetComponentsInChildren<Transform>(true).Length;
                var report = SoftDeformFitDiagnostics.Analyze(setup);
                Assert.That(report.Notices.Any(n => n.Text.Contains("direct physical branches")), Is.True);
                Assert.That(report.Notices.Any(n => n.Text.Contains("20 degrees")), Is.True);
                Assert.That(report.Notices.Any(n => n.Context == collider && n.Severity == MessageType.Warning), Is.True);
                Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before)); Assert.That(EditorJsonUtility.ToJson(pb), Is.EqualTo(pbBefore));
                Assert.That(avatar.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
                setup.leftBreast = null;
                Assert.That(SoftDeformFitDiagnostics.Analyze(setup).Notices.Any(n => n.Severity == MessageType.Error && n.Text.Contains("assigned")), Is.True);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false)] [TestCase(true)]
        public void ClothingDiagnostics_UseMAMappingIncludeInactiveBranchesAndMeasureCurrentShape(bool wrongPrefix)
        {
            var setup = Avatar(); var avatar = setup.gameObject; var mesh = new Mesh();
            try
            {
                var outfit = new GameObject("Outfit"); outfit.transform.SetParent(avatar.transform, false);
                var armature = new GameObject("Armature").transform; armature.SetParent(outfit.transform, false);
                Transform Copy(Transform source)
                {
                    var bone = new GameObject("Out_" + source.name + "_Cloth").transform; bone.SetParent(armature, false); bone.position = source.position; return bone;
                }
                var left = Copy(setup.leftBreast); var right = Copy(setup.rightBreast);
                var detail = new GameObject("Unmatched detail").transform; detail.SetParent(left, false); detail.localPosition = Vector3.forward * .06f;
                var merge = armature.gameObject.AddComponent<ModularAvatarMergeArmature>(); merge.LockMode = ArmatureLockMode.NotLocked;
                merge.mergeTarget.Set(setup.leftBreast.parent.gameObject); merge.prefix = wrongPrefix ? "Wrong_" : "Out_"; merge.suffix = "_Cloth";
                var renderer = outfit.AddComponent<SkinnedMeshRenderer>(); renderer.bones = new[] { left, right, detail }; renderer.rootBone = armature;
                mesh.vertices = new[] { left.position, left.position + Vector3.forward * .02f, right.position, detail.position };
                mesh.triangles = new[] { 0, 1, 2, 1, 2, 3 }; mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
                mesh.boneWeights = new[] { 0, 0, 1, 2 }.Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1 }).ToArray();
                mesh.bindposes = renderer.bones.Select(t => t.worldToLocalMatrix).ToArray();
                mesh.AddBlendShapeFrame("Size", 100, new[] { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.forward * .08f }, new Vector3[4], new Vector3[4]);
                mesh.RecalculateBounds(); renderer.sharedMesh = mesh; outfit.SetActive(false);
                var originalWeights = mesh.boneWeights; var originalBindposes = mesh.bindposes;
                var first = SoftDeformFitDiagnostics.Analyze(setup); var skin = first.Skins.Single();
                Assert.That(skin.LeftVertices, Is.EqualTo(wrongPrefix ? 0 : 3)); Assert.That(skin.RightVertices, Is.EqualTo(wrongPrefix ? 0 : 1));
                Assert.That(skin.MappedBones, Is.EqualTo(wrongPrefix ? 0 : 3)); Assert.That(outfit.activeSelf, Is.False);
                if (!wrongPrefix)
                {
                    renderer.SetBlendShapeWeight(0, 100);
                    var second = SoftDeformFitDiagnostics.Analyze(setup);
                    Assert.That(second.Skins.Single().Bounds.size.z, Is.GreaterThan(skin.Bounds.size.z + .07f));
                    Assert.That(second.SuggestedTravel, Is.EqualTo(first.SuggestedTravel));
                    Assert.That(second.Opposing[0].Radius, Is.EqualTo(first.Opposing[0].Radius));
                }
                Assert.That(mesh.boneWeights, Is.EqualTo(originalWeights)); Assert.That(mesh.bindposes, Is.EqualTo(originalBindposes));
            }
            finally { Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void DistanceSuggestion_UsesParentLocalReachAndLeavesValuesUntilApplied()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            try
            {
                var first = SoftDeformFitDiagnostics.Analyze(setup);
                avatar.transform.localScale = Vector3.one * 2;
                var scaled = SoftDeformFitDiagnostics.Analyze(setup);
                Assert.That(scaled.SuggestedTravel, Is.EqualTo(first.SuggestedTravel).Within(.000001f));
                setup.leftBreast.localScale = setup.rightBreast.localScale = Vector3.one * 2;
                var longer = SoftDeformFitDiagnostics.Analyze(setup);
                Assert.That(longer.SuggestedTravel, Is.EqualTo(first.SuggestedTravel * 2).Within(.000001f));
                Assert.That(setup.rootMotionMaxOffset, Is.EqualTo(.01f));
                string prefix = setup.parameterPrefix; var bone = setup.leftBreast;
                Undo.IncrementCurrentGroup(); SoftDeformFitEditing.ApplyDistances(setup, longer); Undo.FlushUndoRecordObjects();
                Assert.That(setup.rootMotionMaxOffset, Is.EqualTo(longer.SuggestedTravel));
                Assert.That(setup.parameterPrefix, Is.EqualTo(prefix)); Assert.That(setup.leftBreast, Is.SameAs(bone));
                Undo.PerformUndo(); Assert.That(setup.rootMotionMaxOffset, Is.EqualTo(.01f));
                Undo.PerformRedo(); Assert.That(setup.rootMotionMaxOffset, Is.EqualTo(longer.SuggestedTravel));
            }
            finally { Undo.ClearUndo(setup); Undo.IncrementCurrentGroup(); Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void SupportHandle_PreservesPrefabReferencesAndSupportsUndoRedo()
        {
            var setup = Avatar(); GameObject instance = null;
            string path = "Assets/__SoftDeformFitTest_" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(setup.gameObject, path);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var fitted = instance.GetComponent<SoftDeformPBSetup>(); var bone = fitted.leftBreast;
                Undo.IncrementCurrentGroup();
                SoftDeformFitEditing.SetHandle(fitted, SoftDeformFitEditing.Handle.TorsoClearance, .2f); Undo.FlushUndoRecordObjects();
                Assert.That(PrefabUtility.GetPropertyModifications(fitted).Any(p => p.propertyPath == "torsoSupportClearance"), Is.True);
                Assert.That(fitted.leftBreast, Is.SameAs(bone));
                Undo.PerformUndo(); Assert.That(fitted.torsoSupportClearance, Is.Zero);
                Undo.PerformRedo(); Assert.That(fitted.torsoSupportClearance, Is.EqualTo(.2f));
                Assert.That(prefab.GetComponent<SoftDeformPBSetup>().torsoSupportClearance, Is.Zero);
                Undo.ClearUndo(fitted);
            }
            finally
            {
                Undo.IncrementCurrentGroup(); if (instance != null) Object.DestroyImmediate(instance);
                Object.DestroyImmediate(setup.gameObject); AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
