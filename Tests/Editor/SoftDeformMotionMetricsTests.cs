using NUnit.Framework;
using UnityEngine;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformMotionMetricsTests
    {
        [Test]
        public void ExplicitReference_IncludesSagInFirstSampleAndStartsTimeAtZero()
        {
            var collector = new SoftDeformPB.Editor.SoftDeformMotionMetricsCollector();
            collector.SetReferencePose(new Vector3(-0.1f, 0, 0), new Vector3(0.1f, 0, 0), Vector3.one, Vector3.one);
            collector.AddSample(100, new Vector3(-0.1f, -0.02f, 0), new Vector3(0.1f, -0.01f, 0),
                new Vector3(1, 1.04f, 1), Vector3.one);
            Assert.That(collector.HasExplicitBaseline, Is.True);
            Assert.That(collector.Samples[0].time, Is.Zero);
            Assert.That(collector.Samples[0].leftDisplacement, Is.EqualTo(0.02f).Within(0.000001f));
            Assert.That(collector.PeakRelativeScaleDeviation, Is.EqualTo(0.04f).Within(0.000001f));
            collector.AddSample(100.5, new Vector3(-0.1f, -0.01f, 0), new Vector3(0.1f, 0, 0), Vector3.one, Vector3.one);
            Assert.That(collector.Samples[1].leftSpeed, Is.EqualTo(0.02f).Within(0.000001f));
            Assert.That(collector.Duration, Is.EqualTo(0.5).Within(0.000001f));
            collector.Reset();
            Assert.That(collector.HasBaseline, Is.False);
            Assert.That(collector.HasExplicitBaseline, Is.False);
        }

        [Test]
        public void ExplicitReference_RejectsInvalidPoseWithoutDiscardingMeasurements()
        {
            var collector = new SoftDeformPB.Editor.SoftDeformMotionMetricsCollector();
            collector.AddSample(100, Vector3.zero, Vector3.zero, Vector3.one, Vector3.one);
            Assert.Throws<System.ArgumentException>(() => collector.SetReferencePose(
                new Vector3(float.NaN, 0, 0), Vector3.zero, Vector3.one, Vector3.one));
            Assert.That(collector.SampleCount, Is.EqualTo(1));
            Assert.That(collector.HasExplicitBaseline, Is.False);
        }

        [Test]
        public void Collector_ReportsChestRelativeDisplacementSpeedAndAsymmetry()
        {
            var collector = new SoftDeformPB.Editor.SoftDeformMotionMetricsCollector();

            Assert.That(collector.AddSample(
                100.0,
                new Vector3(-0.1f, 0.0f, 0.0f),
                new Vector3(0.1f, 0.0f, 0.0f),
                Vector3.one,
                Vector3.one), Is.True);
            Assert.That(collector.AddSample(
                100.5,
                new Vector3(-0.1f, 0.03f, 0.0f),
                new Vector3(0.1f, 0.02f, 0.0f),
                new Vector3(1.1f, 1.0f, 1.0f),
                Vector3.one), Is.True);

            Assert.That(collector.SampleCount, Is.EqualTo(2));
            Assert.That(collector.PeakLeftDisplacement, Is.EqualTo(0.03f).Within(0.0001f));
            Assert.That(collector.PeakRightDisplacement, Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(collector.PeakLeftSpeed, Is.EqualTo(0.06f).Within(0.0001f));
            Assert.That(collector.PeakRightSpeed, Is.EqualTo(0.04f).Within(0.0001f));
            Assert.That(collector.PeakMagnitudeAsymmetry, Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(collector.PeakRelativeScaleDeviation, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(collector.Duration, Is.EqualTo(0.5).Within(0.0001));
        }

        [Test]
        public void Collector_RejectsInvalidTransformsWithoutPollutingMetrics()
        {
            var collector = new SoftDeformPB.Editor.SoftDeformMotionMetricsCollector();

            Assert.That(collector.AddSample(
                0.0,
                new Vector3(float.NaN, 0.0f, 0.0f),
                Vector3.zero,
                Vector3.one,
                Vector3.one), Is.False);

            Assert.That(collector.SampleCount, Is.Zero);
            Assert.That(collector.InvalidSampleCount, Is.EqualTo(1));
            Assert.That(collector.HasBaseline, Is.False);
        }
    }
}
