using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core {
    public abstract class ExpCommand : UCommand {
        public UVoicePart Part;
        public UNote Note;
        public string Key;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = true,
            };
        public ExpCommand(UVoicePart part) {
            Part = part;
        }
    }

    public class SetNoteExpressionCommand : ExpCommand {
        static readonly HashSet<string> needsPhonemizer = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.CLR, Format.Ustx.SHFT, Format.Ustx.VEL
        };

        public readonly UProject project;
        public readonly UTrack track;
        public readonly float?[] newValue;
        public readonly float?[] oldValue;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = !needsPhonemizer.Contains(Key),
            };
        public SetNoteExpressionCommand(UProject project, UTrack track, UVoicePart part, UNote note, string abbr, float?[] values) : base(part) {
            this.project = project;
            this.track = track;
            this.Note = note;
            Key = abbr;
            newValue = values;
            oldValue = note.GetExpressionNoteHas(project, track, abbr);
        }
        public override string ToString() => $"Set note expression {Key}";
        public override void Execute() => Note.SetExpression(project, track, Key, newValue);
        public override void Unexecute() => Note.SetExpression(project, track, Key, oldValue);
    }

    public class SetNotesSameExpressionCommand : ExpCommand {
        static readonly HashSet<string> needsPhonemizer = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.CLR, Format.Ustx.SHFT, Format.Ustx.VEL
        };

        public readonly UProject project;
        public readonly UTrack track;
        public readonly UNote[] notes;
        public readonly float? newValue;
        public readonly float?[][] oldValue;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = !needsPhonemizer.Contains(Key),
            };
        public SetNotesSameExpressionCommand(UProject project, UTrack track, UVoicePart part, IEnumerable<UNote> notes, string abbr, float? value) : base(part) {
            this.project = project;
            this.track = track;
            Key = abbr;
            this.notes = notes.ToArray();
            newValue = value;
            oldValue = notes.Select(note => note.GetExpressionNoteHas(project, track, abbr)).ToArray();
        }
        public override string ToString() => $"Set note expression {Key}";
        public override void Execute() {
            for (var i = 0; i < notes.Length; i++) {
                notes[i].SetExpression(project, track, Key, new float?[] { newValue });
            }
        }
        public override void Unexecute() {
            for (var i = 0; i < notes.Length; i++) {
                notes[i].SetExpression(project, track, Key, oldValue[i]);
            }
        }
    }

    public class SetPhonemeExpressionCommand : ExpCommand {
        static readonly HashSet<string> needsPhonemizer = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.CLR, Format.Ustx.SHFT, Format.Ustx.VEL
        };

        public readonly UProject project;
        public readonly UTrack track;
        public readonly UPhoneme phoneme;
        public readonly float? newValue;
        public readonly float? oldValue;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = !needsPhonemizer.Contains(Key),
            };
        public SetPhonemeExpressionCommand(UProject project, UTrack track, UVoicePart part, UPhoneme phoneme, string abbr, float? value) : base(part) {
            this.project = project;
            this.track = track;
            this.phoneme = phoneme;
            Key = abbr;
            newValue = value;
            var oldExp = phoneme.GetExpression(project, track, abbr);
            if (oldExp.Item2) {
                oldValue = oldExp.Item1;
            } else {
                oldValue = null;
            }
        }
        public override string ToString() => $"Set phoneme expression {Key}";
        public override void Execute() {
            phoneme.SetExpression(project, track, Key, newValue);
        }
        public override void Unexecute() {
            phoneme.SetExpression(project, track, Key, oldValue);
        }
    }

    public class ResetExpressionsCommand : ExpCommand {
        List<UExpression> phonemeExpressions;
        public ResetExpressionsCommand(UVoicePart part, UNote note) : base(part) {
            Note = note;
            phonemeExpressions = note.phonemeExpressions;
        }
        public override string ToString() => "Reset expressions.";
        public override void Execute() {
            Note.phonemeExpressions = new List<UExpression>();
        }
        public override void Unexecute() {
            Note.phonemeExpressions = phonemeExpressions;
        }
    }

    public abstract class PitchExpCommand : ExpCommand {
        public PitchExpCommand(UVoicePart part) : base(part) { }
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
            SkipPhoneme = true,
        };
    }

    public class DeletePitchPointCommand : PitchExpCommand {
        public int Index;
        public PitchPoint Point;
        public DeletePitchPointCommand(UVoicePart part, UNote note, int index) : base(part) {
            this.Note = note;
            this.Index = index;
            this.Point = Note.pitch.data[Index];
        }
        public override string ToString() { return "Delete pitch point"; }
        public override void Execute() { Note.pitch.data.RemoveAt(Index); }
        public override void Unexecute() { Note.pitch.data.Insert(Index, Point); }
    }

    public class ChangePitchPointShapeCommand : PitchExpCommand {
        public PitchPoint Point;
        public PitchPointShape NewShape;
        public PitchPointShape OldShape;
        public ChangePitchPointShapeCommand(UVoicePart part, PitchPoint point, PitchPointShape shape) : base(part) {
            this.Point = point;
            this.NewShape = shape;
            this.OldShape = point.shape;
        }
        public override string ToString() { return "Change pitch point shape"; }
        public override void Execute() { Point.shape = NewShape; }
        public override void Unexecute() { Point.shape = OldShape; }
    }

    public class SetPitchPointShapeCommand : PitchExpCommand {
        public UNote[] Notes;
        public PitchPointShape NewShape;
        public PitchPointShape[][] OldShapes;
        public SetPitchPointShapeCommand(UVoicePart part, IEnumerable<UNote> notes, PitchPointShape shape) : base(part) {
            this.Notes = notes.ToArray();
            this.NewShape = shape;
            this.OldShapes = notes
                .Select(note => note.pitch.data
                    .Select(point => point.shape)
                    .ToArray())
                .ToArray();
        }
        public override string ToString() { return "Change pitch point shape"; }
        public override void Execute() {
            foreach (var note in Notes) {
                foreach (var point in note.pitch.data) {
                    point.shape = NewShape;
                }
            }
        }
        public override void Unexecute() {
            for (var i = 0; i < Notes.Length; i++) {
                var note = Notes[i];
                var shapes = OldShapes[i];
                for (var p = 0; p < shapes.Length; p++) {
                    note.pitch.data[p].shape = shapes[p];
                }
            }
        }
    }

    public class SnapPitchPointCommand : PitchExpCommand {
        readonly float X, Y;
        public SnapPitchPointCommand(UVoicePart part, UNote note) : base(part) {
            Note = note;
            X = Note.pitch.data.First().X;
            Y = Note.pitch.data.First().Y;
        }
        public override string ToString() { return "Toggle pitch snap"; }
        public override void Execute() {
            Note.pitch.snapFirst = !Note.pitch.snapFirst;
            if (!Note.pitch.snapFirst) {
                Note.pitch.data.First().X = X;
                Note.pitch.data.First().Y = Y;
            }
        }
        public override void Unexecute() {
            Note.pitch.snapFirst = !Note.pitch.snapFirst;
            if (!Note.pitch.snapFirst) {
                Note.pitch.data.First().X = X;
                Note.pitch.data.First().Y = Y;
            }
        }
    }

    public class AddPitchPointCommand : PitchExpCommand {
        public int Index;
        public PitchPoint Point;
        public AddPitchPointCommand(UVoicePart part, UNote note, PitchPoint point, int index) : base(part) {
            this.Note = note;
            this.Index = index;
            this.Point = point;
        }
        public override string ToString() { return "Add pitch point"; }
        public override void Execute() { Note.pitch.data.Insert(Index, Point); }
        public override void Unexecute() { Note.pitch.data.RemoveAt(Index); }
    }

    public class MovePitchPointCommand : PitchExpCommand {
        readonly PitchPoint point;
        readonly float deltaX;
        readonly float deltaY;
        public MovePitchPointCommand(UVoicePart part, PitchPoint point, float deltaX, float deltaY) : base(part) {
            this.point = point;
            this.deltaX = deltaX;
            this.deltaY = deltaY;
        }
        public override string ToString() { return "Move pitch point"; }
        public override void Execute() { point.X += deltaX; point.Y += deltaY; }
        public override void Unexecute() { point.X -= deltaX; point.Y -= deltaY; }
    }

    public class ResetPitchPointsCommand : PitchExpCommand {
        UPitch oldPitch;
        UPitch newPitch;
        public ResetPitchPointsCommand(UVoicePart part, UNote note) : base(part) {
            Note = note;
            oldPitch = note.pitch;
            newPitch = new UPitch();
            int start = NotePresets.Default.DefaultPortamento.PortamentoStart;
            int length = NotePresets.Default.DefaultPortamento.PortamentoLength;
            var shape = NotePresets.Default.DefaultPitchShape;
            newPitch.AddPoint(new PitchPoint(start, 0, shape));
            newPitch.AddPoint(new PitchPoint(start + length, 0, shape));
        }
        public override string ToString() => "Reset pitch points";
        public override void Execute() => Note.pitch = newPitch;
        public override void Unexecute() => Note.pitch = oldPitch;
    }

    public class SetPitchPointsCommand : PitchExpCommand {
        UPitch[] oldPitch;
        UNote[] Notes;
        UPitch newPitch;
        public SetPitchPointsCommand(UVoicePart part, UNote note, UPitch pitch) : base(part) {
            Notes = new UNote[] { note };
            oldPitch = Notes.Select(note => note.pitch).ToArray();
            newPitch = pitch;
        }

        public SetPitchPointsCommand(UVoicePart part, IEnumerable<UNote> notes, UPitch pitch) : base(part) {
            Notes = notes.ToArray();
            oldPitch = Notes.Select(note => note.pitch).ToArray();
            newPitch = pitch;
        }
        public override string ToString() => "Set pitch points";
        public override void Execute(){
            lock (Part) {
                for (var i=0; i<Notes.Length; i++) {
                    Notes[i].pitch = newPitch.Clone();
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                for (var i = 0; i < Notes.Length; i++) {
                    Notes[i].pitch = oldPitch[i];
                }
            }
        }
    }

    public class SetCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int x;
        readonly int y;
        readonly int lastX;
        readonly int lastY;
        int[] oldXs;
        int[] oldYs;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = true,
                SkipPhoneme = true,
            };
        public SetCurveCommand(UProject project, UVoicePart part, string abbr, int x, int y, int lastX, int lastY) : base(part) {
            this.project = project;
            this.abbr = abbr;
            Key = abbr;
            this.x = x;
            this.y = y;
            this.lastX = lastX;
            this.lastY = lastY;
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray();
            oldYs = curve?.ys.ToArray();
        }
        public override string ToString() => "Edit Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            var track = project.tracks[Part.trackNo];
            if (track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                if (curve == null) {
                    curve = new UCurve(descriptor);
                    Part.curves.Add(curve);
                }
                InitializeHiFiUtauNoteBaseline(curve);
                float globalOffset = track.IsHiFiUtauNoteCurve(descriptor)
                    ? SetGlobalCurveCommand.GetGlobalOffset(Part, descriptor.abbr, descriptor)
                    : 0;
                int y1 = (int)Math.Clamp(y - globalOffset, descriptor.min, descriptor.max);
                int lastY1 = (int)Math.Clamp(lastY - globalOffset, descriptor.min, descriptor.max);
                curve.Set(x, y1, lastX, lastY1);
            }
        }
        void InitializeHiFiUtauNoteBaseline(UCurve curve) {
            var track = project.tracks[Part.trackNo];
            if (!track.TryGetExpDescriptor(project, abbr, out var descriptor) ||
                !track.IsHiFiUtauNoteCurve(descriptor)) {
                return;
            }
            int left = Math.Min(x, lastX);
            int right = Math.Max(x, lastX);
            foreach (var phoneme in Part.phonemes.Where(phoneme =>
                !phoneme.Error && phoneme.position <= right && left < phoneme.End)) {
                bool curveAlreadyActive = curve.xs.Count > 0 &&
                    curve.xs[0] < phoneme.End && curve.xs[^1] > phoneme.position;
                var expression = phoneme.GetExpression(project, track, abbr);
                if (curveAlreadyActive || !expression.Item2) {
                    continue;
                }
                int start = phoneme.position;
                int end = phoneme.End;
                int innerEnd = Math.Max(start, end - UCurve.interval);
                int leftValue = curve.Sample(start - UCurve.interval);
                int rightValue = curve.Sample(end);
                Upsert(curve, start - UCurve.interval, leftValue);
                int baseline = (int)Math.Round(expression.Item1);
                Upsert(curve, start, baseline);
                Upsert(curve, innerEnd, baseline);
                Upsert(curve, end, rightValue);
            }
        }
        static void Upsert(UCurve curve, int x, int y) {
            int index = curve.xs.BinarySearch(x);
            if (index >= 0) {
                curve.ys[index] = y;
                return;
            }
            index = ~index;
            curve.xs.Insert(index, x);
            curve.ys.Insert(index, y);
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null) {
                return;
            }
            curve.xs.Clear();
            curve.ys.Clear();
            if (oldXs != null && oldYs != null) {
                curve.xs.AddRange(oldXs);
                curve.ys.AddRange(oldYs);
            }
        }
        public override bool CanMerge(IList<UCommand> commands) {
            return commands.All(c => c is SetCurveCommand);
        }
        public override UCommand Merge(IList<UCommand> commands) {
            var first = commands.First() as SetCurveCommand;
            var last = commands.Last() as SetCurveCommand;
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            curve.Simplify();
            int[] newXs = curve?.xs.ToArray();
            int[] newYs = curve?.ys.ToArray();
            return new MergedSetCurveCommand(
                last.project, last.Part, last.abbr,
                first.oldXs, first.oldYs, newXs, newYs);
        }
    }

    public class MergedSetCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int[] oldXs;
        readonly int[] oldYs;
        readonly int[] newXs;
        readonly int[] newYs;
        readonly bool setReal;
        public MergedSetCurveCommand(UProject project, UVoicePart part,
            string abbr, int[] oldXs, int[] oldYs, int[] newXs, int[] newYs, bool setReal = false) : base(part) {
            this.project = project;
            this.abbr = abbr;
            Key = setReal ? string.Empty : abbr;
            this.oldXs = oldXs;
            this.oldYs = oldYs;
            this.newXs = newXs;
            this.newYs = newYs;
            this.setReal = setReal;
        }
        public override string ToString() => "Edit Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            var track = project.tracks[Part.trackNo];
            if (curve == null && track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                curve = new UCurve(descriptor);
                Part.curves.Add(curve);
            }
            GetCurveXs(curve)?.Clear();
            GetCurveYs(curve)?.Clear();
            if (newXs != null && newYs != null) {
                GetCurveXs(curve)?.AddRange(newXs);
                GetCurveYs(curve)?.AddRange(newYs);
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            var track = project.tracks[Part.trackNo];
            if (curve == null && track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                curve = new UCurve(descriptor);
                Part.curves.Add(curve);
            }
            GetCurveXs(curve)?.Clear();
            GetCurveYs(curve)?.Clear();
            if (oldXs != null && oldYs != null) {
                GetCurveXs(curve)?.AddRange(oldXs);
                GetCurveYs(curve)?.AddRange(oldYs);
            }
        }
        private List<int>? GetCurveXs(UCurve? curve) {
            return setReal ? curve?.realXs : curve?.xs;
        }
        private List<int>? GetCurveYs(UCurve? curve) {
            return setReal ? curve?.realYs : curve?.ys;
        }
    }

    public class ShiftCurveRangeCommand : ExpCommand {
        readonly string abbr;
        readonly int[] oldXs;
        readonly int[] oldYs;
        readonly int[] newXs;
        readonly int[] newYs;
        public bool HasChanges { get; }
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = true,
                SkipPhoneme = true,
            };

        public ShiftCurveRangeCommand(
            UVoicePart part,
            string abbr,
            IEnumerable<(int start, int end, float delta)> ranges) : base(part) {
            this.abbr = abbr;
            var curve = part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            oldXs = curve?.xs.ToArray() ?? Array.Empty<int>();
            oldYs = curve?.ys.ToArray() ?? Array.Empty<int>();
            var xs = oldXs.ToList();
            var ys = oldYs.ToList();
            if (curve != null) {
                foreach (var (start, end, delta) in ranges.OrderBy(range => range.start)) {
                    if (Math.Abs(delta) < 0.001f ||
                        oldXs.Length == 0 || oldXs[0] >= end || oldXs[^1] < start) {
                        continue;
                    }
                    var snapshot = new UCurve(curve.descriptor) {
                        xs = xs.ToList(),
                        ys = ys.ToList(),
                    };
                    int innerEnd = Math.Max(start, end - UCurve.interval);
                    var anchors = new[] {
                        (x: start - UCurve.interval, y: snapshot.Sample(start - UCurve.interval)),
                        (x: start, y: Shift(snapshot.Sample(start))),
                        (x: innerEnd, y: Shift(snapshot.Sample(innerEnd))),
                        (x: end, y: snapshot.Sample(end)),
                    };
                    for (int i = 0; i < xs.Count; i++) {
                        if (start <= xs[i] && xs[i] < end) {
                            ys[i] = Shift(ys[i]);
                        }
                    }
                    foreach (var anchor in anchors) {
                        Upsert(xs, ys, anchor.x, anchor.y);
                    }

                    // Keep the shifted curve unbounded internally. The descriptor range is
                    // applied only when the effective value is displayed or rendered, so
                    // moving a note to a limit does not destroy its original shape.
                    int Shift(int value) => (int)Math.Round(value + delta);
                }
            }
            newXs = xs.ToArray();
            newYs = ys.ToArray();
            HasChanges = !oldXs.SequenceEqual(newXs) || !oldYs.SequenceEqual(newYs);
        }

        public override string ToString() => "Shift Curve";

        public override void Execute() => SetCurve(newXs, newYs);

        public override void Unexecute() => SetCurve(oldXs, oldYs);

        void SetCurve(int[] xs, int[] ys) {
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve == null) {
                return;
            }
            curve.xs.Clear();
            curve.xs.AddRange(xs);
            curve.ys.Clear();
            curve.ys.AddRange(ys);
        }

        static void Upsert(List<int> xs, List<int> ys, int x, int y) {
            int index = xs.BinarySearch(x);
            if (index >= 0) {
                ys[index] = y;
                return;
            }
            index = ~index;
            xs.Insert(index, x);
            ys.Insert(index, y);
        }
    }

    /// <summary>
    /// Sets a HiFiUTAU curve from the note properties panel when no note is selected.
    /// The global value is stored separately from local automation. Curves and
    /// note expressions keep their original shape; consumers add the global
    /// offset and clamp only the effective value at the renderer boundary.
    /// </summary>
    public class SetGlobalCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int[] oldXs;
        readonly int[] oldYs;
        int[] newXs;
        int[] newYs;
        float? oldGlobalValue;
        float? newGlobalValue;

        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = true,
                SkipPhoneme = true,
            };

        public SetGlobalCurveCommand(UProject project, UVoicePart part, string abbr, float? value)
            : base(part) {
            this.project = project;
            this.abbr = abbr;
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray() ?? Array.Empty<int>();
            oldYs = curve?.ys.ToArray() ?? Array.Empty<int>();

            if (!project.tracks[part.trackNo].TryGetExpDescriptor(project, abbr, out var descriptor)) {
                newXs = oldXs;
                newYs = oldYs;
                return;
            }

            bool clearGlobal = !value.HasValue;
            int target = (int)Math.Clamp(
                Math.Round(value ?? descriptor.CustomDefaultValue),
                descriptor.min,
                descriptor.max);
            if (part.hifiUtauGlobalValues?.TryGetValue(abbr, out var storedGlobalValue) == true) {
                oldGlobalValue = storedGlobalValue;
            }
            newGlobalValue = clearGlobal ? null : target;
            if (!clearGlobal && oldXs.Length == 0) {
                int end = Math.Max(UCurve.interval, part.Duration);
                int globalValue = (int)Math.Round(descriptor.CustomDefaultValue);
                newXs = new[] { 0, end };
                newYs = new[] { globalValue, globalValue };
                ApplyNoteOverrides(descriptor, GetNoteOverrides(part, abbr));
            } else {
                newXs = oldXs.ToArray();
                newYs = oldYs
                    .ToArray();
            }
        }

        public static float GetGlobalValue(
            UVoicePart part,
            string abbr,
            UExpressionDescriptor descriptor) {
            if (part.hifiUtauGlobalValues?.TryGetValue(abbr, out var storedGlobalValue) == true) {
                return storedGlobalValue;
            }
            var curve = part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve == null || curve.xs.Count == 0) {
                return descriptor.CustomDefaultValue;
            }
            var xs = curve?.xs.ToArray() ?? Array.Empty<int>();
            var ys = curve?.ys.ToArray() ?? Array.Empty<int>();
            var noteOverrides = GetNoteOverrides(part, abbr);
            int baseline = GetBaseline(xs, ys, noteOverrides, descriptor);
            return baseline;
        }

        public static float GetGlobalOffset(
            UVoicePart part,
            string abbr,
            UExpressionDescriptor descriptor) =>
            part.hifiUtauGlobalValues?.TryGetValue(abbr, out var value) == true
                ? value - descriptor.CustomDefaultValue
                : 0;

        public override string ToString() => "Set Global Curve";

        public override void Execute() {
            SetCurve(newXs, newYs);
            SetGlobalValue(newGlobalValue);
        }

        public override void Unexecute() {
            SetCurve(oldXs, oldYs);
            SetGlobalValue(oldGlobalValue);
        }

        private static List<(int start, int end, UExpression expression)> GetNoteOverrides(
            UVoicePart part,
            string abbr) {
            var ranges = part.phonemes
                .Where(phoneme => !phoneme.Error && phoneme.Parent != null)
                .Select(phoneme => {
                    var note = phoneme.Parent.Extends ?? phoneme.Parent;
                    return (
                        phoneme,
                        expression: note.phonemeExpressions.FirstOrDefault(expression =>
                            expression.abbr == abbr && expression.index == phoneme.index));
                })
                .Where(item => item.expression != null)
                .Select(item => (
                    start: item.phoneme.position,
                    end: item.phoneme.End,
                    expression: item.expression!))
                .ToList();
            foreach (var note in part.notes) {
                if (ranges.Any(range => range.start < note.End && note.position < range.end)) {
                    continue;
                }
                var expression = note.phonemeExpressions
                    .FirstOrDefault(expression => expression.abbr == abbr && expression.index == 0);
                if (expression != null) {
                    ranges.Add((note.position, note.End, expression));
                }
            }
            return ranges.OrderBy(range => range.start).ToList();
        }

        private static int GetBaseline(
            int[] xs,
            int[] ys,
            List<(int start, int end, UExpression expression)> noteOverrides,
            UExpressionDescriptor descriptor) {
            if (xs.Length == 0) {
                return (int)Math.Round(descriptor.CustomDefaultValue);
            }
            int baselineIndex = Array.FindLastIndex(xs, x =>
                !noteOverrides.Any(range => range.start <= x && x < range.end));
            return baselineIndex >= 0
                ? ys[baselineIndex]
                : (int)Math.Round(descriptor.CustomDefaultValue);
        }

        private void ApplyNoteOverrides(
            UExpressionDescriptor descriptor,
            List<(int start, int end, UExpression expression)> noteOverrides) {
            if (noteOverrides.Count == 0 || newXs.Length == 0) {
                return;
            }
            var shiftedGlobal = new UCurve(descriptor) {
                xs = newXs.ToList(),
                ys = newYs.ToList(),
            };
            var xs = newXs.ToList();
            var ys = newYs.ToList();
            foreach (var (start, end, expression) in noteOverrides) {
                int innerEnd = Math.Max(start, end - UCurve.interval);
                int noteValue = (int)Math.Round(Math.Clamp(
                    expression.value, descriptor.min, descriptor.max));
                for (int i = 0; i < xs.Count; i++) {
                    if (start <= xs[i] && xs[i] < end) {
                        ys[i] = noteValue;
                    }
                }
                Upsert(xs, ys, start - UCurve.interval, shiftedGlobal.Sample(start - UCurve.interval));
                Upsert(xs, ys, start, noteValue);
                Upsert(xs, ys, innerEnd, noteValue);
                Upsert(xs, ys, end, shiftedGlobal.Sample(end));
            }
            newXs = xs.ToArray();
            newYs = ys.ToArray();
        }

        private void SetGlobalValue(float? value) {
            if (value.HasValue) {
                Part.hifiUtauGlobalValues ??= new Dictionary<string, float>();
                Part.hifiUtauGlobalValues[abbr] = value.Value;
            } else if (Part.hifiUtauGlobalValues != null) {
                Part.hifiUtauGlobalValues.Remove(abbr);
                if (Part.hifiUtauGlobalValues.Count == 0) {
                    Part.hifiUtauGlobalValues = null;
                }
            }
        }

        private static void Upsert(List<int> xs, List<int> ys, int x, int y) {
            int index = xs.BinarySearch(x);
            if (index >= 0) {
                ys[index] = y;
                return;
            }
            index = ~index;
            xs.Insert(index, x);
            ys.Insert(index, y);
        }

        private void SetCurve(int[] xs, int[] ys) {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (xs.Length == 0) {
                if (curve != null) {
                    curve.xs.Clear();
                    curve.ys.Clear();
                }
                return;
            }
            if (curve == null && project.tracks[Part.trackNo].TryGetExpDescriptor(project, abbr, out var descriptor)) {
                curve = new UCurve(descriptor);
                Part.curves.Add(curve);
            }
            if (curve == null) {
                return;
            }
            curve.xs.Clear();
            curve.xs.AddRange(xs);
            curve.ys.Clear();
            curve.ys.AddRange(ys);
        }
    }

    public class PasteCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int[] xs;
        readonly int[] ys;
        int[]? oldXs;
        int[]? oldYs;
        public PasteCurveCommand(UProject project, UVoicePart part, string abbr, IEnumerable<int> xs, IEnumerable<int> ys) : base(part) {
            this.project = project;
            this.abbr = abbr;
            Key = abbr;
            this.xs = xs.ToArray();
            this.ys = ys.ToArray();
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray();
            oldYs = curve?.ys.ToArray();
        }
        public PasteCurveCommand(UProject project, UVoicePart part, string abbr, int startX, int startY, int endX, int endY) : base(part) {
            this.project = project;
            this.abbr = abbr;
            Key = abbr;
            this.xs = new int[] { startX, endX };
            this.ys = new int[] { startY, endY };
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray();
            oldYs = curve?.ys.ToArray();
        }
        public override string ToString() => "Edit Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            var track = project.tracks[Part.trackNo];
            if (track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                if (curve == null) {
                    curve = new UCurve(descriptor);
                    Part.curves.Add(curve);
                }

                var xs = this.xs.ToList();
                var ys = this.ys.ToList();
                xs.Insert(0, xs[0] - UCurve.interval);
                ys.Insert(0, curve.Sample(xs[0]));
                xs.Add(xs.Last() + UCurve.interval);
                ys.Add(curve.Sample(xs.Last()));
                ys = ys.Select(y => (int)Math.Clamp(y, descriptor.min, descriptor.max)).ToList();

                curve.Set(xs.First(), ys.First(), xs.First(), ys.First());
                curve.Set(xs.Last(), ys.Last(), xs.Last(), ys.Last());
                for (int i = 0; i < xs.Count - 1; i++) {
                    curve.Set(xs[i + 1], ys[i + 1], xs[i], ys[i]);
                }
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null) {
                return;
            }
            curve.xs.Clear();
            curve.ys.Clear();
            if (oldXs != null && oldYs != null) {
                curve.xs.AddRange(oldXs);
                curve.ys.AddRange(oldYs);
            }
        }
    }

    public class ClearCurveCommand : ExpCommand {
        readonly string abbr;
        readonly int[] oldXs;
        readonly int[] oldYs;
        public ClearCurveCommand(UVoicePart part, string abbr) : base(part) {
            this.abbr = abbr;
            Key = abbr;
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve != null) {
                oldXs = curve.xs.ToArray();
                oldYs = curve.ys.ToArray();
            }
        }
        public override string ToString() => "Clear Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve != null) {
                curve.xs.Clear();
                curve.ys.Clear();
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve != null && oldXs != null && oldYs != null) {
                curve.xs.Clear();
                curve.xs.AddRange(oldXs);
                curve.ys.Clear();
                curve.ys.AddRange(oldYs);
            }
        }
    }
}
