using System;

namespace VoxCharger
{
    public abstract partial class Event
    {
        public enum LaserTrack
        {
            Left  = 1,
            Right = 8
        }

        public enum LaserFlag
        {
            Tick  = 0,
            Start = 1,
            End   = 2
        }

        // VOX roll types (laser column C3). Lengths are in quarter notes (column C8); 0 selects the
        // game's default length: 7 beats for Measure, 2 for HalfMeasure, 3 for ThreeBeat and Swing,
        // 12 for TripleMeasure.
        public enum SlamImpact
        {
            None          = 0,
            Measure       = 1, // single 360 degree roll
            HalfMeasure   = 2, // single roll, 2 beat default
            ThreeBeat     = 3, // single roll, 3 beat default
            TripleMeasure = 4, // three rolls
            Swing         = 5  // swing out and back, no full turn
        }

        public enum LaserFilter
        {
            None       = -1,
            Peak       = 0,
            LowPass    = 1,
            HighPass   = 3,
            BitCrusher = 5
        }

        public enum SlamDirection
        {
            Left,
            Right
        }

        public class Laser : Event
        {
            public LaserTrack Track { get; set; }
            public int Offset { get; set; }
            public LaserFlag Flag { get; set; }
            public SlamImpact Impact { get; set; }
            public int Range { get; set; }
            public LaserFilter Filter { get; set; }
            public bool IsLaserSlam { get; set; }

            // Roll length in quarter notes (VOX column C8), 0 = the roll type's default
            public int RollLength { get; set; }

            public Laser(Time time, LaserTrack track, int offset, LaserFlag flag, SlamImpact impact)
                : base (time)
            {
                Track  = track;
                Offset = offset;
                Flag   = flag;
                Impact = impact;
            }

            public Laser(Time time, LaserTrack track, int offset, LaserFlag flag, SlamImpact impact, LaserFilter filter, int range)
                : this(time, track, offset, flag, impact)
            {
                Filter = filter;
                Range  = range;
            }

            public override string ToString()
            {
                // C1 position 0-127, C2 node type, C3 roll type, C4 laser effect (6 = none),
                // C5 range, C6 unused, C7 curve type (0 linear), C8 roll length
                int filter = Filter == LaserFilter.None ? 6 : (int)Filter;
                return $"{base.ToString()}\t{Offset}\t{(int)Flag}\t{(int)Impact}\t{filter}\t{Math.Max(1, Range)}\t0\t0\t{RollLength}";
            }
        }

        // TODO: Pairing like this tend to break stuffs, need more tests, or don't use it at all
        public class Slam : Event
        {
            public Laser Start { get; set; }
            public Laser End { get; set; }
            public LaserTrack Track => Start.Track;
            public SlamDirection Direction => Start.Offset > End.Offset ? SlamDirection.Left : SlamDirection.Right;

            public Slam(Time time, Laser start, Laser end)
                : base (time)
            {
                Start = start;
                End   = end;
            }
        }
    }
}
