using System;
using System.Linq;

namespace VoxCharger
{
    public partial class Effect
    {
        public class BitCrusher : Effect
        {
            public float Mix { get; set; }

            public int Reduction { get; set; }

            public BitCrusher(float mix, int reduction)
                : base(FxType.BitCrusher)
            {
                Mix       = mix;
                Reduction = reduction;
            }

            private BitCrusher()
                : base(FxType.None)
            {
            }

            public new static BitCrusher FromVox(string data)
            {
                var bitCrusher = new BitCrusher();
                var prop = data.Trim().Split(',').Select(p => p.Trim()).ToArray();
                if (!Enum.TryParse(prop[0], out FxType type) || type != FxType.BitCrusher)
                    return bitCrusher;

                if (prop.Length != 3)
                    return bitCrusher;

                try
                {
                    bitCrusher.Type      = type;
                    bitCrusher.Mix       = float.Parse(prop[1]);
                    bitCrusher.Reduction = int.Parse(prop[2]);
                }
                catch (Exception)
                {
                    bitCrusher.Type = FxType.None;
                }

                return bitCrusher;
            }

            public new static BitCrusher FromKsh(string data)
            {
                var bitCrusher = new BitCrusher();
                var prop = data.Trim().Split(';').Select(p => p.Trim()).ToArray();
                if (!Enum.TryParse(prop[0], out FxType type) || type != FxType.BitCrusher)
                    return bitCrusher;

                int reduction = 12;
                if (prop.Length > 1)
                    reduction = int.TryParse(prop[1], out reduction) ? reduction : 12;

                bitCrusher.Type      = type;
                bitCrusher.Mix       = 100.00f;
                bitCrusher.Reduction = reduction;

                return bitCrusher;
            }

            public new static BitCrusher FromKsh(KshDefinition definition)
            {
                var bitCrusher = new BitCrusher();

                try 
                {
                    definition.GetValue("mix",       out float mix);
                    definition.GetValue("reduction", out int samples);

                    // KSH percentages are normalized (0.0-1.0), VOX expects 0-100 scale
                    bitCrusher.Mix       = mix > 0 ? mix * 100f : 100.00f;
                    bitCrusher.Reduction = samples > 0 ? samples : 12;
                    bitCrusher.Type      = FxType.BitCrusher;
                }
                catch (Exception)
                {
                    bitCrusher.Type = FxType.None;
                }

                return bitCrusher;
            }

            public override string ToString()
            {
                if (Type == FxType.None)
                    return base.ToString();

                return $"{(int)Type},\t{Mix:0.00},\t{Reduction}";
            }

            public override string ToKsh()
            {
                if (Type == FxType.None)
                    return string.Empty;

                return $"BitCrusher;{Reduction}";
            }
        }
    }
}
