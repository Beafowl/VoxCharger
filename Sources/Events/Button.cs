using System;
using System.Collections.Generic;

namespace VoxCharger
{
    public abstract partial class Event
    {
        public enum ButtonTrack
        {
            A   = 3,
            B   = 4,
            C   = 5,
            D   = 6,
            FxL = 2,
            FxR = 7
        }

        // FX chip sample ids as the game reads them from TRACK2 / TRACK7 column C2
        public enum ChipFx
        {
            None            = 0,
            BigSnareQuiet   = 1,
            Clap            = 2,
            ShortClap       = 3,
            Snare           = 4,
            ShortSnare      = 5,
            Crash           = 6,
            KickCrashDown   = 7,
            OpenHiHat       = 8,
            KickCrash       = 9,
            SnareClick      = 10,
            VoiceOh         = 11,
            VoiceHey        = 12,
            VoiceYeah       = 13,
            Fireworks       = 14
        }

        // KSH fx-l_se / fx-r_se preset names and the closest game sample
        private static readonly Dictionary<string, ChipFx> _chipFxByKshName = new Dictionary<string, ChipFx>(StringComparer.OrdinalIgnoreCase)
        {
            { "clap",        ChipFx.Clap },
            { "clap_impact", ChipFx.Clap },
            { "clap_punchy", ChipFx.ShortClap },
            { "snare",       ChipFx.ShortSnare },
            { "snare_lo",    ChipFx.Snare },
        };

        public static ChipFx ChipFxFromKsh(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ChipFx.None;

            name = name.Trim();
            if (_chipFxByKshName.TryGetValue(name, out var fx))
                return fx;

            // Legacy enum names ("ClapImpact", "SnareLow") written by older versions
            string compact = name.Replace("_", string.Empty);
            foreach (var pair in _chipFxByKshName)
            {
                if (string.Equals(pair.Key.Replace("_", string.Empty), compact, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            // Custom sample files have no game equivalent
            return ChipFx.None;
        }

        public static string ChipFxToKsh(ChipFx fx)
        {
            switch (fx)
            {
                case ChipFx.Clap:        return "clap";
                case ChipFx.ShortClap:   return "clap_punchy";
                case ChipFx.ShortSnare:  return "snare";
                case ChipFx.Snare:
                case ChipFx.BigSnareQuiet:
                case ChipFx.SnareClick:  return "snare_lo";
                case ChipFx.None:        return string.Empty;
                default:                 return "clap_impact";
            }
        }

        public class Button : Event
        {
            public ButtonTrack Track { get; set; }
            public int HoldLength    { get; set; }
            public Effect HoldFx     { get; set; }
            public ChipFx HitFx      { get; set; }
            public bool IsFx => Track == ButtonTrack.FxL || Track == ButtonTrack.FxR;

            public Button(Time time, ButtonTrack track, int holdLength, Effect holdFx = null, ChipFx chipFx = ChipFx.None)
                : base (time)
            {
                Track      = track;
                HoldLength = holdLength;
                HoldFx     = holdFx;
                HitFx      = chipFx;
            }

            public override string ToString()
            {
                int fx = 0;
                if (HoldFx != null)
                    fx = HoldFx.Id;
                else
                    fx = (int)HitFx;

                return $"{base.ToString()}\t{HoldLength}\t{fx}";
            }
        }
    }
}
