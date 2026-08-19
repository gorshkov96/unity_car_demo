using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// The project's colour palette, in one place.
    ///
    /// Low-poly art has no textures and no material detail to carry a scene, so colour does
    /// all of the work: the picture reads only if hues are saturated and if neighbouring
    /// surfaces differ in both value and temperature. The earlier scheme was a set of greys
    /// separated by a few percent of brightness, which is why everything looked washed out
    /// however the lighting was set.
    ///
    /// The scheme here is warm ground against a cool sky, with saturated accents reserved for
    /// the things the player must react to — ramps and hazards.
    /// </summary>
    public static class Palette
    {
        // --- Descent: the road surface and its surroundings -------------------------
        /// <summary>Road. Deep and slightly violet, so warm scenery pops against it.</summary>
        public static readonly Color Road = Hex("515A7E");

        /// <summary>Lane markings. Near-white with a warm cast, never pure white.</summary>
        public static readonly Color Marking = Hex("F5EFE0");

        /// <summary>Barrier walls: darker than the road so the track edge is unmistakable.</summary>
        public static readonly Color Wall = Hex("2B2F4E");

        /// <summary>Rock faces along the track. Warm sand against the cool road.</summary>
        public static readonly Color Rock = Hex("D9B98A");

        /// <summary>Distant peaks: cool and desaturated, so they read as far away.</summary>
        public static readonly Color Peak = Hex("A8BBD9");

        /// <summary>Snow-capped peaks.</summary>
        public static readonly Color PeakSnow = Hex("E8EEF7");

        // --- Things that matter to the driver ---------------------------------------
        /// <summary>Ramps. The most saturated colour in the scene, seen from furthest away.</summary>
        public static readonly Color Ramp = Hex("FFB020");

        /// <summary>Banked ramps and hazard markings.</summary>
        public static readonly Color Hazard = Hex("FF4757");

        /// <summary>Moving obstacles: cold turquoise, the complement of the warm ramps.</summary>
        public static readonly Color Moving = Hex("2EC4B6");

        /// <summary>Static blocks: mid-value neutral, deliberately quieter than hazards.</summary>
        public static readonly Color Obstacle = Hex("9A8FB5");

        /// <summary>Loose crates.</summary>
        public static readonly Color Crate = Hex("E07A3F");

        /// <summary>Structural accents: posts, collars, buttresses.</summary>
        public static readonly Color Accent = Hex("5B5288");

        // --- Car ---------------------------------------------------------------------
        public static readonly Color CarBody = Hex("E23E3E");
        public static readonly Color CarTrim = Hex("23212E");
        public static readonly Color CarGlass = Hex("15233D");
        public static readonly Color CarRim = Hex("D8DCE6");
        public static readonly Color Tyre = Hex("18181D");
        public static readonly Color Headlight = Hex("FFF6DC");
        public static readonly Color Taillight = Hex("FF3B30");

        // --- Ring map -----------------------------------------------------------------
        public static readonly Color Ground = Hex("6E8A5A");
        public static readonly Color RingRoad = Hex("3A3F52");
        public static readonly Color Building = Hex("D6C6A8");

        // --- Sky and light ---------------------------------------------------------------
        public static readonly Color SkyTint = Hex("4FA8E8");
        public static readonly Color SkyGround = Hex("2A2E48");
        public static readonly Color SunLight = Hex("FFEFD0");

        /// <summary>Ambient from above: cool, so shadows read blue against warm sunlight.</summary>
        public static readonly Color AmbientSky = Hex("7FB6E8");

        public static readonly Color AmbientEquator = Hex("8E8FB4");
        public static readonly Color AmbientGround = Hex("55496B");
        public static readonly Color Fog = Hex("9FB4CE");

        /// <summary>Parses "RRGGBB" so the palette reads the way a designer writes it.</summary>
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out Color color) ? color : Color.magenta;
        }

        /// <summary>Emission colour for lights: HDR values above 1 so bloom picks them up.</summary>
        public static Color Emissive(Color color, float intensity)
        {
            return new Color(color.r * intensity, color.g * intensity, color.b * intensity);
        }
    }
}
