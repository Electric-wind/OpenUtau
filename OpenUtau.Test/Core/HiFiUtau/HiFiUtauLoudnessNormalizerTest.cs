using System;
using Xunit;

namespace OpenUtau.Core.HiFiUtau {
    public class HiFiUtauLoudnessNormalizerTest {
        [Fact]
        public void AutoGain_ProtectsUnvoicedPhonesFromBeingLeveledLikeVowels() {
            double unvoiced = HiFiUtauLoudnessNormalizer.CalculateGain(0.8, 0.05, 0, 86);
            double voiced = HiFiUtauLoudnessNormalizer.CalculateGain(0.8, 0.05, 1, 86);

            Assert.InRange(unvoiced, 0.5, 1.0);
            Assert.True(voiced > 5.0);
        }

        [Fact]
        public void AutoGain_BypassesZeroStrengthAndNearSilence() {
            Assert.Equal(1.0, HiFiUtauLoudnessNormalizer.CalculateGain(0.8, 0.1, 1, 0));
            Assert.Equal(1.0, HiFiUtauLoudnessNormalizer.CalculateGain(0, 0, 0, 100));
            Assert.Equal(1.0, HiFiUtauLoudnessNormalizer.CalculateGain(0.0005, 0.0001, 1, 100));
        }

        [Fact]
        public void VoicedRatio_UsesOnlyTheSelectedSourceFrames() {
            double[] f0 = { 220, 220, 0, 0, 220, 220 };

            Assert.Equal(0, HiFiUtauLoudnessNormalizer.MeasureVoicedRatio(f0, 2, 4, 60));
            Assert.Equal(0.5, HiFiUtauLoudnessNormalizer.MeasureVoicedRatio(f0, 2, 6, 60));
            Assert.Equal(1, HiFiUtauLoudnessNormalizer.MeasureVoicedRatio(f0, -2, 2, 60));
            Assert.Equal(0, HiFiUtauLoudnessNormalizer.MeasureVoicedRatio(f0, 10, 20, 60));
        }

        [Fact]
        public void MelGain_PreservesInternalDynamics() {
            var mel = new float[2, 3] {
                { -2f, -1f, 0f },
                { -3f, -2f, -1f },
            };
            var before = (float[,])mel.Clone();

            HiFiUtauLoudnessNormalizer.ApplyMelGain(mel, 2.0);

            for (int b = 0; b < mel.GetLength(0); b++) {
                for (int t = 0; t < mel.GetLength(1); t++) {
                    Assert.InRange(Math.Exp(mel[b, t] - before[b, t]), 1.999999, 2.000001);
                }
            }
        }
    }
}
