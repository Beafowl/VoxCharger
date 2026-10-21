using System;

namespace VoxCharger
{
    public abstract partial class Camera
    {
        public class Tilt : Camera
        {
            // Column C6 of a manual tilt row: 0 inside a series of manual tilts, 1 a single row,
            // 2 the first row of a series, 3 the last row of a series. The game resumes the
            // laser-driven tilt after a series ends.
            public const int NodeContinue = 0;
            public const int NodeSingle   = 1;
            public const int NodeStart    = 2;
            public const int NodeEnd      = 3;

            public int NodeType { get; set; } = NodeContinue;

            public Tilt(Time time, int duration, float start, float end)
                : base(time, WorkType.Tilt)
            {
                Duration = duration;
                Start    = start;
                End      = end;
            }

            public override string ToString()
            {
                return $"{base.ToString()}" +
                       $"\tTilt" +
                       $"\t2" +
                       $"\t{Duration}" +
                       $"\t{Start:0.00}" +
                       $"\t{End:0.00}" +
                       $"\t{NodeType:0.00}" +
                       $"\t0.00";
            }
        }
    }
}
