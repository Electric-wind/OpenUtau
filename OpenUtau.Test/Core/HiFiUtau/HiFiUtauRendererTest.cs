using System;
using System.Linq;
using OpenUtau.Core.Ustx;
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
        public void SuggestedExpressions_ContainsVibraEnvelopeCurve() {
            var descriptor = new HiFiUtauRenderer()
                .GetSuggestedExpressions(null, null)
                .Single(expression => expression.abbr == "vibc");

            Assert.Equal("Vibra envelop (curve)", descriptor.name);
            Assert.Equal(UExpressionType.Curve, descriptor.type);
            Assert.Equal(-100, descriptor.min);
            Assert.Equal(100, descriptor.max);
            Assert.Equal(0, descriptor.defaultValue);
            Assert.False(descriptor.isFlag);
        }

        [Fact]
        public void ApplyVibraEnvelope_UsesCurveAtEachPitchFrame() {
            var samples = new float[] { 1f, 1f, 1f };

            AudioPostProcessor.ApplyVibraEnvelope(
                samples,
                new float[] { -100f, 0f, 100f },
                new float[] { 6000f, 6100f, 6200f },
                new double[] { 0.0, 0.01, 0.02 },
                100);

            Assert.Equal(0.2f, samples[0], 5);
            Assert.Equal(1f, samples[1], 5);
            Assert.Equal(5f, samples[2], 5);
        }

        [Fact]
        public void ApplyVibraEnvelope_DoesNotChangeFlatPitch() {
            var samples = new float[] { 0.25f, -0.5f, 0.75f };
            var expected = samples.ToArray();

            AudioPostProcessor.ApplyVibraEnvelope(
                samples,
                new float[] { 100f, 100f, 100f },
                new float[] { 6000f, 6000f, 6000f },
                new double[] { 0.0, 0.01, 0.02 },
                100);

            Assert.Equal(expected, samples);
        }
    }
}
