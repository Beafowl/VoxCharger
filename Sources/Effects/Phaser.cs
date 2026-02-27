using System;
using System.Linq;

namespace VoxCharger
{
    public partial class Effect
    {
        public class Phaser : Effect
        {
            public float Mix       { get; set; }
            public float Period    { get; set; }
            public float Feedback  { get; set; }
            public int StereoWidth { get; set; }
            public float HiCutGain { get; set; }

            public Phaser(float mix, float period, float feedback, int stereoWidth, float hiCutGain)
                : base(FxType.Phaser)
            {
                Mix         = mix;
                Period      = period;
                Feedback    = feedback;
                StereoWidth = stereoWidth;
                HiCutGain   = hiCutGain;
            }

            private Phaser()
                : base(FxType.None)
            {
            }

            public new static Phaser FromVox(string data)
            {
                var flanger = new Phaser();
                var prop = data.Trim().Split(',').Select(p => p.Trim()).ToArray();
                if (!Enum.TryParse(prop[0], out FxType type) || type != FxType.Phaser)
                    return flanger;

                if (prop.Length != 6)
                    return flanger;

                try
                {
                    flanger.Type        = type;
                    flanger.Mix         = float.Parse(prop[1]);
                    flanger.Period      = int.Parse(prop[2]);
                    flanger.Feedback    = float.Parse(prop[3]);
                    flanger.StereoWidth = int.Parse(prop[4]);
                    flanger.HiCutGain   = float.Parse(prop[5]);
                }
                catch (Exception)
                {
                    flanger.Type = FxType.None;
                }

                return flanger;
            }

            public new static Wobble FromKsh(string data)
            {
                var prop = data.Trim().Split(';').Select(p => p.Trim()).ToArray();
                if (!Enum.TryParse(prop[0], out FxType type) || type != FxType.Phaser)
                    return null;

                var wobble  = new Wobble(100.00f, 1500.00f, 20000.00f, 0.50f, 1.41f);
                wobble.Flag = 1;

                return wobble;
            }

            // KSH Phaser maps to VOX Wobble (FxType 6)
            public new static Wobble FromKsh(KshDefinition definition)
            {
                try
                {
                    definition.GetValue("mix",      out float mix);
                    definition.GetValue("loFreq",   out float loFreq);
                    definition.GetValue("hiFreq",   out float hiFreq);
                    definition.GetValue("Q",        out float q);
                    definition.GetValue("feedback", out float feedback);

                    // KSH percentages are normalized (0.0-1.0), VOX expects 0-100 scale
                    float wobbleMix  = mix > 0 ? mix * 100f : 100.00f;
                    float lowFreq    = loFreq > 0 ? loFreq : 1500.00f;
                    float highFreq   = hiFreq > 0 ? hiFreq : 20000.00f;
                    float resonance  = q > 0 ? q : 1.41f;
                    float waveLength = feedback > 0 ? feedback * 2f : 0.50f;

                    var wobble  = new Wobble(wobbleMix, lowFreq, highFreq, waveLength, resonance);
                    wobble.Flag = 1;

                    return wobble;
                }
                catch (Exception)
                {
                    var wobble  = new Wobble(100.00f, 1500.00f, 20000.00f, 0.50f, 1.41f);
                    wobble.Flag = 1;
                    return wobble;
                }
            }

            public override string ToString()
            {
                if (Type == FxType.None)
                    return base.ToString();

                return $"{(int)Type},"       +
                       $"\t{Mix:0.00},"      +
                       $"\t{Period:0.00},"   +
                       $"\t{Feedback:0.00}," +
                       $"\t{StereoWidth},"   +
                       $"\t{HiCutGain:0.00}";
            }

            // VOX Phaser came from KSH Flanger
            public override string ToKsh()
            {
                if (Type == FxType.None)
                    return string.Empty;

                return "Flanger";
            }
        }
    }
}
