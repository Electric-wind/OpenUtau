using System;
using System.Collections.Generic;

namespace OpenUtau.Core.HiFiUtau {
    /// <summary>
    /// Mono ITU-R BS.1770-4 loudness normalization matching Hifisampler's
    /// pyloudnorm configuration.
    /// </summary>
    static class HiFiUtauLoudnessNormalizer {
        public const double TargetLufs = -16.0;
        public const double PeakLimitDb = -1.0;

        const double BlockSeconds = 0.400;
        const double AbsoluteGateLufs = -70.0;
        const double SilenceThresholdDb = -52.0;

        public static double NormalizeInPlace(
            float[] samples,
            int sampleRate,
            double strength,
            double targetLufs = TargetLufs,
            double peakLimitDb = PeakLimitDb,
            bool trimSilence = true) {
            if (samples == null || samples.Length == 0 || sampleRate <= 0) {
                return 0;
            }

            double gainDb = CalculateGainDb(
                samples, sampleRate, strength, targetLufs, peakLimitDb, trimSilence);
            if (!double.IsFinite(gainDb) || Math.Abs(gainDb) < 1e-9) {
                return 0;
            }

            float gain = (float)Math.Pow(10.0, gainDb / 20.0);
            for (int i = 0; i < samples.Length; i++) {
                samples[i] *= gain;
            }
            return gainDb;
        }

        public static void NormalizePhonesInPlace(
            float[] samples,
            HiFiUtauPhone[] phones,
            int sampleRate,
            double samplesPerModelFrame,
            double targetLufs = TargetLufs,
            double peakLimitDb = PeakLimitDb) {
            if (samples == null || samples.Length == 0 ||
                phones == null || phones.Length == 0 || sampleRate <= 0 ||
                !double.IsFinite(samplesPerModelFrame) || samplesPerModelFrame <= 0) {
                return;
            }

            int ToSample(int frame) => Math.Clamp(
                (int)Math.Round(frame * samplesPerModelFrame, MidpointRounding.AwayFromZero),
                0,
                samples.Length);

            var segments = new List<HiFiUtauGainSegment>();
            foreach (var phone in phones) {
                int start = ToSample(phone.ModelStartFrame);
                int end = ToSample(phone.ModelEndFrame);
                if (end <= start) {
                    continue;
                }

                var phoneSamples = new float[end - start];
                Array.Copy(samples, start, phoneSamples, 0, phoneSamples.Length);
                double gainDb = CalculateGainDb(
                    phoneSamples,
                    sampleRate,
                    phone.Normalize,
                    targetLufs,
                    peakLimitDb,
                    trimSilence: true);
                float gain = double.IsFinite(gainDb)
                    ? (float)Math.Pow(10.0, gainDb / 20.0)
                    : 1f;
                segments.Add(HiFiUtauGainSegment.FromPhone(
                    phone, start, end, gain, sampleRate));
            }
            if (segments.Count == 0) {
                return;
            }

            HiFiUtauGainSmoother.Apply(samples, segments);
        }

        public static double MeasureIntegratedLoudness(float[] samples, int sampleRate) {
            if (samples == null || samples.Length == 0 || sampleRate <= 0) {
                return double.NegativeInfinity;
            }
            var input = PadToBlock(samples, sampleRate);
            var shelf = Biquad.HighShelf(4.0, 1.0 / Math.Sqrt(2.0), 1500.0, sampleRate);
            var highPass = Biquad.HighPass(0.5, 38.0, sampleRate);
            var filtered = shelf.Filter(input);
            filtered = highPass.Filter(filtered);

            double duration = filtered.Length / (double)sampleRate;
            int blockCount = (int)Math.Round(
                (duration - BlockSeconds) / (BlockSeconds * 0.25),
                MidpointRounding.ToEven) + 1;
            blockCount = Math.Max(1, blockCount);
            int blockSamples = Math.Max(1, (int)(BlockSeconds * sampleRate));
            var meanSquares = new double[blockCount];
            var blockLoudness = new double[blockCount];

            for (int block = 0; block < blockCount; block++) {
                int start = (int)(BlockSeconds * (block * 0.25) * sampleRate);
                int end = Math.Min(filtered.Length,
                    (int)(BlockSeconds * (block * 0.25 + 1.0) * sampleRate));
                double sum = 0;
                for (int i = start; i < end; i++) {
                    sum += filtered[i] * filtered[i];
                }
                meanSquares[block] = sum / blockSamples;
                blockLoudness[block] = LoudnessFromMeanSquare(meanSquares[block]);
            }

            var absoluteGated = new List<int>();
            for (int block = 0; block < blockCount; block++) {
                if (blockLoudness[block] >= AbsoluteGateLufs) {
                    absoluteGated.Add(block);
                }
            }
            if (absoluteGated.Count == 0) {
                return double.NegativeInfinity;
            }

            double relativeGate = LoudnessFromMeanSquare(Mean(meanSquares, absoluteGated)) - 10.0;
            var relativeGated = new List<int>();
            for (int block = 0; block < blockCount; block++) {
                if (blockLoudness[block] > AbsoluteGateLufs && blockLoudness[block] > relativeGate) {
                    relativeGated.Add(block);
                }
            }
            if (relativeGated.Count == 0) {
                return double.NegativeInfinity;
            }
            return LoudnessFromMeanSquare(Mean(meanSquares, relativeGated));
        }

        static float[] PrepareMeasurement(float[] samples, int sampleRate, bool trimSilence) {
            int start = 0;
            int end = samples.Length;
            if (trimSilence && TryFindActiveRange(samples, sampleRate, out int activeStart, out int activeEnd)) {
                start = activeStart;
                end = activeEnd;
            }
            int length = Math.Max(1, end - start);
            var segment = new float[length];
            Array.Copy(samples, start, segment, 0, Math.Min(length, samples.Length - start));
            return PadToBlock(segment, sampleRate);
        }

        static double CalculateGainDb(
            float[] samples,
            int sampleRate,
            double strength,
            double targetLufs,
            double peakLimitDb,
            bool trimSilence) {
            strength = Math.Clamp(strength, 0, 100);
            var measurement = PrepareMeasurement(samples, sampleRate, trimSilence);
            double inputLufs = MeasureIntegratedLoudness(measurement, sampleRate);
            double gainDb = double.IsFinite(inputLufs)
                ? (targetLufs - inputLufs) * strength / 100.0
                : 0.0;

            double peak = Peak(samples);
            if (peak > 1e-12) {
                double peakGainDb = peakLimitDb - 20.0 * Math.Log10(peak);
                gainDb = Math.Min(gainDb, peakGainDb);
            }
            return double.IsFinite(gainDb) ? gainDb : 0.0;
        }

        static bool TryFindActiveRange(float[] samples, int sampleRate, out int start, out int end) {
            start = 0;
            end = samples.Length;
            int frameSamples = Math.Max(1, (int)(sampleRate * 0.020));
            int hopSamples = Math.Max(1, (int)(sampleRate * 0.010));
            if (samples.Length < frameSamples) {
                return Peak(samples) > Math.Pow(10.0, SilenceThresholdDb / 20.0);
            }

            int firstFrame = -1;
            int lastFrame = -1;
            int frameIndex = 0;
            for (int offset = 0; offset + frameSamples <= samples.Length; offset += hopSamples, frameIndex++) {
                double sum = 0;
                for (int i = offset; i < offset + frameSamples; i++) {
                    sum += samples[i] * samples[i];
                }
                double rms = Math.Sqrt(sum / frameSamples);
                double rmsDb = rms > 1e-10 ? 20.0 * Math.Log10(rms) : double.NegativeInfinity;
                if (rmsDb > SilenceThresholdDb) {
                    firstFrame = firstFrame < 0 ? frameIndex : firstFrame;
                    lastFrame = frameIndex;
                }
            }
            if (firstFrame < 0) {
                return false;
            }

            int tailPaddingFrames = (int)(sampleRate * 0.100) / hopSamples;
            start = Math.Max(0, firstFrame * hopSamples);
            end = Math.Min(samples.Length,
                (lastFrame + 1 + tailPaddingFrames) * hopSamples + frameSamples);
            return end > start;
        }

        static float[] PadToBlock(float[] samples, int sampleRate) {
            int minimumLength = Math.Max(1, (int)(sampleRate * BlockSeconds));
            if (samples.Length >= minimumLength) {
                return (float[])samples.Clone();
            }
            var padded = new float[minimumLength];
            Array.Copy(samples, padded, samples.Length);
            for (int i = samples.Length; i < padded.Length; i++) {
                int source = HiFiUtauMath.ReflectIndex(i, samples.Length);
                padded[i] = samples[source];
            }
            return padded;
        }

        static double Peak(float[] samples) {
            double peak = 0;
            for (int i = 0; i < samples.Length; i++) {
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }
            return peak;
        }

        static double Mean(double[] values, List<int> indexes) {
            double sum = 0;
            for (int i = 0; i < indexes.Count; i++) {
                sum += values[indexes[i]];
            }
            return sum / indexes.Count;
        }

        static double LoudnessFromMeanSquare(double meanSquare) {
            return meanSquare > 0
                ? -0.691 + 10.0 * Math.Log10(meanSquare)
                : double.NegativeInfinity;
        }

        readonly struct Biquad {
            readonly double b0;
            readonly double b1;
            readonly double b2;
            readonly double a1;
            readonly double a2;

            Biquad(double b0, double b1, double b2, double a0, double a1, double a2) {
                this.b0 = b0 / a0;
                this.b1 = b1 / a0;
                this.b2 = b2 / a0;
                this.a1 = a1 / a0;
                this.a2 = a2 / a0;
            }

            public static Biquad HighShelf(double gainDb, double q, double frequency, int sampleRate) {
                double a = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * frequency / sampleRate;
                double alpha = Math.Sin(w0) / (2.0 * q);
                double cos = Math.Cos(w0);
                double sqrtA = Math.Sqrt(a);
                return new Biquad(
                    a * ((a + 1) + (a - 1) * cos + 2 * sqrtA * alpha),
                    -2 * a * ((a - 1) + (a + 1) * cos),
                    a * ((a + 1) + (a - 1) * cos - 2 * sqrtA * alpha),
                    (a + 1) - (a - 1) * cos + 2 * sqrtA * alpha,
                    2 * ((a - 1) - (a + 1) * cos),
                    (a + 1) - (a - 1) * cos - 2 * sqrtA * alpha);
            }

            public static Biquad HighPass(double q, double frequency, int sampleRate) {
                double w0 = 2.0 * Math.PI * frequency / sampleRate;
                double alpha = Math.Sin(w0) / (2.0 * q);
                double cos = Math.Cos(w0);
                return new Biquad(
                    (1 + cos) / 2,
                    -(1 + cos),
                    (1 + cos) / 2,
                    1 + alpha,
                    -2 * cos,
                    1 - alpha);
            }

            public double[] Filter(float[] input) {
                var asDouble = new double[input.Length];
                for (int i = 0; i < input.Length; i++) {
                    asDouble[i] = input[i];
                }
                return Filter(asDouble);
            }

            public double[] Filter(double[] input) {
                var output = new double[input.Length];
                double x1 = 0;
                double x2 = 0;
                double y1 = 0;
                double y2 = 0;
                for (int i = 0; i < input.Length; i++) {
                    double x0 = input[i];
                    double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    output[i] = y0;
                    x2 = x1;
                    x1 = x0;
                    y2 = y1;
                    y1 = y0;
                }
                return output;
            }
        }
    }
}
