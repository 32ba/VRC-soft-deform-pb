using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    [Serializable]
    public struct SoftDeformMotionSample
    {
        public double time;
        public Vector3 leftChestLocalPosition;
        public Vector3 rightChestLocalPosition;
        public Vector3 leftLocalScale;
        public Vector3 rightLocalScale;
        public float leftDisplacement;
        public float rightDisplacement;
        public float leftSpeed;
        public float rightSpeed;
    }

    [Serializable]
    public sealed class SoftDeformMotionMetricsCollector
    {
        [SerializeField] private List<SoftDeformMotionSample> samples = new List<SoftDeformMotionSample>();
        [SerializeField] private int invalidSampleCount;
        [SerializeField] private Vector3 leftBaselinePosition;
        [SerializeField] private Vector3 rightBaselinePosition;
        [SerializeField] private Vector3 leftBaselineScale;
        [SerializeField] private Vector3 rightBaselineScale;
        [SerializeField] private bool hasBaseline;
        [SerializeField] private bool hasTimeOrigin;
        [SerializeField] private bool hasExplicitBaseline;
        [SerializeField] private double timeOrigin;
        [SerializeField] private float peakLeftDisplacement;
        [SerializeField] private float peakRightDisplacement;
        [SerializeField] private float peakLeftSpeed;
        [SerializeField] private float peakRightSpeed;
        [SerializeField] private float peakMagnitudeAsymmetry;
        [SerializeField] private float peakRelativeScaleDeviation;
        [SerializeField] private double sumLeftDisplacementSquared;
        [SerializeField] private double sumRightDisplacementSquared;
        [SerializeField] private double sumMagnitudeAsymmetrySquared;

        public IReadOnlyList<SoftDeformMotionSample> Samples => samples;
        public int SampleCount => samples.Count;
        public int InvalidSampleCount => invalidSampleCount;
        public bool HasBaseline => hasBaseline;
        public bool HasExplicitBaseline => hasExplicitBaseline;
        public float PeakLeftDisplacement => peakLeftDisplacement;
        public float PeakRightDisplacement => peakRightDisplacement;
        public float PeakLeftSpeed => peakLeftSpeed;
        public float PeakRightSpeed => peakRightSpeed;
        public float PeakMagnitudeAsymmetry => peakMagnitudeAsymmetry;
        public float PeakRelativeScaleDeviation => peakRelativeScaleDeviation;
        public float RmsLeftDisplacement => RootMeanSquare(sumLeftDisplacementSquared, samples.Count);
        public float RmsRightDisplacement => RootMeanSquare(sumRightDisplacementSquared, samples.Count);
        public float RmsMagnitudeAsymmetry => RootMeanSquare(sumMagnitudeAsymmetrySquared, samples.Count);
        public double Duration => samples.Count < 2 ? 0.0 : samples[samples.Count - 1].time - samples[0].time;

        public void Reset()
        {
            samples.Clear();
            invalidSampleCount = 0;
            leftBaselinePosition = Vector3.zero;
            rightBaselinePosition = Vector3.zero;
            leftBaselineScale = Vector3.one;
            rightBaselineScale = Vector3.one;
            hasBaseline = false;
            hasTimeOrigin = false;
            hasExplicitBaseline = false;
            timeOrigin = 0.0;
            peakLeftDisplacement = 0.0f;
            peakRightDisplacement = 0.0f;
            peakLeftSpeed = 0.0f;
            peakRightSpeed = 0.0f;
            peakMagnitudeAsymmetry = 0.0f;
            peakRelativeScaleDeviation = 0.0f;
            sumLeftDisplacementSquared = 0.0;
            sumRightDisplacementSquared = 0.0;
            sumMagnitudeAsymmetrySquared = 0.0;
        }

        public void SetReferencePose(
            Vector3 leftPosition, Vector3 rightPosition, Vector3 leftScale, Vector3 rightScale)
        {
            if (!IsFinite(leftPosition) || !IsFinite(rightPosition) || !IsFinite(leftScale) || !IsFinite(rightScale))
                throw new ArgumentException("Reference pose must contain finite positions and scales.");
            Reset();
            leftBaselinePosition = leftPosition;
            rightBaselinePosition = rightPosition;
            leftBaselineScale = leftScale;
            rightBaselineScale = rightScale;
            hasBaseline = true;
            hasExplicitBaseline = true;
        }

        public bool AddSample(
            double time,
            Vector3 leftChestLocalPosition,
            Vector3 rightChestLocalPosition,
            Vector3 leftLocalScale,
            Vector3 rightLocalScale)
        {
            if (!IsFinite(time) ||
                !IsFinite(leftChestLocalPosition) ||
                !IsFinite(rightChestLocalPosition) ||
                !IsFinite(leftLocalScale) ||
                !IsFinite(rightLocalScale))
            {
                invalidSampleCount++;
                return false;
            }

            if (!hasBaseline)
            {
                leftBaselinePosition = leftChestLocalPosition;
                rightBaselinePosition = rightChestLocalPosition;
                leftBaselineScale = leftLocalScale;
                rightBaselineScale = rightLocalScale;
                hasBaseline = true;
            }

            if (!hasTimeOrigin)
            {
                timeOrigin = time;
                hasTimeOrigin = true;
            }

            double relativeTime = time - timeOrigin;

            float leftDisplacement = Vector3.Distance(leftBaselinePosition, leftChestLocalPosition);
            float rightDisplacement = Vector3.Distance(rightBaselinePosition, rightChestLocalPosition);
            float leftSpeed = 0.0f;
            float rightSpeed = 0.0f;
            if (samples.Count > 0)
            {
                SoftDeformMotionSample previous = samples[samples.Count - 1];
                double deltaTime = relativeTime - previous.time;
                if (deltaTime > 1e-6)
                {
                    leftSpeed = Vector3.Distance(previous.leftChestLocalPosition, leftChestLocalPosition) /
                                (float)deltaTime;
                    rightSpeed = Vector3.Distance(previous.rightChestLocalPosition, rightChestLocalPosition) /
                                 (float)deltaTime;
                }
            }

            var sample = new SoftDeformMotionSample
            {
                time = relativeTime,
                leftChestLocalPosition = leftChestLocalPosition,
                rightChestLocalPosition = rightChestLocalPosition,
                leftLocalScale = leftLocalScale,
                rightLocalScale = rightLocalScale,
                leftDisplacement = leftDisplacement,
                rightDisplacement = rightDisplacement,
                leftSpeed = leftSpeed,
                rightSpeed = rightSpeed
            };
            samples.Add(sample);

            float magnitudeAsymmetry = Mathf.Abs(leftDisplacement - rightDisplacement);
            peakLeftDisplacement = Mathf.Max(peakLeftDisplacement, leftDisplacement);
            peakRightDisplacement = Mathf.Max(peakRightDisplacement, rightDisplacement);
            peakLeftSpeed = Mathf.Max(peakLeftSpeed, leftSpeed);
            peakRightSpeed = Mathf.Max(peakRightSpeed, rightSpeed);
            peakMagnitudeAsymmetry = Mathf.Max(peakMagnitudeAsymmetry, magnitudeAsymmetry);
            peakRelativeScaleDeviation = Mathf.Max(
                peakRelativeScaleDeviation,
                Mathf.Max(
                    RelativeScaleDeviation(leftBaselineScale, leftLocalScale),
                    RelativeScaleDeviation(rightBaselineScale, rightLocalScale)));
            sumLeftDisplacementSquared += leftDisplacement * leftDisplacement;
            sumRightDisplacementSquared += rightDisplacement * rightDisplacement;
            sumMagnitudeAsymmetrySquared += magnitudeAsymmetry * magnitudeAsymmetry;
            return true;
        }

        public void WriteCsv(string path)
        {
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(
                    "time_s,left_x_m,left_y_m,left_z_m,right_x_m,right_y_m,right_z_m," +
                    "left_displacement_m,right_displacement_m,left_speed_mps,right_speed_mps," +
                    "left_scale_x,left_scale_y,left_scale_z,right_scale_x,right_scale_y,right_scale_z");
                foreach (SoftDeformMotionSample sample in samples)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Format(sample.time),
                        Format(sample.leftChestLocalPosition.x),
                        Format(sample.leftChestLocalPosition.y),
                        Format(sample.leftChestLocalPosition.z),
                        Format(sample.rightChestLocalPosition.x),
                        Format(sample.rightChestLocalPosition.y),
                        Format(sample.rightChestLocalPosition.z),
                        Format(sample.leftDisplacement),
                        Format(sample.rightDisplacement),
                        Format(sample.leftSpeed),
                        Format(sample.rightSpeed),
                        Format(sample.leftLocalScale.x),
                        Format(sample.leftLocalScale.y),
                        Format(sample.leftLocalScale.z),
                        Format(sample.rightLocalScale.x),
                        Format(sample.rightLocalScale.y),
                        Format(sample.rightLocalScale.z)
                    }));
                }
            }
        }

        private static float RootMeanSquare(double sumSquared, int count)
        {
            return count == 0 ? 0.0f : Mathf.Sqrt((float)(sumSquared / count));
        }

        private static float RelativeScaleDeviation(Vector3 baseline, Vector3 value)
        {
            float x = RelativeDeviation(baseline.x, value.x);
            float y = RelativeDeviation(baseline.y, value.y);
            float z = RelativeDeviation(baseline.z, value.z);
            return Mathf.Max(x, Mathf.Max(y, z));
        }

        private static float RelativeDeviation(float baseline, float value)
        {
            return Mathf.Abs(baseline) < 1e-6f ? Mathf.Abs(value - baseline) : Mathf.Abs(value / baseline - 1.0f);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string Format(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
