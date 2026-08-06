using System;
using System.Linq;
using System.Numerics;
using OpenUtau.Core.Format;
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
            var renderer = new HiFiUtauRenderer();
            var descriptor = renderer
                .GetSuggestedExpressions(null, null)
                .Single(expression => expression.abbr == "vibc");

            Assert.Equal("vibra envelop (curve)", descriptor.name);
            Assert.Equal(UExpressionType.Curve, descriptor.type);
            Assert.Equal(-100, descriptor.min);
            Assert.Equal(100, descriptor.max);
            Assert.Equal(0, descriptor.defaultValue);
            Assert.False(descriptor.isFlag);
        }

        [Fact]
        public void SuggestedExpressions_ContainsDirectOption() {
            var renderer = new HiFiUtauRenderer();
            var descriptor = renderer
                .GetSuggestedExpressions(null, null)
                .Single(expression => expression.abbr == OpenUtau.Core.Format.Ustx.DIR);

            Assert.True(renderer.SupportsExpression(descriptor));
            Assert.Equal("direct", descriptor.name);
            Assert.Equal(UExpressionType.Options, descriptor.type);
            Assert.Equal(new[] { "off", "on" }, descriptor.options);
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

        [Fact]
        public void SliceDirectSamples_UsesOtoOffsetAndCutoff() {
            var source = Enumerable.Range(0, 100).Select(value => (float)value).ToArray();
            var phone = new HiFiUtauPhone {
                OffsetMs = 10,
                CutoffMs = 20,
            };

            var result = HiFiUtauRenderer.SliceDirectSamples(source, phone, 1000);

            Assert.Equal(70, result.Length);
            Assert.Equal(10f, result[0]);
            Assert.Equal(79f, result[^1]);
        }

        [Fact]
        public void ApplyDirectPhone_ReplacesAudioAtEnvelopeStart() {
            var destination = new float[8];
            Array.Fill(destination, -1f);
            var source = new[] { 0.25f, 0.5f, 0.75f };
            var phone = new HiFiUtauPhone {
                PositionMs = 20,
                LeadingMs = 20,
                PreutterMs = 20,
                Velocity = 100,
                Volume = 1,
                Envelope = new[] {
                    new Vector2(-20, 100),
                    new Vector2(-10, 100),
                    new Vector2(0, 100),
                    new Vector2(10, 100),
                    new Vector2(20, 100),
                },
            };

            HiFiUtauRenderer.ApplyDirectPhone(destination, source, phone, 0, 1000);

            Assert.Equal(new[] { 0.25f, 0.5f, 0.75f, -1f, -1f, -1f, -1f, -1f }, destination);
        }

        [Fact]
        public void ApplyDirectPhone_UsesClassicSkipOver() {
            var destination = new float[4];
            var source = Enumerable.Range(0, 14).Select(value => (float)value).ToArray();
            var phone = new HiFiUtauPhone {
                PositionMs = 20,
                LeadingMs = 20,
                PreutterMs = 30,
                Velocity = 100,
                Volume = 1,
                Envelope = new[] {
                    new Vector2(-20, 100),
                    new Vector2(-10, 100),
                    new Vector2(0, 100),
                    new Vector2(10, 100),
                    new Vector2(20, 100),
                },
            };

            HiFiUtauRenderer.ApplyDirectPhone(destination, source, phone, 0, 1000);

            Assert.Equal(new[] { 10f, 11f, 12f, 13f }, destination);
        }

        [Fact]
        public void ApplyDirectPhone_DoesNotEraseAudioAfterFadeOut() {
            var destination = Enumerable.Repeat(10f, 5).ToArray();
            var source = Enumerable.Repeat(2f, 5).ToArray();
            var phone = new HiFiUtauPhone {
                PositionMs = 0,
                LeadingMs = 0,
                PreutterMs = 0,
                Velocity = 100,
                Volume = 1,
                Envelope = new[] {
                    new Vector2(0, 100),
                    new Vector2(1, 100),
                    new Vector2(2, 100),
                    new Vector2(3, 50),
                    new Vector2(4, 0),
                },
            };

            HiFiUtauRenderer.ApplyDirectPhone(destination, source, phone, 0, 1000);

            Assert.Equal(2f, destination[0]);
            Assert.Equal(6f, destination[3]);
            Assert.Equal(10f, destination[4]);
        }
    }
}
