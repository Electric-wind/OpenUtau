using System;
using System.Collections.Generic;

namespace OpenUtau.Core.HiFiUtau {
    /// <summary>
    /// Computes Hifisampler-compatible loudness gains from source audio and
    /// applies them to mel frames. Gains are interpolated around phone
    /// boundaries so normalization cannot introduce a frame-sized step.
    /// </summary>
    static class HiFiUtauMelLoudnessNormalizer {
        public const double TargetLufs = -16.0;
        public const double PeakLimitDb = -1.0;

        const double BlockSeconds = 0.400;
        const double AbsoluteGateLufs = -70.0;
        const double SilenceThresholdDb = -52.0;
        const int BoundaryRadiusFrames = 2;

        public static double CalculateGainDb(
            float[] samples,
            int start,
            int length,
            int sampleRate,
            double strength,
            double targetLufs = TargetLufs,
            double peakLimitDb = PeakLimitDb,
            bool trimSilence = true) {
            if (samples == null || samples.Length == 0 || sampleRate <= 0 || length <= 0) {
                return 0;
            }
            start = Math.Clamp(start, 0, samples.Length);
            length = Math.Clamp(length, 0, samples.Length - start);
            if (length == 0) {
                return 0;
            }

            strength = Math.Clamp(strength, 0, 100);
            var measurement = PrepareMeasurement(samples, start, length, sampleRate, trimSilence);
            double inputLufs = MeasureIntegratedLoudness(measurement, sampleRate);
            double gainDb = double.IsFinite(inputLufs)
                ? (targetLufs - inputLufs) * strength / 100.0
                : 0.0;

            double peak = Peak(samples, start, length);
            if (peak > 1e-12) {
                gainDb = Math.Min(gainDb, peakLimitDb - 20.0 * Math.Log10(peak));
            }
            return double.IsFinite(gainDb) ? gainDb : 0.0;
        }

        public static void ApplyPhoneMelGains(HiFiUtauPhone[] phones) {
            if (phones == null || phones.Length == 0) {
                return;
            }

            var valid = new List<int>();
            for (int i = 0; i < phones.Length; i++) {
                if (phones[i].Mel != null && phones[i].Mel.GetLength(1) > 0 &&
                    double.IsFinite(phones[i].LoudnessGainDb)) {
                    valid.Add(i);
                }
            }
            if (valid.Count == 0) {
                return;
            }

            for (int n = 0; n < valid.Count; n++) {
                int phoneIndex = valid[n];
                var phone = phones[phoneIndex];
                var mel = phone.Mel!;
                int frames = mel.GetLength(1);
                var transitions = new List<GainTransition>(2);
                if (n > 0) {
                    AddTransition(transitions, phones[valid[n - 1]], phone);
                }
                if (n + 1 < valid.Count) {
                    AddTransition(transitions, phone, phones[valid[n + 1]]);
                }

                for (int frame = 0; frame < frames; frame++) {
                    double modelPosition = phone.ModelStartFrame +
                        (frame + 0.5) * phone.ModelFrames / (double)frames;
                    double gainDb = phone.LoudnessGainDb;
                    for (int t = 0; t < transitions.Count; t++) {
                        var transition = transitions[t];
                        if (modelPosition >= transition.Start && modelPosition <= transition.End) {
                            double span = transition.End - transition.Start;
                            double alpha = span > 0
                                ? (modelPosition - transition.Start) / span
                                : 1.0;
                            alpha = alpha * alpha * (3.0 - 2.0 * alpha);
                            gainDb = transition.FromDb + (transition.ToDb - transition.FromDb) * alpha;
                            break;
                        }
                    }
                    float logGain = (float)(gainDb * Math.Log(10.0) / 20.0);
                    for (int bin = 0; bin < mel.GetLength(0); bin++) {
                        mel[bin, frame] += logGain;
                    }
                }
            }
        }

        static void AddTransition(List<GainTransition> transitions, HiFiUtauPhone from, HiFiUtauPhone to) {
            if (from.ModelEndFrame <= from.ModelStartFrame || to.ModelEndFrame <= to.ModelStartFrame) {
                return;
            }
            int overlapStart = Math.Max(from.ModelStartFrame, to.ModelStartFrame);
            int overlapEnd = Math.Min(from.ModelEndFrame, to.ModelEndFrame);
            if (overlapEnd > overlapStart) {
                transitions.Add(new GainTransition(overlapStart, overlapEnd,
                    from.LoudnessGainDb, to.LoudnessGainDb));
                return;
            }

            int boundary = to.ModelStartFrame;
            int start = Math.Max(from.ModelStartFrame, boundary - BoundaryRadiusFrames);
            int end = Math.Min(to.ModelEndFrame, boundary + BoundaryRadiusFrames);
            if (end > start) {
                transitions.Add(new GainTransition(start, end,
                    from.LoudnessGainDb, to.LoudnessGainDb));
            }
        }

        static float[] PrepareMeasurement(
            float[] samples, int start, int length, int sampleRate, bool trimSilence) {
            int end = start + length;
            if (trimSilence && TryFindActiveRange(
                samples, start, length, sampleRate, out int activeStart, out int activeEnd)) {
                start = activeStart;
                end = activeEnd;
            }
            length = Math.Max(1, end - start);
            var segment = new float[length];
            Array.Copy(samples, start, segment, 0, Math.Min(length, samples.Length - start));
            return PadToBlock(segment, sampleRate);
        }

        static double MeasureIntegratedLoudness(float[] samples, int sampleRate) {
            var input = PadToBlock(samples, sampleRate);
            var shelf = Biquad.HighShelf(4.0, 1.0 / Math.Sqrt(2.0), 1500.0, sampleRate);
            var highPass = Biquad.HighPass(0.5, 38.0, sampleRate);
            var filtered = highPass.Filter(shelf.Filter(input));
            double duration = filtered.Length / (double)sampleRate;
            int blockCount = Math.Max(1, (int)Math.Round(
                (duration - BlockSeconds) / (BlockSeconds * 0.25),
                MidpointRounding.ToEven) + 1);
            int blockSamples = Math.Max(1, (int)(BlockSeconds * sampleRate));
            var meanSquares = new double[blockCount];
            var blockLoudness = new double[blockCount];
            for (int block = 0; block < blockCount; block++) {
                int start = (int)(BlockSeconds * block * 0.25 * sampleRate);
                int end = Math.Min(filtered.Length,
                    (int)(BlockSeconds * (block * 0.25 + 1.0) * sampleRate));
                double sum = 0;
                for (int i = start; i < end; i++) {
                    sum += filtered[i] * filtered[i];
                }
                meanSquares[block] = sum / blockSamples;
                blockLoudness[block] = LoudnessFromMeanSquare(meanSquares[block]);
            }

            var absolute = new List<int>();
            for (int i = 0; i < blockCount; i++) {
                if (blockLoudness[i] >= AbsoluteGateLufs) {
                    absolute.Add(i);
                }
            }
            if (absolute.Count == 0) {
                return double.NegativeInfinity;
            }
            double relativeGate = LoudnessFromMeanSquare(Mean(meanSquares, absolute)) - 10.0;
            var relative = new List<int>();
            for (int i = 0; i < blockCount; i++) {
                if (blockLoudness[i] > AbsoluteGateLufs && blockLoudness[i] > relativeGate) {
                    relative.Add(i);
                }
            }
            return relative.Count == 0
                ? double.NegativeInfinity
                : LoudnessFromMeanSquare(Mean(meanSquares, relative));
        }

        static bool TryFindActiveRange(
            float[] samples, int rangeStart, int rangeLength, int sampleRate,
            out int start, out int end) {
            start = rangeStart;
            end = rangeStart + rangeLength;
            int frameSamples = Math.Max(1, (int)(sampleRate * 0.020));
            int hopSamples = Math.Max(1, (int)(sampleRate * 0.010));
            if (rangeLength < frameSamples) {
                return Peak(samples, rangeStart, rangeLength) >
                    Math.Pow(10.0, SilenceThresholdDb / 20.0);
            }
            int rangeEnd = rangeStart + rangeLength;
            int first = -1;
            int last = -1;
            for (int offset = rangeStart; offset + frameSamples <= rangeEnd; offset += hopSamples) {
                double sum = 0;
                for (int i = offset; i < offset + frameSamples; i++) {
                    sum += samples[i] * samples[i];
                }
                double rms = Math.Sqrt(sum / frameSamples);
                if (20.0 * Math.Log10(Math.Max(rms, 1e-10)) > SilenceThresholdDb) {
                    first = first < 0 ? offset : first;
                    last = offset;
                }
            }
            if (first < 0) {
                return false;
            }
            int padding = (int)(sampleRate * 0.100) / hopSamples;
            start = first;
            end = Math.Min(rangeEnd, last + (1 + padding) * hopSamples + frameSamples);
            return end > start;
        }

        static float[] PadToBlock(float[] samples, int sampleRate) {
            int minimumLength = Math.Max(1, (int)(sampleRate * BlockSeconds));
            if (samples.Length >= minimumLength) {
                return samples;
            }
            var padded = new float[minimumLength];
            Array.Copy(samples, padded, samples.Length);
            for (int i = samples.Length; i < padded.Length; i++) {
                padded[i] = samples[HiFiUtauMath.ReflectIndex(i, samples.Length)];
            }
            return padded;
        }

        static double Peak(float[] samples, int start, int length) {
            double peak = 0;
            for (int i = start; i < start + length; i++) {
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

        readonly struct GainTransition {
            public readonly int Start;
            public readonly int End;
            public readonly double FromDb;
            public readonly double ToDb;

            public GainTransition(int start, int end, double fromDb, double toDb) {
                Start = start;
                End = end;
                FromDb = fromDb;
                ToDb = toDb;
            }
        }

        readonly struct Biquad {
            readonly double b0, b1, b2, a1, a2;

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
                    (1 + cos) / 2, -(1 + cos), (1 + cos) / 2,
                    1 + alpha, -2 * cos, 1 - alpha);
            }

            public double[] Filter(float[] input) {
                var values = new double[input.Length];
                for (int i = 0; i < input.Length; i++) {
                    values[i] = input[i];
                }
                return Filter(values);
            }

            public double[] Filter(double[] input) {
                var output = new double[input.Length];
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                for (int i = 0; i < input.Length; i++) {
                    double x0 = input[i];
                    double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    output[i] = y0;
                    x2 = x1; x1 = x0; y2 = y1; y1 = y0;
                }
                return output;
            }
        }
    }
}
