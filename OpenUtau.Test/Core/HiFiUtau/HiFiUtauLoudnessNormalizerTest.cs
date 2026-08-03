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

        [Fact]
        public void NormalizePhonesInPlace_LevelsPhonesIndependently() {
            var samples = new float[SampleRate * 2];
            var quiet = Sine(1000, 0.05, 1.0);
            var loud = Sine(1000, 0.2, 1.0);
            Array.Copy(quiet, 0, samples, 0, quiet.Length);
            Array.Copy(loud, 0, samples, quiet.Length, loud.Length);
            var phones = new[] {
                Phone(0, SampleRate, 100),
                Phone(SampleRate, SampleRate * 2, 100),
            };

            HiFiUtauLoudnessNormalizer.NormalizePhonesInPlace(
                samples, phones, SampleRate, 1.0);

            // Exclude the short boundary transition when checking each target.
            var first = new float[SampleRate * 9 / 10];
            var second = new float[SampleRate * 9 / 10];
            Array.Copy(samples, 0, first, 0, first.Length);
            Array.Copy(samples, SampleRate + SampleRate / 10, second, 0, second.Length);
            double firstLufs = HiFiUtauLoudnessNormalizer.MeasureIntegratedLoudness(first, SampleRate);
            double secondLufs = HiFiUtauLoudnessNormalizer.MeasureIntegratedLoudness(second, SampleRate);
            Assert.InRange(firstLufs, -16.001, -15.999);
            Assert.InRange(secondLufs, -16.001, -15.999);
        }

        [Fact]
        public void NormalizePhonesInPlace_PreservesDynamicsInsidePhone() {
            var samples = new float[SampleRate];
            var quiet = Sine(1000, 0.05, 0.5);
            var loud = Sine(1000, 0.2, 0.5);
            Array.Copy(quiet, 0, samples, 0, quiet.Length);
            Array.Copy(loud, 0, samples, quiet.Length, loud.Length);
            double ratioBefore = Peak(loud) / Peak(quiet);

            HiFiUtauLoudnessNormalizer.NormalizePhonesInPlace(
                samples, new[] { Phone(0, SampleRate, 100) }, SampleRate, 1.0);

            var first = new float[SampleRate / 2];
            var second = new float[SampleRate / 2];
            Array.Copy(samples, 0, first, 0, first.Length);
            Array.Copy(samples, first.Length, second, 0, second.Length);
            Assert.InRange(Math.Abs(Peak(second) / Peak(first) - ratioBefore), 0, 1e-5);
        }

        static HiFiUtauPhone Phone(int start, int end, double strength) {
            return new HiFiUtauPhone {
                ModelStartFrame = start,
                ModelEndFrame = end,
                Normalize = strength,
            };
        }

        static double Peak(float[] samples) {
            double peak = 0;
            foreach (float sample in samples) {
                peak = Math.Max(peak, Math.Abs(sample));
            }
            return peak;
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
