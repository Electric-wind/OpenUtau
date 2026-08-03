using System;
using Xunit;

namespace OpenUtau.Core.HiFiUtau {
    public class HiFiUtauRendererTest {
        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 1.0)]
        [InlineData(2.0, 2.0)]
        public void ApplyPhoneVolumes_MatchesClassicLinearRatio(double volume, double expected) {
            var samples = new float[8];
            Array.Fill(samples, 1f);
            var phones = new[] {
                new HiFiUtauPhone {
                    ModelStartFrame = 2,
                    ModelEndFrame = 6,
                    Volume = volume,
                },
            };

            HiFiUtauRenderer.ApplyPhoneVolumes(samples, phones, 1.0);

            Assert.All(samples, sample => Assert.InRange(Math.Abs(sample - expected), 0, 1e-6));
        }

        [Fact]
        public void ApplyPhoneVolumes_CrossfadesOverlappingPhones() {
            var samples = new float[10];
            Array.Fill(samples, 1f);
            var phones = new[] {
                new HiFiUtauPhone {
                    ModelStartFrame = 0,
                    ModelEndFrame = 6,
                    Volume = 0.5,
                },
                new HiFiUtauPhone {
                    ModelStartFrame = 4,
                    ModelEndFrame = 10,
                    Volume = 1.0,
                },
            };

            HiFiUtauRenderer.ApplyPhoneVolumes(samples, phones, 1.0);

            Assert.Equal(new float[] {
                0.5f, 0.5f, 0.5f, 0.5f, 0.5f,
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
            }, samples);
        }

        [Fact]
        public void ApplyPhoneVolumes_SmoothsAdjacentPhonesWithoutOverlap() {
            const int boundary = 2205;
            var samples = new float[boundary * 2];
            Array.Fill(samples, 1f);
            var phones = new[] {
                new HiFiUtauPhone {
                    ModelStartFrame = 0,
                    ModelEndFrame = boundary,
                    Volume = 0.5,
                },
                new HiFiUtauPhone {
                    ModelStartFrame = boundary,
                    ModelEndFrame = samples.Length,
                    Volume = 1.0,
                },
            };

            HiFiUtauRenderer.ApplyPhoneVolumes(samples, phones, 1.0);

            Assert.Equal(0.5f, samples[0]);
            Assert.Equal(1.0f, samples[^1]);
            float maxStep = 0;
            for (int i = 1; i < samples.Length; i++) {
                maxStep = Math.Max(maxStep, Math.Abs(samples[i] - samples[i - 1]));
            }
            Assert.InRange(maxStep, 0, 0.001f);
        }
    }
}
