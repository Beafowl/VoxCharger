using System;

namespace VoxCharger
{
    // VOX position: measure (1-based), beat within the measure (1-based) and cell within the beat
    // (0-based). A beat is one note of the time signature's denominator, so a beat holds
    // 192 / denominator cells (48 in x/4, 24 in x/8, 12 in x/16); a measure holds numerator beats.
    public class Time
    {
        public static readonly Time Initial = new Time(1, 1, 0);

        public const int TicksPerWholeNote = 192;

        public int Measure { get; set; }
        public int Beat    { get; set; }
        public int Offset  { get; set; }

        public Time()
            : this(1, 1, 0)
        {
        }

        public Time(int measure, int beat, int cell)
        {
            Measure = measure;
            Beat    = beat;
            Offset  = cell;
        }

        public static Time FromString(string input)
        {
            try
            {
                var data = input.Split(',');
                if (data.Length != 3)
                    return null;

                return new Time()
                {
                    Measure = int.Parse(data[0]),
                    Beat    = int.Parse(data[1]),
                    Offset  = int.Parse(data[2])
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Cells per beat for a signature: 192 / denominator (48 for x/4)
        public static int CellsPerBeat((int beat, int note) signature)
        {
            return TicksPerWholeNote / Math.Max(1, signature.note);
        }

        // Real ticks (cells) in one measure of the signature
        public static int CellsPerMeasure((int beat, int note) signature)
        {
            return Math.Max(1, signature.beat) * CellsPerBeat(signature);
        }

        // Converts a *normalized* position (every measure spans 192 units, whatever its signature;
        // used for KSH line positions and the KSH serializer grid) into a VOX position.
        public static Time FromOffset(float absOffset, (int beat, int note) signature)
        {
            int   beats        = Math.Max(1, signature.beat);
            int   cellsPerBeat = CellsPerBeat(signature);
            float beatLength   = TicksPerWholeNote / (float)beats;

            int   measure   = (int)Math.Floor(absOffset / TicksPerWholeNote);
            float inMeasure = absOffset - measure * (float)TicksPerWholeNote;
            int   beat      = (int)Math.Floor(inMeasure / beatLength);
            float fraction  = (inMeasure - beat * beatLength) / beatLength;
            int   cell      = (int)Math.Round(fraction * cellsPerBeat);

            // Rounding may land exactly on the next beat
            beat += cell / cellsPerBeat;
            cell %= cellsPerBeat;

            measure += beat / beats;
            beat    %= beats;

            return new Time(measure, beat + 1, cell);
        }

        // Inverse of FromOffset: normalized position (192 units per measure)
        public int GetAbsoluteOffset((int beat, int note) signature)
        {
            int   beats        = Math.Max(1, signature.beat);
            int   cellsPerBeat = CellsPerBeat(signature);
            float beatLength   = TicksPerWholeNote / (float)beats;

            float offset = Measure * (float)TicksPerWholeNote;
            offset      += (Beat - 1) * beatLength;
            offset      += Offset / (float)cellsPerBeat * beatLength;

            return (int)Math.Round(offset);
        }

        // Real tick difference to another position, assuming the same signature applies to both.
        // EventCollection.ToTicks handles signature changes in between.
        public int Difference(Time time, (int beat, int note) signature)
        {
            int cellsPerBeat = CellsPerBeat(signature);
            return (Measure - time.Measure) * cellsPerBeat * Math.Max(1, signature.beat) +
                   (Beat    - time.Beat)    * cellsPerBeat +
                   (Offset  - time.Offset);
        }

        // Adds a normalized position (see FromOffset)
        public Time Add(int position, (int beat, int note) signature)
        {
            return FromOffset(GetAbsoluteOffset(signature) + position, signature);
        }

        public Time Add(Time time, (int beat, int note) signature)
        {
            int beats        = Math.Max(1, signature.beat);
            int cellsPerBeat = CellsPerBeat(signature);

            int measure = Measure + time.Measure;
            int beat    = Beat    + time.Beat - 1;
            int offset  = Offset  + time.Offset;

            beat   += offset / cellsPerBeat;
            offset %= cellsPerBeat;

            measure += (beat - 1) / beats;
            beat     = (beat - 1) % beats + 1;

            return new Time(measure, beat, offset);
        }

        // Lexicographic order on (Measure, Beat, Offset). Only meaningful when
        // both Times share a time signature basis, but BPM/signature lookups
        // only compare positions authored in the same chart so this is fine.
        public static bool operator <=(Time a, Time b)
        {
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
                return ReferenceEquals(a, b);
            if (a.Measure != b.Measure) return a.Measure < b.Measure;
            if (a.Beat    != b.Beat)    return a.Beat    < b.Beat;
            return a.Offset <= b.Offset;
        }

        public static bool operator >=(Time a, Time b)
        {
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
                return ReferenceEquals(a, b);
            if (a.Measure != b.Measure) return a.Measure > b.Measure;
            if (a.Beat    != b.Beat)    return a.Beat    > b.Beat;
            return a.Offset >= b.Offset;
        }

        public static bool operator ==(Time a, Time b)
        {
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
                return ReferenceEquals(a, b);

            return a.Measure == b.Measure && a.Beat == b.Beat && a.Offset == b.Offset;
        }

        public static bool operator !=(Time a, Time b)
        {
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
                return !ReferenceEquals(a, b);

            return a.Measure != b.Measure || a.Beat != b.Beat || a.Offset != b.Offset;
        }

        public static Time operator +(Time time, int position)
        {
            return time.Add(position, (4, 4));
        }

        public static Time operator +(Time a, Time b)
        {
            return a.Add(b, (4, 4));
        }

        public override bool Equals(object obj)
        {
            return obj is Time time && time == this;
        }

        public override int GetHashCode()
        {
            return Measure + Beat + Offset;
        }

        public Time Clone()
        {
            return new Time(Measure, Beat, Offset);
        }

        public override string ToString()
        {
            return $"{Measure:D3},{Beat:D2},{Offset:D2}";
        }
    }
}
