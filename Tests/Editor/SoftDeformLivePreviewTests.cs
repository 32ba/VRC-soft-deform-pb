using System;
using System.Linq;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformLivePreviewTests
    {
        private GameObject avatar, template;
        private SoftDeformPBSetup setup;
        private VRCPhysBone left, right;

        [SetUp]
        public void CreateAvatar()
        {
            avatar = new GameObject("Live preview test");
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var chest = new GameObject("Chest").transform;
            chest.SetParent(avatar.transform, false);
            left = Breast(chest, "Left", -.1f);
            right = Breast(chest, "Right", .1f);
            setup = avatar.AddComponent<SoftDeformPBSetup>();
            setup.leftBreast = left.transform;
            setup.rightBreast = right.transform;
            setup.responseSamples = 3;
            setup.preserveExistingMotion = true;
            left.pull = .7f; right.pull = .9f;
        }

        private static VRCPhysBone Breast(Transform parent, string name, float x)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = new Vector3(x, .1f, .1f);
            var end = new GameObject(name + " End").transform;
            end.SetParent(bone, false);
            end.localPosition = Vector3.forward * .12f;
            return bone.gameObject.AddComponent<VRCPhysBone>();
        }

        [TearDown]
        public void Cleanup()
        {
            if (template != null) Object.DestroyImmediate(template);
            if (setup != null) Undo.ClearUndo(setup);
            if (avatar != null) Object.DestroyImmediate(avatar);
        }

        [Test]
        public void Settings_SurviveSerializationWithoutReplacingBoneReferences()
        {
            var baseline = SoftDeformPreviewSettings.Capture(setup);
            setup.motionPull = .23f; setup.responseSamples = 7; setup.parameterPrefix = "Custom";
            setup.motionForceOverrides = SoftDeformMotionForceOverrides.Spring | SoftDeformMotionForceOverrides.Pull;
            setup.preserveExistingMotion = false;
            var tuned = SoftDeformPreviewSettings.Capture(setup);
            var restored = JsonUtility.FromJson<SoftDeformPreviewSettings>(JsonUtility.ToJson(tuned));
            baseline.Apply(setup);
            restored.Apply(setup);
            Assert.That(SoftDeformPreviewSettings.Capture(setup).SameAs(tuned), Is.True);
            Assert.That(setup.leftBreast, Is.SameAs(left.transform));
            Assert.That(setup.rightBreast, Is.SameAs(right.transform));
            Assert.That(left.pull, Is.EqualTo(.7f)); Assert.That(right.pull, Is.EqualTo(.9f));
        }

        [Test]
        public void Adopting_ChangesOnlyTunedFieldsAndSupportsUndo()
        {
            var baseline = SoftDeformPreviewSettings.Capture(setup);
            setup.motionPull = .17f;
            var tuned = SoftDeformPreviewSettings.Capture(setup);
            baseline.Apply(setup);
            setup.volumeRetention = .67f; // An independent authored edit must survive adoption.
            Undo.IncrementCurrentGroup();
            tuned.Apply(setup, baseline, true);
            Undo.FlushUndoRecordObjects();
            Assert.That(setup.motionPull, Is.EqualTo(.17f));
            Assert.That(setup.volumeRetention, Is.EqualTo(.67f));
            Undo.PerformUndo();
            Assert.That(setup.motionPull, Is.EqualTo(.1024f));
            Assert.That(setup.volumeRetention, Is.EqualTo(.67f));
            Assert.That(setup.leftBreast, Is.SameAs(left.transform));
        }

        [Test]
        public void Template_IsInactiveAndAllowsSerializedTuningAtTheSameWorldPose()
        {
            avatar.transform.position = new Vector3(2, 3, 4);
            string before = EditorJsonUtility.ToJson(setup);
            template = SoftDeformLivePreview.CreateTemplate(avatar);
            var copy = template.GetComponent<SoftDeformPBSetup>();
            Assert.That(template.activeSelf, Is.False);
            Assert.That(avatar.activeSelf, Is.True);
            Assert.That(copy.leftBreast, Is.Not.SameAs(left.transform));
            Assert.That(copy.leftBreast.IsChildOf(template.transform), Is.True);
            Assert.That(Vector3.Distance(copy.leftBreast.position, left.transform.position), Is.LessThan(.00001f));
            Assert.That(template.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
            Assert.That(template.hideFlags & HideFlags.HideInHierarchy, Is.EqualTo(HideFlags.HideInHierarchy));
            using (var settings = new SerializedObject(copy))
            {
                var support = settings.FindProperty("restPoseSupport");
                Assert.That(support.editable, Is.True, "Live sliders must remain editable on the hidden template.");
                Assert.That(settings.FindProperty("motionPull").editable, Is.True);
                support.floatValue = .78f;
                settings.ApplyModifiedProperties();
                Assert.That(copy.restPoseSupport, Is.EqualTo(.78f));
            }
            Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before));
        }

        [Test]
        public void Rebuilding_AppliesForcesAndStructuralChangesWithoutMutatingSourceOrTemplate()
        {
            setup.rootMotionMaxOffset = 0;
            var baseline = SoftDeformPreviewSettings.Capture(setup);
            template = SoftDeformLivePreview.CreateTemplate(avatar);
            string templateBefore = EditorJsonUtility.ToJson(template.GetComponent<SoftDeformPBSetup>());
            setup.rootMotionMaxOffset = .02f;
            setup.motionPull = .11f;
            setup.motionForceOverrides = SoftDeformMotionForceOverrides.Pull;
            var tuned = SoftDeformPreviewSettings.Capture(setup);
            baseline.Apply(setup);
            string sourceBefore = EditorJsonUtility.ToJson(setup);
            using (var a = SoftDeformLivePreview.Build(template, baseline))
            using (var b = SoftDeformLivePreview.Build(template, tuned))
            {
                Assert.That(a.Avatar.GetComponent<SoftDeformPBSetup>(), Is.Null);
                Assert.That(b.Avatar.GetComponent<SoftDeformPBSetup>(), Is.Null);
                var aBones = a.Avatar.GetComponentsInChildren<VRCPhysBone>(true);
                var bBones = b.Avatar.GetComponentsInChildren<VRCPhysBone>(true);
                Assert.That(bBones.Length, Is.GreaterThan(aBones.Length));
                Assert.That(aBones.Single(bone => bone.parameter == "SoftDeformPB_L").pull, Is.EqualTo(.7f));
                Assert.That(aBones.Single(bone => bone.parameter == "SoftDeformPB_R").pull, Is.EqualTo(.9f));
                Assert.That(bBones.Single(bone => bone.parameter == "SoftDeformPB_L").pull, Is.EqualTo(.11f));
                Assert.That(bBones.Single(bone => bone.parameter == "SoftDeformPB_R").pull, Is.EqualTo(.11f));
                Assert.That(b.Avatar.activeSelf, Is.False);
                Assert.That(SoftDeformLivePreview.FindFxController(b.Avatar), Is.Not.Null);
            }
            Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(sourceBefore));
            Assert.That(EditorJsonUtility.ToJson(template.GetComponent<SoftDeformPBSetup>()), Is.EqualTo(templateBefore));
            Assert.That(left.pull, Is.EqualTo(.7f)); Assert.That(right.pull, Is.EqualTo(.9f));
        }

        [Test]
        public void FailedRebuild_LeavesThePreviousPreviewAlive()
        {
            template = SoftDeformLivePreview.CreateTemplate(avatar);
            var baseline = SoftDeformPreviewSettings.Capture(setup);
            using (var good = SoftDeformLivePreview.Build(template, baseline))
            {
                setup.parameterPrefix = "";
                var invalid = SoftDeformPreviewSettings.Capture(setup);
                bool previous = LogAssert.ignoreFailingMessages;
                try
                {
                    LogAssert.ignoreFailingMessages = true;
                    Assert.Throws<InvalidOperationException>(() => SoftDeformLivePreview.Build(template, invalid));
                }
                finally { LogAssert.ignoreFailingMessages = previous; }
                Assert.That(good.Avatar, Is.Not.Null);
                Assert.That(good.Avatar.GetComponentsInChildren<VRCPhysBone>(true).Length, Is.GreaterThan(0));
                Assert.That(template.GetComponent<SoftDeformPBSetup>(), Is.Not.Null);
            }
        }

        [Test]
        public void FxParameters_ReceivePhysBoneAndContactValues()
        {
            var controller = new AnimatorController();
            var machine = new AnimatorStateMachine();
            controller.AddLayer(new AnimatorControllerLayer { name = "FX", defaultWeight = 1, stateMachine = machine });
            controller.AddParameter("Test_Squish", AnimatorControllerParameterType.Float);
            controller.AddParameter("Test_IsGrabbed", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Touch", AnimatorControllerParameterType.Float);
            left.parameter = "Test";
            var contact = avatar.AddComponent<VRCContactReceiver>();
            contact.parameter = "Touch";
            var graph = PlayableGraph.Create("Preview parameter test");
            try
            {
                var fx = AnimatorControllerPlayable.Create(graph, controller);
                Action refresh = SoftDeformLivePreview.ConnectParameters(avatar, fx);
                left.param_Squish.floatVal = .63f;
                left.param_IsGrabbed.boolVal = true;
                contact.paramAccess.floatVal = .41f;
                Assert.That(fx.GetFloat("Test_Squish"), Is.EqualTo(.63f));
                Assert.That(fx.GetBool("Test_IsGrabbed"), Is.True);
                Assert.That(fx.GetFloat("Touch"), Is.EqualTo(.41f));
                left.param_Squish = null; // SDK initialization can replace bindings after enabling a bone.
                refresh();
                left.param_Squish.floatVal = .27f;
                Assert.That(fx.GetFloat("Test_Squish"), Is.EqualTo(.27f));
                graph.Destroy();
                Assert.DoesNotThrow(() => left.param_Squish.floatVal = .2f);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(machine);
            }
        }

        [Test]
        public void FxDefaults_UseExpressionDefaultsAndPreserveBindingsForMissingParameters()
        {
            var controller = new AnimatorController();
            var expressions = ScriptableObject.CreateInstance<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>();
            var machine = new AnimatorStateMachine();
            controller.AddLayer(new AnimatorControllerLayer { name = "FX", defaultWeight = 1, stateMachine = machine });
            controller.AddParameter("Size", AnimatorControllerParameterType.Float);
            controller.AddParameter("Choice", AnimatorControllerParameterType.Int);
            controller.AddParameter("Enabled", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
            expressions.parameters = new[]
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = "Size", defaultValue = 1 },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = "Choice", defaultValue = 2 },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = "Enabled", defaultValue = 1 }
            };
            avatar.GetComponent<VRCAvatarDescriptor>().expressionParameters = expressions;
            left.parameter = "Missing";
            var existing = new TestParameterAccess();
            left.param_Squish = existing;
            var graph = PlayableGraph.Create("Preview default test");
            try
            {
                var fx = AnimatorControllerPlayable.Create(graph, controller);
                SoftDeformLivePreview.ConnectParameters(avatar, fx);
                Assert.That(fx.GetFloat("Size"), Is.EqualTo(1));
                Assert.That(fx.GetInteger("Choice"), Is.EqualTo(2));
                Assert.That(fx.GetBool("Enabled"), Is.True);
                Assert.That(fx.GetBool("IsLocal"), Is.True);
                Assert.That(left.param_Squish, Is.SameAs(existing));
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); Object.DestroyImmediate(expressions);
            }
        }

        private sealed class TestParameterAccess : VRC.SDKBase.IAnimParameterAccess
        {
            public bool boolVal { get; set; }
            public int intVal { get; set; }
            public float floatVal { get; set; }
        }
    }
}
