using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformTuningGuideTests
    {
        [Test]
        public void OpeningSetupInspector_DoesNotLogOrChangeConfiguration()
        {
            var avatar = new GameObject("Tuning guide lifecycle test");
            UnityEditor.Editor inspector = null;
            try
            {
                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                setup.preserveExistingMotion = true;
                setup.motionForceOverrides = SoftDeformMotionForceOverrides.Pull;
                setup.motionPull = .25f;
                string before = EditorJsonUtility.ToJson(setup);
                inspector = UnityEditor.Editor.CreateEditor(setup);
                Assert.That(inspector.GetType().FullName, Is.EqualTo("SoftDeformPB.Editor.SoftDeformPBInspector"));
                Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
            }
        }
    }
}
