using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxCharger
{
    // The highest EX score a chart allows, the value music_db carries as max_exscore: 5 per chip and
    // 2 per judgement tick of a long note or laser.
    //
    // The tick rules were fitted to the game's own 8,649 charts (2026-10), whose max_exscore values
    // are known; they reproduce 92% of them exactly and nearly all of the rest within a few points:
    //   * ticks are 1/16 notes, 1/8 notes from 255 BPM on (not doubling again at higher BPM)
    //   * a long note counts ticks from its start; from 6 ticks it loses the last, from 7 the first
    //   * a laser counts the ticks of the measure grid it covers, plus one per slam; grid ticks within
    //     50 ms of a slam are dropped
    // Stops are ignored. Remaining misses sit in charts with BPM changes inside objects, objects off
    // the 1/16 grid and other time signatures.
    public static class ExScoreCalculator
    {
        private const int ChipPoints  = 5;
        private const int TickPoints  = 2;
        private const double SlamWindowMs = 50.0;

        public static int Calculate(VoxChart chart)
        {
            var events = chart.Events;
            int chips = 0, ticks = 0;

            // Auto tab rows are parsed as buttons on a track beyond FX-R; they score nothing
            foreach (var button in events.OfType<Event.Button>().Where(b => Enum.IsDefined(typeof(Event.ButtonTrack), b.Track)))
            {
                if (button.HoldLength <= 0)
                {
                    chips++;
                    continue;
                }

                int start = events.ToTicks(button.Time);
                int m = 0;
                for (double t = start; t < start + button.HoldLength - 1e-6; t += Interval(events, t))
                    m++;

                ticks += m - (m >= 6 ? 1 : 0) - (m >= 7 ? 1 : 0);
            }

            foreach (var section in Sections(events))
                ticks += LaserTicks(events, section);

            return chips * ChipPoints + ticks * TickPoints;
        }

        // Laser points of one track between a start and an end flag, in time order. A slam is a Slam
        // event (parsed .vox files: its start point is in the collection, its end point only in the
        // Slam) or two points on one tick at different positions (KSH imports).
        private sealed class Section
        {
            public readonly List<(int tick, Event.Laser point)> Points = new List<(int, Event.Laser)>();
            public readonly HashSet<int> Slams = new HashSet<int>();
        }

        private static IEnumerable<Section> Sections(EventCollection events)
        {
            var lasers = events.OfType<Event.Laser>().ToList();
            var slams  = events.OfType<Event.Slam>().ToList();
            foreach (var track in lasers.Select(l => l.Track).Concat(slams.Select(s => s.Track)).Distinct())
            {
                // The slam starting at a point; a chained slam starts at the previous slam's end point
                var slamFrom = new Dictionary<Event.Laser, Event.Slam>();
                foreach (var slam in slams.Where(s => s.Track == track))
                    slamFrom[slam.Start] = slam;

                var seen = new HashSet<Event.Slam>();
                var sections = new List<Section>();
                Section open = null;
                foreach (var point in lasers.Where(l => l.Track == track).OrderBy(l => events.ToTicks(l.Time)))
                {
                    int tick = events.ToTicks(point.Time);
                    if (point.Flag == Event.LaserFlag.Start || open == null)
                    {
                        if (open != null && open.Points.Count > 0) sections.Add(open);
                        open = new Section();
                    }
                    if (open.Points.Count > 0)
                    {
                        var last = open.Points[open.Points.Count - 1];
                        if (last.tick == tick && last.point.Offset != point.Offset) open.Slams.Add(tick);
                    }
                    open.Points.Add((tick, point));
                    bool ended = point.Flag == Event.LaserFlag.End;
                    for (var from = point; slamFrom.TryGetValue(from, out var slam) && seen.Add(slam); from = slam.End)
                    {
                        open.Slams.Add(tick);
                        open.Points.Add((tick, slam.End));
                        ended |= slam.End.Flag == Event.LaserFlag.End;
                    }
                    if (ended) { sections.Add(open); open = null; }
                }
                if (open != null && open.Points.Count > 0) sections.Add(open);

                // A slam whose start point is not in the collection: count it in the section covering it
                foreach (var slam in slams.Where(s => s.Track == track && !seen.Contains(s)))
                {
                    int tick = events.ToTicks(slam.Time);
                    var section = sections.FirstOrDefault(x => x.Points[0].tick <= tick && tick <= x.Points[x.Points.Count - 1].tick);
                    if (section == null) { section = new Section(); section.Points.Add((tick, slam.Start)); section.Points.Add((tick, slam.End)); sections.Add(section); }
                    section.Slams.Add(tick);
                }
                foreach (var section in sections)
                    yield return section;
            }
        }

        private static int LaserTicks(EventCollection events, Section section)
        {
            var points = section.Points;
            var slams  = section.Slams;
            int first  = points[0].tick;
            int last   = points[points.Count - 1].tick;
            int count = slams.Count;
            if (last <= first)
                return count;

            int origin      = events.MeasureStartTick(points[0].point.Time.Measure);
            double interval = Interval(events, first);
            for (double t = origin + Math.Ceiling((first - origin) / interval - 1e-9) * interval; t < last - 1e-6; t += Interval(events, t))
            {
                int tick = (int)Math.Round(t);
                if (slams.Contains(tick))
                    continue;

                double msPerTick = 60000.0 / BpmAt(events, t) / 48.0;
                if (slams.All(s => Math.Abs(tick - s) * msPerTick >= SlamWindowMs - 1e-9))
                    count++;
            }

            return count;
        }

        // Judgement tick spacing in ticks (192 per whole note): a 1/16 note, a 1/8 note from 255 BPM
        private static double Interval(EventCollection events, double tick)
        {
            return BpmAt(events, tick) >= 255f ? 24 : 12;
        }

        private static float BpmAt(EventCollection events, double tick)
        {
            var bpm = events.GetBpm(events.FromTicks((int)Math.Round(tick)));
            if (bpm != null && bpm.Value > 0)
                return bpm.Value;

            var firstBpm = events.OfType<Event.Bpm>().OrderBy(b => events.ToTicks(b.Time)).FirstOrDefault();
            return firstBpm != null && firstBpm.Value > 0 ? firstBpm.Value : 120f;
        }
    }
}
