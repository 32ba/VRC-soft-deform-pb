using System;
using System.Linq;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformEasyTuningTests
    {
        private GameObject avatar;
        private SoftDeformPBSetup setup;
        private VRCPhysBone left, right;

        [SetUp]
        public void CreateAvatar()
        {
            avatar = new GameObject("Easy pad test");
            setup = avatar.AddComponent<SoftDeformPBSetup>();
            left = new GameObject("left").AddComponent<VRCPhysBone>();
            right = new GameObject("right").AddComponent<VRCPhysBone>();
            left.transform.SetParent(avatar.transform); right.transform.SetParent(avatar.transform);
            setup.leftBreast = left.transform; setup.rightBreast = right.transform;
            setup.preserveExistingMotion = true;
            setup.motionForceOverrides = SoftDeformMotionForceOverrides.Gravity | SoftDeformMotionForceOverrides.Stiffness;
            setup.motionPull = .123f; setup.motionSpring = .45f; setup.motionGravity = .21f;
            left.pull = .81f; right.pull = .49f; left.spring = .64f; right.spring = .16f;
            setup.reuseTorsoColliders = false; setup.torsoSupportClearance = .25f;
            setup.opposingColliderCoverage = .7f; setup.opposingColliderCenter = .4f;
            setup.parameterPrefix = "ExistingCustomName";
        }

        [TearDown]
        public void DestroyAvatar()
        {
            if (setup != null) Undo.ClearUndo(setup);
            if (avatar != null) Object.DestroyImmediate(avatar);
        }

        [Test]
        public void ReadingEveryPad_DoesNotRewriteTheAvatarOrAuthoredPhysBones()
        {
            var settings = new SerializedObject(setup);
            string before = EditorJsonUtility.ToJson(setup), leftBefore = EditorJsonUtility.ToJson(left), rightBefore = EditorJsonUtility.ToJson(right);
            foreach (SoftDeformEasyPad pad in Enum.GetValues(typeof(SoftDeformEasyPad)))
            {
                var point = SoftDeformEasyTuning.Read(pad, settings, left, right);
                Assert.That(point.x, Is.InRange(0, 1)); Assert.That(point.y, Is.InRange(0, 1));
                SoftDeformEasyTuning.IsApproximate(pad, settings, point, left, right);
                Assert.That(SoftDeformEasyTuning.Write(pad, settings, point, point), Is.False);
            }
            Assert.That(settings.hasModifiedProperties, Is.False);
            Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before));
            Assert.That(EditorJsonUtility.ToJson(left), Is.EqualTo(leftBefore));
            Assert.That(EditorJsonUtility.ToJson(right), Is.EqualTo(rightBefore));
        }

        [Test]
        public void MotionPad_ReadsAuthoredUnselectedForcesAndSelectedSavedForces()
        {
            var settings = new SerializedObject(setup);
            var initial = SoftDeformEasyTuning.Read(SoftDeformEasyPad.Motion, settings, left, right);
            setup.motionPull = .02f; setup.motionSpring = 1; settings.Update();
            Assert.That(SoftDeformEasyTuning.Read(SoftDeformEasyPad.Motion, settings, left, right), Is.EqualTo(initial));
            left.pull = .1f;
            Assert.That(SoftDeformEasyTuning.Read(SoftDeformEasyPad.Motion, settings, left, right).x, Is.GreaterThan(initial.x));
            setup.motionForceOverrides |= SoftDeformMotionForceOverrides.Spring; setup.motionSpring = .04f; settings.Update();
            Assert.That(SoftDeformEasyTuning.Read(SoftDeformEasyPad.Motion, settings, left, right).y, Is.EqualTo(.2f).Within(.00001f));
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        public void EditingOneMotionAxis_PreservesTheOtherForceAndExistingMode(bool preserve, int axis)
        {
            setup.preserveExistingMotion = preserve;
            string leftBefore = EditorJsonUtility.ToJson(left), rightBefore = EditorJsonUtility.ToJson(right);
            float unchanged = axis == 0 ? setup.motionSpring : setup.motionPull;
            var settings = new SerializedObject(setup);
            var previous = SoftDeformEasyTuning.Read(SoftDeformEasyPad.Motion, settings, left, right);
            var next = previous; next[axis] = .3f;
            Assert.That(SoftDeformEasyTuning.Write(SoftDeformEasyPad.Motion, settings, previous, next), Is.True);
            settings.ApplyModifiedProperties();
            var expectedMask = SoftDeformMotionForceOverrides.Gravity | SoftDeformMotionForceOverrides.Stiffness;
            if (preserve) expectedMask |= axis == 0 ? SoftDeformMotionForceOverrides.Pull : SoftDeformMotionForceOverrides.Spring;
            Assert.That(setup.motionForceOverrides, Is.EqualTo(expectedMask));
            Assert.That(axis == 0 ? setup.motionSpring : setup.motionPull, Is.EqualTo(unchanged));
            Assert.That(setup.preserveExistingMotion, Is.EqualTo(preserve));
            Assert.That(setup.motionGravity, Is.EqualTo(.21f));
            Assert.That(EditorJsonUtility.ToJson(left), Is.EqualTo(leftBefore));
            Assert.That(EditorJsonUtility.ToJson(right), Is.EqualTo(rightBefore));
        }

        [TestCase((int)SoftDeformEasyPad.Travel)]
        [TestCase((int)SoftDeformEasyPad.Shape)]
        [TestCase((int)SoftDeformEasyPad.Posture)]
        [TestCase((int)SoftDeformEasyPad.Angle)]
        [TestCase((int)SoftDeformEasyPad.Touch)]
        public void OtherPads_PreservePrimaryForcesBonesAndColliderConfiguration(int index)
        {
            var pad = (SoftDeformEasyPad)index;
            string leftBefore = EditorJsonUtility.ToJson(left), rightBefore = EditorJsonUtility.ToJson(right);
            var settings = new SerializedObject(setup);
            var previous = SoftDeformEasyTuning.Read(pad, settings, left, right);
            var target = new Vector2(.17f, .82f);
            Assert.That(SoftDeformEasyTuning.Write(pad, settings, previous, target), Is.True);
            settings.ApplyModifiedProperties();
            var actual = SoftDeformEasyTuning.Read(pad, settings, left, right);
            Assert.That(Vector2.Distance(actual, target), Is.LessThan(.00001f));
            Assert.That(setup.motionPull, Is.EqualTo(.123f)); Assert.That(setup.motionSpring, Is.EqualTo(.45f));
            Assert.That(setup.motionGravity, Is.EqualTo(.21f));
            Assert.That(setup.motionForceOverrides, Is.EqualTo(SoftDeformMotionForceOverrides.Gravity | SoftDeformMotionForceOverrides.Stiffness));
            Assert.That(setup.preserveExistingMotion, Is.True);
            Assert.That(setup.leftBreast, Is.EqualTo(left.transform)); Assert.That(setup.rightBreast, Is.EqualTo(right.transform));
            Assert.That(setup.reuseTorsoColliders, Is.False);
            Assert.That(setup.torsoSupportClearance, Is.EqualTo(.25f));
            Assert.That(setup.opposingColliderCoverage, Is.EqualTo(.7f)); Assert.That(setup.opposingColliderCenter, Is.EqualTo(.4f));
            Assert.That(setup.parameterPrefix, Is.EqualTo("ExistingCustomName"));
            Assert.That(EditorJsonUtility.ToJson(left), Is.EqualTo(leftBefore));
            Assert.That(EditorJsonUtility.ToJson(right), Is.EqualTo(rightBefore));
        }

        [Test]
        public void ThicknessOnly_DoesNotReplaceIndividuallyTunedLengthOrEnableMotion()
        {
            setup.maxSquish = .23f; setup.maxStretch = .44f; setup.stretchMotion = 0;
            setup.squashDepth = .27f; setup.stretchDepth = .34f;
            var settings = new SerializedObject(setup);
            var previous = SoftDeformEasyTuning.Read(SoftDeformEasyPad.Shape, settings);
            Assert.That(SoftDeformEasyTuning.IsApproximate(SoftDeformEasyPad.Shape, settings, previous), Is.True);
            SoftDeformEasyTuning.Write(SoftDeformEasyPad.Shape, settings, previous, new Vector2(previous.x, .35f));
            settings.ApplyModifiedProperties();
            Assert.That(setup.maxSquish, Is.EqualTo(.23f)); Assert.That(setup.maxStretch, Is.EqualTo(.44f));
            Assert.That(setup.stretchMotion, Is.Zero); Assert.That(setup.volumeRetention, Is.EqualTo(.35f));
            Assert.That(setup.squashDepth, Is.EqualTo(.27f)); Assert.That(setup.stretchDepth, Is.EqualTo(.34f));
        }

        [TestCase((int)SoftDeformEasyPad.Motion)]
        [TestCase((int)SoftDeformEasyPad.Travel)]
        [TestCase((int)SoftDeformEasyPad.Shape)]
        [TestCase((int)SoftDeformEasyPad.Posture)]
        [TestCase((int)SoftDeformEasyPad.Angle)]
        [TestCase((int)SoftDeformEasyPad.Touch)]
        public void AllCorners_KeepTheConfigurationWithinItsDeclaredRanges(int index)
        {
            var pad = (SoftDeformEasyPad)index;
            var settings = new SerializedObject(setup);
            foreach (var target in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
            {
                var previous = SoftDeformEasyTuning.Read(pad, settings, left, right);
                SoftDeformEasyTuning.Write(pad, settings, previous, target); settings.ApplyModifiedProperties();
                foreach (var field in typeof(SoftDeformPBSetup).GetFields())
                {
                    var range = (UnityEngine.RangeAttribute)Attribute.GetCustomAttribute(field, typeof(UnityEngine.RangeAttribute));
                    if (range != null)
                        Assert.That(Convert.ToSingle(field.GetValue(setup)), Is.InRange(range.min, range.max), field.Name);
                }
            }
        }

        [TestCase(float.NaN, 0)]
        [TestCase(0, float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity, 1)]
        public void NonFiniteInput_IsRejectedBeforeAnyPartialEdit(float x, float y)
        {
            var settings = new SerializedObject(setup);
            string before = EditorJsonUtility.ToJson(setup);
            Assert.Throws<ArgumentOutOfRangeException>(() => SoftDeformEasyTuning.Write(
                SoftDeformEasyPad.Shape, settings, Vector2.zero, new Vector2(x, y)));
            Assert.That(settings.hasModifiedProperties, Is.False);
            Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before));
        }

        [Test]
        public void PadEdits_RecordPrefabOverridesAndUndoRestoresConfigurationAndPosition()
        {
            string path = "Assets/SDPBEasyPadTest-" + Guid.NewGuid().ToString("N") + ".prefab";
            GameObject instance = null;
            SoftDeformPBSetup instanceSetup = null;
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(avatar, path);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instanceSetup = instance.GetComponent<SoftDeformPBSetup>();
                var settings = new SerializedObject(instanceSetup);
                string before = EditorJsonUtility.ToJson(instanceSetup);
                var previous = SoftDeformEasyTuning.Read(SoftDeformEasyPad.Travel, settings);
                Undo.IncrementCurrentGroup();
                SoftDeformEasyTuning.Write(SoftDeformEasyPad.Travel, settings, previous, new Vector2(.88f, previous.y));
                Assert.That(settings.ApplyModifiedProperties(), Is.True);
                Assert.That(PrefabUtility.GetPropertyModifications(instance).Any(p => p.propertyPath == "rootMotionMaxOffset"), Is.True);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); settings.Update();
                Assert.That(EditorJsonUtility.ToJson(instanceSetup), Is.EqualTo(before));
                Assert.That(Vector2.Distance(SoftDeformEasyTuning.Read(SoftDeformEasyPad.Travel, settings), previous), Is.LessThan(.00001f));
            }
            finally
            {
                if (instanceSetup != null) Undo.ClearUndo(instanceSetup);
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
