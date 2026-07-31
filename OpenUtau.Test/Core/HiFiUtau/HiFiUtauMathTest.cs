using System;
using Xunit;

namespace OpenUtau.Core.HiFiUtau {
    public class HiFiUtauMathTest {
        [Fact]
        public void ReflectPadVowel_PreservesBinsAndMatchesNumpyReflect() {
            var mel = new float[2, 5] {
                { 0, 1, 2, 3, 4 },
                { 10, 11, 12, 13, 14 },
            };

            var result = HiFiUtauMath.ReflectPadVowel(mel, 2, 6);

            AssertMelEqual(new float[2, 11] {
                { 0, 1, 2, 3, 4, 3, 2, 3, 4, 3, 2 },
                { 10, 11, 12, 13, 14, 13, 12, 13, 14, 13, 12 },
            }, result);
        }

        [Fact]
        public void ReflectPadVowel_RepeatsAcrossMultiplePeriods() {
            var mel = new float[1, 4] { { 3, 4, 5, 6 } };

            var result = HiFiUtauMath.ReflectPadVowel(mel, 2, 10);

            AssertMelEqual(new float[1, 14] {
                { 3, 4, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6 },
            }, result);
        }

        [Fact]
        public void ReflectPadVowel_DoesNotPadSingleFrameVowel() {
            var mel = new float[1, 3] { { 1, 2, 3 } };

            var result = HiFiUtauMath.ReflectPadVowel(mel, 2, 5);

            Assert.Same(mel, result);
        }

        [Fact]
        public void ResamplePhoneMelLoop_MatchesFragmentPaddingAndMapping() {
            var mel = new float[2, 5] {
                { 0, 0, 0, 1, 2 },
                { 0, 0, 2, 1, 0 },
            };
            var original = (float[,])mel.Clone();

            // target vowel = 8, original vowel = 3. Fragment pads by
            // 8 - 3 + min(4, 3 / 2) = 6 frames before linear mapping.
            var padded = HiFiUtauMath.ReflectPadVowel(mel, 2, 6);
            var expected = HiFiUtauMath.ResamplePhoneMel(padded, 10, 2, 2, 9, 1.0);
            HiFiUtauMath.NormalizeLoopVowelEnergy(expected, 2);

            var actual = HiFiUtauMath.ResamplePhoneMelLoop(mel, 10, 2, 2, 3, 1.0);
            var normal = HiFiUtauMath.ResamplePhoneMel(mel, 10, 2, 2, 3, 1.0);

            AssertMelEqual(expected, actual, 1e-6f);
            AssertMelEqual(original, mel);
            Assert.True(AnyDifference(actual, normal, 1e-3f));
        }

        [Fact]
        public void ResamplePhoneMelLoop_DoesNotPadAtOnePointFiveThreshold() {
            var mel = new float[2, 6] {
                { 0, 0, 0, 1, 2, 3 },
                { 0, 0, 3, 2, 1, 0 },
            };
            var expected = HiFiUtauMath.ResamplePhoneMel(mel, 8, 2, 2, 4, 1.0);
            HiFiUtauMath.NormalizeLoopVowelEnergy(expected, 2);

            // target vowel = 6, exactly 1.5 times the four-frame source vowel.
            var actual = HiFiUtauMath.ResamplePhoneMelLoop(mel, 8, 2, 2, 4, 1.0);

            AssertMelEqual(expected, actual, 1e-6f);
        }

        [Fact]
        public void NormalizeLoopVowelEnergy_FollowsFirstToLastTrend() {
            var mel = new float[1, 4] {
                { 100, 0, (float)Math.Log(4), (float)Math.Log(2) },
            };

            HiFiUtauMath.NormalizeLoopVowelEnergy(mel, 1);

            Assert.Equal(100, mel[0, 0]);
            Assert.InRange(Math.Abs(mel[0, 1] - 0), 0, 1e-6f);
            Assert.InRange(Math.Abs(mel[0, 2] - (float)Math.Log(1.5)), 0, 1e-6f);
            Assert.InRange(Math.Abs(mel[0, 3] - (float)Math.Log(2)), 0, 1e-6f);
        }

        [Fact]
        public void PadBlankLeftFadeIn_MatchesFragmentFormula() {
            var mel = new float[2, 1] {
                { (float)Math.Log(0.8) },
                { (float)Math.Log(0.2) },
            };

            var result = HiFiUtauMath.PadBlankLeftFadeIn(mel, 3);

            for (int t = 0; t < 3; t++) {
                double alpha = (t + 1.0) / 4.0;
                Assert.InRange(Math.Abs(result[0, t] - Math.Log(0.8 * alpha + 1e-10)), 0, 1e-6);
                Assert.InRange(Math.Abs(result[1, t] - Math.Log(0.2 * alpha + 1e-10)), 0, 1e-6);
            }
            AssertMelEqual(mel, HiFiUtauMath.SliceMel(result, 3, 4));
        }

        [Fact]
        public void ResamplePhoneMel_DefaultModeIsUnchanged() {
            var mel = new float[1, 5] { { 0, 10, 20, 30, 40 } };

            var result = HiFiUtauMath.ResamplePhoneMel(mel, 5, 2, 2, 3, 1.0);

            AssertMelEqual(mel, result);
        }

        static bool AnyDifference(float[,] left, float[,] right, float tolerance) {
            for (int b = 0; b < left.GetLength(0); b++) {
                for (int t = 0; t < left.GetLength(1); t++) {
                    if (Math.Abs(left[b, t] - right[b, t]) > tolerance) {
                        return true;
                    }
                }
            }
            return false;
        }

        static void AssertMelEqual(float[,] expected, float[,] actual, float tolerance = 0) {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));
            for (int b = 0; b < expected.GetLength(0); b++) {
                for (int t = 0; t < expected.GetLength(1); t++) {
                    Assert.InRange(Math.Abs(expected[b, t] - actual[b, t]), 0, tolerance);
                }
            }
        }
    }
}
