using System;
using System.Collections.Generic;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.HiFiUtau {
    /// <summary>
    /// Worldline-R1.1 source auto gain, applied before feature splicing so the model
    /// blends normalized phones, including the two cross-synthesis voices.
    /// </summary>
    sealed class HiFiUtauLoudnessNormalizer {
        const int AnalysisSampleRate = 44100;
        const int AnalysisHop = 220; // Worldline-R1.1's approximately 5 ms frames.
        const int AnalysisFftSize = 2048;
        const double SilencePeak = 1e-3;

        // Per render: reuse analysis for aliases in the same recording without retaining
        // voicebank audio indefinitely or serving stale F0 after a recording is edited.
        readonly Dictionary<string, SourceAnalysis> sources = new Dictionary<string, SourceAnalysis>();

        public void NormalizeMelInPlace(float[,] mel, float[] source, HiFiUtauPhone phone,
                int segmentStart, int segmentEnd, int sampleRate) {
            if (mel.Length == 0 || source.Length == 0 || sampleRate <= 0 || phone.Direct ||
                !double.IsFinite(phone.Normalize) || phone.Normalize <= 0) {
                return;
            }
            segmentStart = Math.Clamp(segmentStart, 0, source.Length);
            segmentEnd = Math.Clamp(segmentEnd, segmentStart, source.Length);
            if (segmentEnd <= segmentStart) {
                return;
            }
            if (!sources.TryGetValue(phone.AudioPath, out var analysis)) {
                // Worldline.SourceF0 reads .frq timing at 44.1 kHz, independently of
                // the HiFiUTAU model's sample rate. Use the same analysis as R1.1.
                var audio = sampleRate == AnalysisSampleRate ? source
                    : HiFiUtauMath.ReadMonoSamples(phone.AudioPath, AnalysisSampleRate);
                var config = Worldline.InitAnalysisConfig(AnalysisSampleRate, AnalysisHop, AnalysisFftSize);
                double peak = Peak(audio, 0, audio.Length);
                var f0 = peak < SilencePeak ? Array.Empty<double>()
                    : Worldline.SourceF0(phone.AudioPath, audio, config);
                analysis = new SourceAnalysis(peak, f0, config.f0_floor);
                sources.Add(phone.AudioPath, analysis);
            }
            int firstFrame = (int)Math.Floor(segmentStart * (double)AnalysisSampleRate / sampleRate / AnalysisHop);
            int endFrame = (int)Math.Ceiling(segmentEnd * (double)AnalysisSampleRate / sampleRate / AnalysisHop);
            double voicedRatio = MeasureVoicedRatio(analysis.F0, firstFrame, endFrame, analysis.F0Floor);
            double gain = CalculateGain(analysis.Peak, Peak(source, segmentStart, segmentEnd),
                voicedRatio, phone.Normalize);
            ApplyMelGain(mel, gain);
        }

        internal static double CalculateGain(double sourcePeak, double segmentPeak,
                double voicedRatio, double strength) {
            if (!double.IsFinite(sourcePeak) || !double.IsFinite(segmentPeak) ||
                !double.IsFinite(voicedRatio) || !double.IsFinite(strength)) {
                return 1.0;
            }
            sourcePeak = Math.Max(0, sourcePeak);
            segmentPeak = Math.Max(0, segmentPeak);
            voicedRatio = Math.Clamp(voicedRatio, 0, 1);
            strength = Math.Clamp(strength, 0, 100);
            double weight = 1.0 / (1.0 + Math.Exp(5.0 - 10.0 * voicedRatio));
            double referencePeak = segmentPeak * weight + sourcePeak * (1.0 - weight);
            // R1.1 does not amplify near-silence. P0 is a complete bypass.
            return referencePeak < SilencePeak || strength == 0
                ? 1.0 : Math.Pow(0.5 / referencePeak, strength * 0.01);
        }

        internal static double MeasureVoicedRatio(double[] f0, int start, int end, double floor) {
            start = Math.Clamp(start, 0, f0.Length);
            end = Math.Clamp(end, start, f0.Length);
            int voiced = 0;
            for (int i = start; i < end; i++) {
                if (double.IsFinite(f0[i]) && f0[i] > floor) {
                    voiced++;
                }
            }
            return end > start ? voiced / (double)(end - start) : 0;
        }

        internal static void ApplyMelGain(float[,] mel, double gain) {
            if (double.IsFinite(gain) && gain > 0 && Math.Abs(gain - 1.0) > 1e-9) {
                HiFiUtauMath.AddLogGain(mel, Math.Log(gain));
            }
        }

        static double Peak(float[] samples, int start, int end) {
            double peak = 0;
            for (int i = start; i < end; i++) {
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }
            return peak;
        }

        readonly struct SourceAnalysis {
            public readonly double Peak;
            public readonly double[] F0;
            public readonly double F0Floor;

            public SourceAnalysis(double peak, double[] f0, double f0Floor) {
                Peak = peak;
                F0 = f0;
                F0Floor = f0Floor;
            }
        }
    }
}
