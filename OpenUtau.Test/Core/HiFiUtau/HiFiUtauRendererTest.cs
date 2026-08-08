using System;
using System.Linq;
using System.Numerics;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.HiFiUtau {
    public class HiFiUtauRendererTest {
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
