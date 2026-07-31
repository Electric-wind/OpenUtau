using System;
using Xunit;

namespace OpenUtau.Core.HiFiUtau {
    public class HiFiUtauLoudnessNormalizerTest {
        const int SampleRate = 44100;

        [Fact]
        public void MeasureIntegratedLoudness_MatchesPyloudnormForOneKilohertz() {
            var samples = Sine(1000, 0.1, 2.0);

            double loudness = HiFiUtauLoudnessNormalizer.MeasureIntegratedLoudness(samples, SampleRate);

            Assert.InRange(loudness, -23.0460, -23.0455);
        }

        [Fact]
        public void MeasureIntegratedLoudness_MatchesPyloudnormForLowFrequency() {
            var samples = Sine(100, 0.2, 2.0);

            double loudness = HiFiUtauLoudnessNormalizer.MeasureIntegratedLoudness(samples, SampleRate);

            Assert.InRange(loudness, -18.8541, -18.8535);
        }

        [Fact]
        public void MeasureIntegratedLoudness_MatchesPyloudnormGating() {
            var samples = new float[SampleRate * 2];
            var voiced = Sine(440, 0.1, 1.0);
            Array.Copy(voiced, 0, samples, SampleRate / 2, voiced.Length);

            double loudness = HiFiUtauLoudnessNormalizer.MeasureIntegratedLoudness(samples, SampleRate);

            Assert.InRange(loudness, -24.8760, -24.8753);
        }

        [Fact]
        public void NormalizeInPlace_ReachesTargetAtFullStrength() {
            var samples = Sine(1000, 0.1, 2.0);

            double gainDb = HiFiUtauLoudnessNormalizer.NormalizeInPlace(
                samples, SampleRate, 100, trimSilence: false);
            double loudness = HiFiUtauLoudnessNormalizer.MeasureIntegratedLoudness(samples, SampleRate);

            Assert.InRange(gainDb, 7.0455, 7.0460);
            Assert.InRange(loudness, -16.0002, -15.9998);
        }

        [Fact]
        public void NormalizeInPlace_ConstrainsTransientPeak() {
            var samples = Sine(1000, 0.001, 2.0);
            samples[SampleRate] = 0.9f;

            HiFiUtauLoudnessNormalizer.NormalizeInPlace(
                samples, SampleRate, 100, trimSilence: false);

            double peak = 0;
            foreach (float sample in samples) {
                peak = Math.Max(peak, Math.Abs(sample));
            }
            double expectedPeak = Math.Pow(10.0, HiFiUtauLoudnessNormalizer.PeakLimitDb / 20.0);
            Assert.InRange(peak, expectedPeak - 1e-6, expectedPeak + 1e-6);
        }

        [Fact]
        public void NormalizeInPlace_LeavesSilenceUnchanged() {
            var samples = new float[SampleRate];

            double gainDb = HiFiUtauLoudnessNormalizer.NormalizeInPlace(samples, SampleRate, 100);

            Assert.Equal(0, gainDb);
            Assert.All(samples, sample => Assert.Equal(0, sample));
        }

        static float[] Sine(double frequency, double amplitude, double seconds) {
            var samples = new float[(int)(SampleRate * seconds)];
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / SampleRate));
            }
            return samples;
        }
    }
}
