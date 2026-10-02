using System;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformPreviewOverrideTests
    {
        private GameObject avatar;
        private Animator animator;
        private SkinnedMeshRenderer body, garment;
        private Mesh mesh;
        private AnimationClip clip;
        private AnimatorController controller;
        private AnimatorStateMachine machine;
        private AnimatorState state;
        private VRCExpressionParameters expressions;
        private VRCExpressionsMenu menu;
        private PlayableGraph graph;
        private SoftDeformPreviewShapeLayer shapeLayer;

        [SetUp]
        public void CreateAnimatedAvatar()
        {
            avatar = new GameObject("Shape override test");
            animator = avatar.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            expressions = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expressions.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "Size", defaultValue = .5f },
                new VRCExpressionParameters.Parameter { name = "Choice", defaultValue = 2 },
                new VRCExpressionParameters.Parameter { name = "Visible", defaultValue = 1 }
            };
            descriptor.expressionParameters = expressions;
            menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls.Add(new VRCExpressionsMenu.Control { name = "胸サイズ", type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = "Size" } } });
            descriptor.expressionsMenu = menu;
            mesh = new Mesh { name = "Test shapes", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (string name in new[] { "Big", "Small", "Smile" })
                mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.forward, Vector3.forward, Vector3.forward }, new Vector3[3], new Vector3[3]);
            body = Renderer("Body");
            garment = Renderer("Clothes");
            clip = new AnimationClip { name = "Animated size" };
            Curve("Body", "blendShape.Big", 10, 20);
            Curve("Clothes", "blendShape.Big", 10, 20);
            Curve("Body", "blendShape.Smile", 30, 90);
            Curve("Body", "m_LocalPosition.y", 0, 1, typeof(Transform));
            controller = new AnimatorController();
            machine = new AnimatorStateMachine();
            state = machine.AddState("Size");
            state.motion = clip;
            state.writeDefaultValues = false;
            machine.defaultState = state;
            controller.AddLayer(new AnimatorControllerLayer { name = "FX", defaultWeight = 1, stateMachine = machine });
            controller.AddParameter("Size", AnimatorControllerParameterType.Float);
            controller.AddParameter("Choice", AnimatorControllerParameterType.Int);
            controller.AddParameter("Visible", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Trigger", AnimatorControllerParameterType.Trigger);
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer {
                type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = controller } };
            graph = PlayableGraph.Create("Shape override tests");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        }

        private SkinnedMeshRenderer Renderer(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(avatar.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.updateWhenOffscreen = true;
            return renderer;
        }

        private void Curve(string path, string property, float start, float end, Type type = null)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type ?? typeof(SkinnedMeshRenderer), property),
                AnimationCurve.Linear(0, start, 1, end));
        }

        private AnimationClipPlayable ConnectClip(SoftDeformPreviewOverrides settings)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            var output = AnimationPlayableOutput.Create(graph, "Shapes", animator);
            output.SetSourcePlayable(playable);
            shapeLayer = new SoftDeformPreviewShapeLayer(graph, animator, output);
            shapeLayer.Refresh(settings);
            graph.Play();
            return playable;
        }

        [TearDown]
        public void Release()
        {
            shapeLayer?.Dispose();
            shapeLayer = null;
            if (graph.IsValid()) graph.Destroy();
            Object.DestroyImmediate(avatar);
            Object.DestroyImmediate(controller);
            Object.DestroyImmediate(state);
            Object.DestroyImmediate(machine);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(expressions);
            Object.DestroyImmediate(menu);
        }

        [Test]
        public void FinalShapeOverride_WinsAcrossFramesAndKeepsOtherShapesTransformsAndClothesAnimated()
        {
            var settings = new SoftDeformPreviewOverrides();
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Body", name = "Big", value = 100 });
            var playable = ConnectClip(settings);
            foreach (float time in new[] { 0f, .2f, .7f, .9f })
            {
                playable.SetTime(time);
                graph.Evaluate(0);
                Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(100).Within(.01));
                Assert.That(body.GetBlendShapeWeight(2), Is.EqualTo(Mathf.Lerp(30, 90, time)).Within(.01));
                Assert.That(garment.GetBlendShapeWeight(0), Is.EqualTo(Mathf.Lerp(10, 20, time)).Within(.01));
                Assert.That(body.transform.localPosition.y, Is.EqualTo(time).Within(.001));
            }
            settings.shapes[0].value = 67;
            shapeLayer.Refresh(settings);
            double previousTime = playable.GetTime();
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(67).Within(.01));
            Assert.That(playable.GetTime(), Is.EqualTo(previousTime));
        }

        [Test]
        public void DisablingRemovingAndReaddingOverrides_RestoresAnimationWithoutRestartingMotion()
        {
            var settings = new SoftDeformPreviewOverrides();
            var entry = new SoftDeformPreviewOverrides.Shape { rendererPath = "Body", name = "Big", value = 88 };
            settings.shapes.Add(entry);
            var playable = ConnectClip(settings);
            playable.SetTime(.6);
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(88).Within(.01));
            entry.enabled = false;
            shapeLayer.Refresh(settings);
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(16).Within(.01));
            settings.shapes.Clear();
            shapeLayer.Refresh(settings);
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(16).Within(.01));
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Clothes", name = "Big", value = 99 });
            shapeLayer.Refresh(settings);
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(16).Within(.01));
            Assert.That(garment.GetBlendShapeWeight(0), Is.EqualTo(99).Within(.01));
            Assert.That(playable.GetTime(), Is.EqualTo(.6).Within(.0001));
        }

        [Test]
        public void UnknownTargetsAndNonfiniteValues_LeaveAnimationUntouched()
        {
            var settings = new SoftDeformPreviewOverrides();
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Missing", name = "Big", value = 100 });
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Body", name = "Missing", value = 100 });
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Body", name = "Big", value = float.NaN });
            var playable = ConnectClip(settings);
            playable.SetTime(.4);
            graph.Evaluate(0);
            Assert.That(shapeLayer.BoundShapeCount, Is.EqualTo(1));
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(14).Within(.01));
        }

        [Test]
        public void UnanimatedShapeOverride_ReleasesToAuthoredValue()
        {
            body.SetBlendShapeWeight(1, 35);
            var settings = new SoftDeformPreviewOverrides();
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Body", name = "Small", value = 75 });
            ConnectClip(settings);
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(1), Is.EqualTo(75).Within(.01));
            settings.shapes[0].enabled = false;
            shapeLayer.Refresh(settings);
            graph.Evaluate(0);
            Assert.That(body.GetBlendShapeWeight(1), Is.EqualTo(35).Within(.01));
        }

        [Test]
        public void SizeParameter_ChangesAnimatedBodyAndGarmentTogether()
        {
            state.timeParameter = "Size";
            state.timeParameterActive = true;
            var settings = new SoftDeformPreviewOverrides();
            settings.parameters.Add(new SoftDeformPreviewOverrides.Parameter { name = "Size", enabled = true, value = 1 });
            var fx = AnimatorControllerPlayable.Create(graph, controller);
            var output = AnimationPlayableOutput.Create(graph, "FX", animator);
            output.SetSourcePlayable(fx);
            Action refresh = SoftDeformLivePreview.ConnectParameters(avatar, fx, settings);
            graph.Play();
            foreach (float size in new[] { 0f, .5f, 1f, .25f })
            {
                settings.parameters[0].value = size;
                refresh();
                graph.Evaluate(.1f);
                Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(Mathf.Lerp(10, 20, size)).Within(.01));
                Assert.That(garment.GetBlendShapeWeight(0), Is.EqualTo(body.GetBlendShapeWeight(0)).Within(.01));
            }
            Assert.That(expressions.parameters[0].defaultValue, Is.EqualTo(.5f));
        }

        [Test]
        public void ParameterOverrides_ReapplyAfterExternalWritesAndRestoreExpressionDefaultsOnRelease()
        {
            var settings = new SoftDeformPreviewOverrides();
            settings.parameters.Add(new SoftDeformPreviewOverrides.Parameter { name = "Size", enabled = true, value = 1 });
            settings.parameters.Add(new SoftDeformPreviewOverrides.Parameter { name = "Choice", enabled = true, value = 7 });
            settings.parameters.Add(new SoftDeformPreviewOverrides.Parameter { name = "Visible", enabled = true, value = 0 });
            settings.parameters.Add(new SoftDeformPreviewOverrides.Parameter { name = "Missing", enabled = true, value = 1 });
            var fx = AnimatorControllerPlayable.Create(graph, controller);
            Action refresh = SoftDeformLivePreview.ConnectParameters(avatar, fx, settings);
            for (int i = 0; i < 5; i++)
            {
                fx.SetFloat("Size", .2f); fx.SetInteger("Choice", 3); fx.SetBool("Visible", true);
                refresh();
                Assert.That(fx.GetFloat("Size"), Is.EqualTo(1));
                Assert.That(fx.GetInteger("Choice"), Is.EqualTo(7));
                Assert.That(fx.GetBool("Visible"), Is.False);
            }
            settings.parameters[0].enabled = false;
            settings.parameters.RemoveAt(1);
            settings.parameters[1].enabled = false;
            refresh();
            Assert.That(fx.GetFloat("Size"), Is.EqualTo(.5f));
            Assert.That(fx.GetInteger("Choice"), Is.EqualTo(2));
            Assert.That(fx.GetBool("Visible"), Is.True);
            fx.SetFloat("Size", .3f);
            refresh();
            Assert.That(fx.GetFloat("Size"), Is.EqualTo(.3f), "Released parameters must be free to animate again.");
            Assert.That(expressions.parameters[0].defaultValue, Is.EqualTo(.5f));
        }

        [Test]
        public void ParameterOptions_DiscoverShapeTimeControlAndMenuLabelWithoutEnablingOverrides()
        {
            state.timeParameter = "Size";
            state.timeParameterActive = true;
            var options = SoftDeformPreviewOverrides.ReadOptions(avatar);
            var settings = new SoftDeformPreviewOverrides();
            settings.Initialize(options);
            Assert.That(settings.parameters.Count, Is.EqualTo(1));
            Assert.That(settings.parameters[0].name, Is.EqualTo("Size"));
            Assert.That(settings.parameters[0].value, Is.EqualTo(.5f));
            Assert.That(settings.parameters[0].enabled, Is.False);
            Assert.That(options[0].Label, Is.EqualTo("胸サイズ (Size)"));
            Assert.That(options[0].Radial, Is.True);
            settings.parameters[0].value = .8f;
            settings.Initialize(options);
            Assert.That(settings.parameters.Count, Is.EqualTo(1));
            Assert.That(settings.parameters[0].value, Is.EqualTo(.8f));
        }

        [Test]
        public void PreviewOverrideSerialization_PreservesPathsValuesAndEnabledStates()
        {
            var settings = new SoftDeformPreviewOverrides();
            settings.parameters.Add(new SoftDeformPreviewOverrides.Parameter { name = "Size", enabled = true, value = 1 });
            settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = "Clothes", name = "Big", value = 76 });
            var roundTrip = JsonUtility.FromJson<SoftDeformPreviewOverrides>(JsonUtility.ToJson(settings));
            Assert.That(roundTrip.parameters[0].name, Is.EqualTo("Size"));
            Assert.That(roundTrip.parameters[0].enabled, Is.True);
            Assert.That(roundTrip.parameters[0].value, Is.EqualTo(1));
            Assert.That(roundTrip.shapes[0].rendererPath, Is.EqualTo("Clothes"));
            Assert.That(roundTrip.shapes[0].value, Is.EqualTo(76));
            Assert.That(roundTrip.shapes[0].enabled, Is.True);
        }
    }
}
