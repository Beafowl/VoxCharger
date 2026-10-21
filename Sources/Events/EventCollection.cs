using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VoxCharger
{
    public class EventCollection : ICollection<Event>
    {
        private List<Event> _events = new List<Event>();

        public int Count => _events.Count;

        public bool IsReadOnly => false;

        public EventCollection()
        {
        }

        public Event[] this[int measure]
        {
            get => _events.FindAll((ev) => ev.Time.Measure == measure).ToArray();
        }

        public Event[] this[Time time]
        {
            get => _events.FindAll((ev) => ev.Time == time).ToArray();
            set
            {
                foreach (var ev in value.Where(e => e != null))
                {
                    ev.Time = time;
                    Add(ev);
                }
            }
        }

        public Event.TimeSignature GetTimeSignature(int measure)
        {
            return GetTimeSignature(new Time(measure, 1, 0));
        }

        public Event.TimeSignature GetTimeSignature(Time time)
        {
            // "Active signature at `time`" = the last signature event whose
            // position is <= time. Previous implementation used `Measure <`,
            // which ignored signature changes earlier in the SAME measure and
            // would return the prior measure's signature.
            var timeSig = _events.LastOrDefault(ev =>
                ev is Event.TimeSignature && ev.Time <= time
            ) as Event.TimeSignature;

            return timeSig != null ? timeSig : new Event.TimeSignature(time, 4, 4);
        }

        public Event.Bpm GetBpm(int measure)
        {
            return GetBpm(new Time(measure, 1, 0));
        }

        public Event.Bpm GetBpm(Time time)
        {
            // "Active BPM at `time`" = the last Bpm event at a position <=
            // time. The previous `Measure <` check silently dropped any BPM
            // change authored earlier in the same measure — e.g. a `t=880`
            // set at (79,1,0) would be invisible to a stop-end lookup at
            // (79,K,24), which then resumed at whatever BPM had been active
            // several measures prior. On And Revive's measure 79 (50/4 @ 880
            // with 44× stop=24) every stop resumed at ~110 BPM, scrolling
            // the chart 8× slower between stops and producing the ~13-20 s
            // of visible lag the user was hitting.
            return _events.LastOrDefault(ev =>
                ev is Event.Bpm && ev.Time <= time
            ) as Event.Bpm;
        }

        public void Add(Event ev)
        {
            if (ev == null)
                return;

            _events.Add(ev);
            if (ev is Event.TimeSignature)
                _segments = null;
        }

        public void Add(params Event[] ev)
        {
            foreach (var e in ev)
                Add(e);
        }

        public bool Remove(Event ev)
        {
            if (ev is Event.TimeSignature)
                _segments = null;

            return _events.Remove(ev);
        }

        #region --- Real tick positions ---

        // Signature segments: from `startMeasure` on, each measure has `cellsPerMeasure` cells and
        // starts at `startTick + (measure - startMeasure) * cellsPerMeasure`
        private struct Segment
        {
            public int StartMeasure;
            public int StartTick;
            public int CellsPerBeat;
            public int CellsPerMeasure;
        }

        private List<Segment> _segments;

        private List<Segment> Segments
        {
            get
            {
                if (_segments != null)
                    return _segments;

                var segments = new List<Segment>();
                var signatures = _events.OfType<Event.TimeSignature>()
                    .Where(s => s.Beat > 0 && s.Note > 0)
                    .OrderBy(s => s.Time.Measure)
                    .ToList();

                var first = new Segment { StartMeasure = 1, StartTick = 0, CellsPerBeat = 48, CellsPerMeasure = 192 };
                segments.Add(first);
                foreach (var sig in signatures)
                {
                    int measure = Math.Max(1, sig.Time.Measure);
                    var last    = segments[segments.Count - 1];
                    var next    = new Segment
                    {
                        StartMeasure    = measure,
                        CellsPerBeat    = Time.CellsPerBeat((sig.Beat, sig.Note)),
                        CellsPerMeasure = Time.CellsPerMeasure((sig.Beat, sig.Note)),
                    };

                    if (measure == last.StartMeasure)
                    {
                        // Same measure: the later definition wins
                        next.StartTick = last.StartTick;
                        segments[segments.Count - 1] = next;
                    }
                    else
                    {
                        next.StartTick = last.StartTick + (measure - last.StartMeasure) * last.CellsPerMeasure;
                        segments.Add(next);
                    }
                }

                _segments = segments;
                return segments;
            }
        }

        private Segment SegmentOfMeasure(int measure)
        {
            var segments = Segments;
            var result   = segments[0];
            foreach (var s in segments)
            {
                if (s.StartMeasure > measure)
                    break;

                result = s;
            }

            return result;
        }

        // Tick (1/48 quarter note) at which a measure starts, honouring every signature change
        public int MeasureStartTick(int measure)
        {
            measure = Math.Max(1, measure);
            var s   = SegmentOfMeasure(measure);
            return s.StartTick + (measure - s.StartMeasure) * s.CellsPerMeasure;
        }

        // Real ticks from the chart start for a VOX position
        public int ToTicks(Time time)
        {
            int measure = Math.Max(1, time.Measure);
            var s       = SegmentOfMeasure(measure);
            return MeasureStartTick(measure) + (time.Beat - 1) * s.CellsPerBeat + time.Offset;
        }

        // VOX position for real ticks from the chart start
        public Time FromTicks(int ticks)
        {
            ticks = Math.Max(0, ticks);
            var segments = Segments;
            var s = segments[0];
            foreach (var candidate in segments)
            {
                if (candidate.StartTick > ticks)
                    break;

                s = candidate;
            }

            int measure = s.StartMeasure + (ticks - s.StartTick) / s.CellsPerMeasure;
            int rest    = (ticks - s.StartTick) % s.CellsPerMeasure;
            return new Time(measure, rest / s.CellsPerBeat + 1, rest % s.CellsPerBeat);
        }

        // Real tick length of one measure
        public int MeasureLength(int measure)
        {
            return SegmentOfMeasure(Math.Max(1, measure)).CellsPerMeasure;
        }

        #endregion

        public bool Contains(Event ev)
        {
            return _events.Contains(ev);
        }

        public void CopyTo(Event[] events, int index)
        {
            _events.CopyTo(events, index);
        }

        public void Clear()
        {
            _events.Clear();
            _segments = null;
        }

        public IEnumerator<Event> GetEnumerator()
        {
            return _events.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _events.GetEnumerator();
        }
    }
}
