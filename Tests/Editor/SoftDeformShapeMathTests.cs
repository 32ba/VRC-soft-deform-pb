using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEngine;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformShapeMathTests
    {
        [TestCase(0, 1, 2, 1.0f)]
        [TestCase(1, 0, 2, 1.0f)]
        [TestCase(2, 0, 1, 1.0f)]
        [TestCase(0, 1, 2, -1.0f)]
        [TestCase(1, 0, 2, -1.0f)]
        [TestCase(2, 0, 1, -1.0f)]
        public void VisualSupineShape_ShortensAndPreservesScaleProductAcrossAxes(
            int longitudinal, int horizontal, int vertical, float sign)
        {
            var axes = new SoftDeformAxisMap(longitudinal, horizontal, vertical);
            for (int step = 0; step <= 10; step++)
            {
                float weight = step / 10.0f;
                Vector3 scale = SoftDeformShapeMath.EvaluateGravityScale(
                    Vector3.one, axes, 0, -sign * weight, sign, 0.2f, 0.1f);
                Assert.That(scale[longitudinal], Is.EqualTo(1 - 0.1f * weight).Within(0.00001f));
                Assert.That(scale[horizontal], Is.EqualTo(1 + 0.2f * weight).Within(0.00001f));
                Assert.That(scale.x * scale.y * scale.z, Is.EqualTo(1.0f).Within(0.00001f));
            }
            Assert.That(SoftDeformShapeMath.EvaluateGravityScale(
                Vector3.one, axes, 0, sign, sign, 0.2f, 0.1f), Is.EqualTo(Vector3.one));
        }

        [TestCase(0.0f, 15.0f)]
        [TestCase(0.00001f, 30.0f)]
        [TestCase(0.01f, 15.0f)]
        [TestCase(0.05f, 1.0f)]
        public void RootLeverLength_BoundsTheFullChord(float maxOffset, float angle)
        {
            float length = SoftDeformShapeMath.RootLeverLength(maxOffset, angle);
            Vector3 tip = Vector3.up * length;
            for (int step = 0; step <= 20; step++)
            {
                Vector3 moved = Quaternion.AngleAxis(angle * step / 20.0f, Vector3.right) * tip;
                Assert.That(Vector3.Distance(tip, moved), Is.LessThanOrEqualTo(maxOffset + 0.000001f));
            }
            Assert.That(2.0f * length * Mathf.Sin(angle * Mathf.Deg2Rad * 0.5f),
                Is.EqualTo(maxOffset).Within(0.000001f));
        }

        [Test]
        public void RootLeverLength_RejectsInvalidGeometry()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SoftDeformShapeMath.RootLeverLength(float.NaN, 15));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SoftDeformShapeMath.RootLeverLength(-0.01f, 15));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SoftDeformShapeMath.RootLeverLength(0.01f, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SoftDeformShapeMath.RootLeverLength(0.01f, float.PositiveInfinity));
        }

        [TestCase(0, 2, 1)]
        [TestCase(1, 0, 2)]
        [TestCase(2, 0, 1)]
        public void UprightGravity_WithZeroCorrectionPreservesAuthoredScale(int longitudinal, int horizontal, int vertical)
        {
            var rest = new Vector3(1.2f, 0.8f, 1.4f);
            var scale = SoftDeformShapeMath.EvaluateGravityScale(rest,
                new SoftDeformAxisMap(longitudinal, horizontal, vertical), 0, 0, -1, 0.2f);
            Assert.That(scale, Is.EqualTo(rest));
        }

        [TestCase(1, 0, 0)]
        [TestCase(0, -2, 0)]
        [TestCase(0, 0, 3)]
        public void DominantAxis_SelectsLargestComponent(float x, float y, float z)
        {
            int expected = x != 0 ? 0 : y != 0 ? 1 : 2;
            Assert.That(SoftDeformShapeMath.DominantAxis(new Vector3(x, y, z)), Is.EqualTo(expected));
        }

        [Test]
        public void FullVolumeRetention_CompensatesNativePhysBoneLengthChange()
        {
            var axes = new SoftDeformAxisMap(2, 0, 1);
            const float squash = 0.75f;
            const float stretch = 0.2f;
            const float squashDepth = 0.32f;
            const float stretchDepth = 0.12f;
            Vector3 scale = SoftDeformShapeMath.EvaluateScale(
                Vector3.one, axes, squash, stretch, squashDepth, stretchDepth, 1.0f, 0.55f);
            float nativeLengthRatio = SoftDeformShapeMath.EvaluateLengthRatio(
                squash, stretch, squashDepth, stretchDepth);

            Assert.That(nativeLengthRatio * scale.x * scale.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(scale.z, Is.EqualTo(1.0f));
            Assert.That(scale.x, Is.GreaterThan(1.0f));
            Assert.That(scale.y, Is.GreaterThan(1.0f));
        }

        [Test]
        public void ZeroInputs_ReturnRestScale()
        {
            var rest = new Vector3(1.2f, 0.8f, 1.4f);
            Vector3 result = SoftDeformShapeMath.EvaluateScale(
                rest, new SoftDeformAxisMap(0, 2, 1), 0, 0, 0.32f, 0.12f, 0.9f, 0.55f);

            Assert.That(result, Is.EqualTo(rest));
        }

        [Test]
        public void UprightGravity_PreservesAuthoredShape()
        {
            var axes = new SoftDeformAxisMap(2, 0, 1);
            Vector3 scale = SoftDeformShapeMath.EvaluateGravityScale(
                new Vector3(1.2f, 0.8f, 1.4f),
                axes,
                0.0f,
                0.0f,
                1.0f,
                0.14f);

            Assert.That(scale.x * scale.y, Is.EqualTo(1.2f * 0.8f).Within(0.0001f));
            Assert.That(scale.y, Is.EqualTo(0.8f));
            Assert.That(scale.z, Is.EqualTo(1.4f));
        }

        [TestCase(1.0f)]
        [TestCase(-1.0f)]
        public void SupineGravity_WidensForEitherLongitudinalAxisSign(float outwardSign)
        {
            var axes = new SoftDeformAxisMap(2, 0, 1);
            Vector3 scale = SoftDeformShapeMath.EvaluateGravityScale(
                Vector3.one,
                axes,
                0.0f,
                -outwardSign,
                outwardSign,
                0.14f);

            Assert.That(scale.x, Is.EqualTo(1.14f).Within(0.0001f));
            Assert.That(scale.y, Is.EqualTo(1.0f / 1.14f).Within(0.0001f));
            Assert.That(scale.z, Is.EqualTo(1.0f));
        }

        [Test]
        public void SideGravity_LeavesShapeToNativePhysBoneMotion()
        {
            var axes = new SoftDeformAxisMap(2, 0, 1);
            Vector3 scale = SoftDeformShapeMath.EvaluateGravityScale(
                Vector3.one,
                axes,
                -1.0f,
                0.0f,
                1.0f,
                0.14f);

            Assert.That(scale, Is.EqualTo(Vector3.one));
        }

        [TestCase(1.0f)]
        [TestCase(-1.0f)]
        public void ProneGravity_LeavesRestShapeForContactSquish(float outwardSign)
        {
            var axes = new SoftDeformAxisMap(2, 0, 1);
            Vector3 scale = SoftDeformShapeMath.EvaluateGravityScale(
                new Vector3(1.2f, 0.8f, 1.4f),
                axes,
                0.0f,
                outwardSign,
                outwardSign,
                0.14f);

            Assert.That(scale, Is.EqualTo(new Vector3(1.2f, 0.8f, 1.4f)));
        }
    }
}
