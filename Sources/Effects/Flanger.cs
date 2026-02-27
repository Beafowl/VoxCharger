using System;
using System.Linq;

namespace VoxCharger
{
    public partial class Effect
    {
        public class Flanger : Effect
        {
            public float Mix     { get; set; }
            public float Samples { get; set; }
            public float Depth   { get; set; }
            public float Period  { get; set; }

            public Flanger(float mix, float samples, float depth, float period)
                : base(FxType.Flanger)
            {
                Mix     = mix;
                Samples = samples;
                Depth   = depth;
                Period  = period;
            }

            public Flanger()
                : base(FxType.None)
            {
            }

            public new static Flanger FromVox(string data)
            {
                var highPass = new Flanger();
                var prop = data.Trim().Split(',').Select(p => p.Trim()).ToArray();
                if (!Enum.TryParse(prop[0], out FxType type) || type != FxType.Flanger)
                    return highPass;

                if (prop.Length != 5)
                    return highPass;

                try
                {
                    highPass.Type    = type;
                    highPass.Mix     = float.Parse(prop[1]);
                    highPass.Samples = float.Parse(prop[2]);
                    highPass.Depth   = float.Parse(prop[3]);
                    highPass.Period  = float.Parse(prop[4]);
                }
                catch (Exception)
                {
                    highPass.Type = FxType.None;
                }

                return highPass;
            }

            public new static Phaser FromKsh(string data)
            {
                var prop = data.Trim().Split(';').Select(p => p.Trim()).ToArray();
                if (!Enum.TryParse(prop[0], out FxType type) || type != FxType.Flanger)
                    return null;

                return new Phaser(80.00f, 2.00f, 0.50f, 90, 2.00f);
            }

            // KSH Flanger maps to VOX Phaser (FxType 3)
            public new static Phaser FromKsh(KshDefinition definition)
            {
                try
                {
                    definition.GetValue("volume",      out float volume);
                    definition.GetValue("period",      out float period);
                    definition.GetValue("feedback",    out float feedback);
                    definition.GetValue("stereoWidth", out int stereoWidth);

                    // KSH percentages are normalized (0.0-1.0), VOX expects 0-100 scale
                    float mix = volume > 0 ? volume * 100f : 80.00f;
                    float phaserPeriod = period > 0 ? period * 2.67f : 2.00f;
                    float fb = feedback > 0 ? feedback : 0.50f;

                    return new Phaser(mix, phaserPeriod, fb, stereoWidth > 0 ? stereoWidth : 90, 2.00f);
                }
                catch (Exception)
                {
                    return new Phaser(80.00f, 2.00f, 0.50f, 90, 2.00f);
                }
            }

            public override string ToString()
            {
                if (Type == FxType.None)
                    return base.ToString();

                return $"{(int)Type},"      +
                       $"\t{Mix:0.00},"     +
                       $"\t{Samples:0.00}," +
                       $"\t{Depth:0.00},"   +
                       $"\t{Period:0.00}";
            }

            public override string ToKsh()
            {
                if (Type == FxType.None)
                    return string.Empty;

                return "Flanger";
            }
        }

    }
}
