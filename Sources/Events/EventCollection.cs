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
            set => _events.AddRange(value.Where(ev => ev != null).Select(ev => { ev.Time = time; return ev; }));
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
            if (ev != null)
                _events.Add(ev);
        }

        public void Add(params Event[] ev)
        {
            _events.AddRange(new List<Event>(ev).FindAll(e => e != null));
        }

        public bool Remove(Event ev)
        {
            return _events.Remove(ev);
        }

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
