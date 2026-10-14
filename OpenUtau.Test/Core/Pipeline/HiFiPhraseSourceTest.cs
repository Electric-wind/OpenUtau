using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.HiFiUtau;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Pipeline {
    public class HiFiPhraseSourceTest {
        sealed class FixtureSinger : USinger {
            readonly UOto primary;
            readonly UOto secondary;
            public FixtureSinger(UOto primary, UOto secondary) {
                this.primary = primary;
                this.secondary = secondary;
                found = loaded = true;
            }
            public override string Id => "hifi-snapshot-fixture";
            public override IList<USubbank> Subbanks => secondary.Subbanks;
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = phoneme == "A" ? primary : null;
                return oto != null;
            }
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = color == "B" ? secondary : null;
                return oto != null;
            }
        }

        static (UProject project, UTrack track, UVoicePart part) Fixture() {
            var fixture = PhraseSourceHashTest.BuildFixture(new HiFiUtauRenderer());
            fixture.track.RendererSettings.renderer = Renderers.HIFIUTAU;
            foreach (var note in fixture.part.notes) {
                note.vibrato.length = 0;
            }
            fixture.part.curves.Clear();
            return fixture;
        }

        static float At(RenderPhrase phrase, float[] curve, int partPosition, int tick) =>
            curve[(int)Math.Ceiling((tick - (phrase.position - partPosition - phrase.leading)) / (double)UCurve.interval)];

        [Fact]
        public void GlobalOffsetsAndNoteOverridesAreCapturedBeforeWorkerBuild() {
            var (project, track, part) = Fixture();
            var descriptor = project.expressions[Format.Ustx.TENC];
            part.hifiUtauGlobalValues = new Dictionary<string, float> { [descriptor.abbr] = 70 };
            var note = part.notes.First();
            var expression = new UExpression(descriptor) { index = 0, value = 35 };
            note.phonemeExpressions.Add(expression);
            var curve = new UCurve(descriptor);
            curve.xs.AddRange(new[] { 100, 200 });
            curve.ys.AddRange(new[] { 10, 10 });
            part.curves.Add(curve);
            var source = RenderPhrase.CreateSource(project, track, part, 1);

            // Change every live input after capture, before the background work runs.
            part.hifiUtauGlobalValues[descriptor.abbr] = 5;
            expression.value = 99;
            curve.ys[0] = curve.ys[1] = 99;
            part.notes.Clear();
            part.phonemes.Clear();
            var phrase = source.BuildPhrases().First();

            Assert.Equal(55f, At(phrase, phrase.tension, part.position, 0));
            Assert.Equal(30f, At(phrase, phrase.tension, part.position, 150));
            Assert.Equal(70f, At(phrase, phrase.tension, part.position, 600));
            Assert.True(phrase.tensionCurveActive[(150 + phrase.leading) / UCurve.interval]);
            Assert.False(phrase.tensionCurveActive[(600 + phrase.leading) / UCurve.interval]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GlobalOrNoteXsyCapturesSecondaryOtoAndIndependentTiming(bool global) {
            string directory = Path.Combine(Path.GetTempPath(), "openutau-hifi-source-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try {
                // No model or valid PCM is required: source preparation only checks existence.
                File.WriteAllBytes(Path.Combine(directory, "secondary.wav"), Array.Empty<byte>());
                var (project, track, part) = Fixture();
                var primary = UOto.OfDummy("A");
                var secondary = new UOto(new Oto {
                    Alias = "AB", Phonetic = "A", Wav = "secondary.wav", Preutter = 80, Overlap = 20,
                }, new UOtoSet(new OtoSet { File = Path.Combine(directory, "oto.ini"), Name = "fixture" }, directory),
                    new[] { new USubbank(new Subbank { Color = "B" }) });
                track.Singer = new FixtureSinger(primary, secondary);
                var color = new UExpressionDescriptor("secondary color", Format.Ustx.CLRY, 0, 1, 0) {
                    options = new[] { "B" },
                };
                project.RegisterExpression(color);
                track.VoiceColor2Exp = color;
                if (global) {
                    part.hifiUtauGlobalValues = new Dictionary<string, float> { [Format.Ustx.XSY] = 40 };
                } else {
                    part.notes.First().phonemeExpressions.Add(new UExpression(project.expressions[Format.Ustx.XSY]) {
                        index = 0, value = 40,
                    });
                }
                foreach (var phone in part.phonemes) {
                    phone.Validate(new ValidateOptions(), project, track, part, phone.Parent);
                }
                var original = part.phonemes.First();
                double preutter = original.preutter;
                var envelope = original.envelope.data.ToArray();
                Assert.Equal(40f, global ? part.hifiUtauGlobalValues[Format.Ustx.XSY]
                    : original.GetExpression(project, track, Format.Ustx.XSY).Item1);
                var source = RenderPhrase.CreateSource(project, track, part, 2);
                if (global) {
                    part.hifiUtauGlobalValues.Clear();
                } else {
                    part.notes.First().phonemeExpressions.Clear();
                }
                track.VoiceColor2Exp.options[0] = "unavailable";
                var phrase = source.BuildPhrases().First();

                Assert.Same(secondary, phrase.phones.First().oto2);
                Assert.NotNull(phrase.secondaryPhones);
                Assert.Same(secondary, phrase.secondaryPhones.First().oto);
                Assert.True(phrase.secondaryPhones.First().leadingMs > phrase.phones.First().leadingMs);
                Assert.Equal(phrase.secondaryPhones.First().leadingMs, phrase.leadingMs);
                Assert.Equal(40f, At(phrase, phrase.xsy, part.position, 0));
                Assert.Equal(preutter, original.preutter);
                Assert.Equal(envelope, original.envelope.data.ToArray());
            } finally {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void GraphDrivenHiFiCurveOverridesCapturedGlobalsAndNoteValues() {
            var (project, track, part) = Fixture();
            part.hifiUtauGlobalValues = new Dictionary<string, float> { [Format.Ustx.TENC] = 80 };
            part.notes.First().phonemeExpressions.Add(new UExpression(project.expressions[Format.Ustx.TENC]) {
                index = 0, value = 5,
            });
            var constant = new ExpressionGraph.UGraphNode { id = 1, type = ExpressionGraph.GraphNodeTypes.Constant };
            constant.Set("value", "25");
            var output = new ExpressionGraph.UGraphNode { id = 2, type = ExpressionGraph.GraphNodeTypes.CurveOutput };
            output.Set("abbr", Format.Ustx.TENC);
            project.expressionGraphs = new List<ExpressionGraph.UExpressionGraph> {
                new ExpressionGraph.UExpressionGraph {
                    id = "hifi-graph", renderer = Renderers.HIFIUTAU,
                    nodes = new List<ExpressionGraph.UGraphNode> { constant, output },
                    links = new List<ExpressionGraph.UGraphLink> {
                        new ExpressionGraph.UGraphLink { from = 1, to = 2, toPort = ExpressionGraph.GraphNodeTypes.Value },
                    },
                },
            };
            project.defaultExpressionGraphs = new Dictionary<string, string> { [Renderers.HIFIUTAU] = "hifi-graph" };
            var source = RenderPhrase.CreateSource(project, track, part, 3);
            Assert.NotNull(source.ExpressionGraph);
            var phrase = source.BuildPhrases().First();
            Assert.All(phrase.tension, value => Assert.Equal(25f, value));
            Assert.All(phrase.tensionCurveActive, value => Assert.True(value));
            Assert.Equal(phrase.tension, phrase.drivenCurves[Format.Ustx.TENC]);
        }

        [Fact]
        public void MaskedCurvesAndHiFiGlobalsCoexistThroughSerializationAndDeepClone() {
            var part = new UVoicePart {
                hifiUtauGlobalValues = new Dictionary<string, float> { [Format.Ustx.XSY] = 35 },
            };
            var masked = new UMaskedCurve("rpit");
            masked.SetValues(new[] { (960, 6000.25f), (965, 6010.5f) });
            part.maskedCurves.Add(masked);
            var yaml = Yaml.DefaultSerializer.Serialize(part);
            var read = Yaml.DefaultDeserializer.Deserialize<UVoicePart>(yaml);
            Assert.Equal(35f, read.hifiUtauGlobalValues[Format.Ustx.XSY]);
            Assert.Equal(new[] { 6000.25f, 6010.5f }, Assert.Single(read.maskedCurves).runs.Single().ys);

            var clone = Assert.IsType<UVoicePart>(part.Clone());
            clone.hifiUtauGlobalValues[Format.Ustx.XSY] = 99;
            clone.maskedCurves.Single().runs.Single().ys[0] = 1;
            Assert.Equal(35f, part.hifiUtauGlobalValues[Format.Ustx.XSY]);
            Assert.Equal(6000.25f, part.maskedCurves.Single().runs.Single().ys[0]);
        }
    }
}
