using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Classic {
    // Regression test for: opening a second UST breaks the phonemizer for
    // classic singers (and singer switching can break it too), only restart helps.
    //
    // Root cause: upstream #2299 added SingerManager.ReleaseSingersNotInUse ->
    // ClassicSinger.FreeMemory(), which clears otoMap/subbanks but left
    // loaded=true, so the next EnsureLoaded() was a no-op and the shared
    // singer instance stayed permanently empty.
    public class ClassicSingerFreeMemoryTest {
        ClassicSinger LoadSinger() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var file = Path.Join(dir, "Files", "ja_cv", "character.txt");
            VoicebankLoader.IsTest = true;
            var voicebank = new Voicebank() { File = file, BasePath = dir };
            VoicebankLoader.LoadVoicebank(voicebank);
            var singer = new ClassicSinger(voicebank);
            singer.EnsureLoaded();
            return singer;
        }

        [Fact]
        public void SingerReloadsAfterFreeMemory() {
            var singer = LoadSinger();
            Assert.True(singer.Loaded);
            Assert.True(singer.TryGetOto("あ", out _));

            // Simulate ReleaseSingersNotInUse() when a second UST is opened.
            singer.FreeMemory();
            Assert.False(singer.Loaded);

            // Simulate UTrack.Validate() on the new project's track.
            var project = new UProject();
            Ustx.AddDefaultExpressions(project);
            var track = project.tracks[0];
            track.Singer = singer;
            track.Validate(new ValidateOptions(), project);

            // The freed singer must reload instead of staying permanently empty.
            Assert.True(singer.Loaded);
            Assert.True(singer.TryGetOto("あ", out _));
            Assert.NotNull(track.VoiceColorExp);
        }

        [Fact]
        public void VoiceColorRebuildsOnSingerSwitch() {
            var singer = LoadSinger();
            var project = new UProject();
            Ustx.AddDefaultExpressions(project);
            var track = project.tracks[0];
            track.Singer = singer;
            track.Validate(new ValidateOptions(), project);
            Assert.NotNull(track.VoiceColorExp);

            // Simulate TrackHeaderViewModel.ApplySingerToTrack: switching the
            // singer clears the voice color descriptors, Validate must rebuild.
            track.Singer = LoadSinger();
            Assert.Null(track.VoiceColorExp);
            track.Validate(new ValidateOptions(), project);
            Assert.NotNull(track.VoiceColorExp);
            Assert.True(track.Singer.TryGetOto("あ", out _));
        }
    }
}
