using System;
using System.Collections.Generic;

namespace OpenUtau.Core.HiFiUtau {
    readonly struct HiFiUtauGainSegment {
        public HiFiUtauGainSegment(int start, int end, float gain, int fadeInSamples, int fadeOutSamples) {
            Start = start;
            End = end;
            Gain = gain;
            FadeInSamples = Math.Max(0, fadeInSamples);
            FadeOutSamples = Math.Max(0, fadeOutSamples);
        }

        public readonly int Start;
        public readonly int End;
        public readonly float Gain;
        public readonly int FadeInSamples;
        public readonly int FadeOutSamples;

        public static HiFiUtauGainSegment FromPhone(
            HiFiUtauPhone phone,
            int start,
            int end,
            float gain,
            int sampleRate) {
            double fadeInMs = 5;
            double fadeOutMs = 35;
            if (phone.Envelope != null && phone.Envelope.Length >= 5) {
                // Keep a minimum ramp even for manually collapsed envelopes.
                fadeInMs = Math.Max(5, phone.Envelope[1].X - phone.Envelope[0].X);
                fadeOutMs = Math.Max(5, phone.Envelope[4].X - phone.Envelope[3].X);
            }
            return new HiFiUtauGainSegment(
                start,
                end,
                gain,
                (int)Math.Round(fadeInMs * sampleRate / 1000.0),
                (int)Math.Round(fadeOutMs * sampleRate / 1000.0));
        }
    }

    static class HiFiUtauGainSmoother {
        public static void Apply(float[] samples, IReadOnlyList<HiFiUtauGainSegment> segments) {
            if (samples == null || samples.Length == 0 || segments == null || segments.Count == 0) {
                return;
            }

            var gains = BuildCurve(samples.Length, segments);
            for (int i = 0; i < samples.Length; i++) {
                samples[i] *= gains[i];
            }
        }

        internal static float[] BuildCurve(int length, IReadOnlyList<HiFiUtauGainSegment> segments) {
            if (length <= 0) {
                return Array.Empty<float>();
            }
            var gains = new float[length];
            if (segments == null || segments.Count == 0) {
                Array.Fill(gains, 1f);
                return gains;
            }

            float previousGain = Math.Max(0, segments[0].Gain);
            Array.Fill(gains, previousGain);
            int previousStart = Math.Clamp(segments[0].Start, 0, length);
            int previousEnd = Math.Clamp(segments[0].End, previousStart, length);
            int previousFadeOut = segments[0].FadeOutSamples;

            for (int i = 1; i < segments.Count; i++) {
                var segment = segments[i];
                int start = Math.Clamp(segment.Start, 0, length);
                int end = Math.Clamp(segment.End, start, length);
                if (end <= start) {
                    continue;
                }

                float gain = Math.Max(0, segment.Gain);
                int transitionStart;
                int transitionEnd;
                if (start < previousEnd) {
                    transitionStart = start;
                    transitionEnd = Math.Min(previousEnd, end);
                } else {
                    transitionStart = Math.Max(previousStart, previousEnd - previousFadeOut);
                    transitionEnd = Math.Min(end, start + segment.FadeInSamples);
                }

                transitionStart = Math.Clamp(transitionStart, 0, length);
                transitionEnd = Math.Clamp(transitionEnd, transitionStart, length);
                if (transitionEnd <= transitionStart) {
                    transitionStart = Math.Max(previousStart, Math.Min(previousEnd, start) - 1);
                    transitionEnd = Math.Min(end, Math.Max(start + 1, transitionStart + 2));
                }

                float fromGain = gains[transitionStart];
                int transitionSamples = transitionEnd - transitionStart;
                for (int j = transitionStart; j < transitionEnd; j++) {
                    float alpha = transitionSamples <= 1
                        ? 1f
                        : (j - transitionStart) / (float)(transitionSamples - 1);
                    gains[j] = fromGain + (gain - fromGain) * alpha;
                }
                if (end > transitionEnd) {
                    Array.Fill(gains, gain, transitionEnd, end - transitionEnd);
                }

                if (end >= previousEnd) {
                    previousStart = start;
                    previousEnd = end;
                    previousFadeOut = segment.FadeOutSamples;
                    previousGain = gain;
                }
            }

            if (previousEnd < length) {
                Array.Fill(gains, previousGain, previousEnd, length - previousEnd);
            }
            return gains;
        }
    }
}
