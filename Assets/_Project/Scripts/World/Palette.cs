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
    /// The scheme sticks to colours things actually are — asphalt grey, stone, road-sign
    /// yellow and red — and gets its punch from contrast and saturation rather than from
    /// inventing hues. Violet roads and pink ramps read as a debug palette, not as a world.
    /// </summary>
    public static class Palette
    {
        // --- Descent: the road surface and its surroundings -------------------------
        /// <summary>Road. Deep and slightly violet, so warm scenery pops against it.</summary>
        public static readonly Color Road = Hex("55595F");

        /// <summary>Lane markings. Near-white with a warm cast, never pure white.</summary>
        public static readonly Color Marking = Hex("F5EFE0");

        /// <summary>Barrier walls: darker than the road so the track edge is unmistakable.</summary>
        public static readonly Color Wall = Hex("3D4148");

        /// <summary>Rock faces along the track. Warm sand against the cool road.</summary>
        public static readonly Color Rock = Hex("C2B49A");

        /// <summary>Distant peaks: cool and desaturated, so they read as far away.</summary>
        public static readonly Color Peak = Hex("9FB0C4");

        /// <summary>Snow-capped peaks.</summary>
        public static readonly Color PeakSnow = Hex("E8EEF7");

        // --- Things that matter to the driver ---------------------------------------
        /// <summary>Ramps. The most saturated colour in the scene, seen from furthest away.</summary>
        public static readonly Color Ramp = Hex("F5A31A");

        /// <summary>Banked ramps and hazard markings.</summary>
        public static readonly Color Hazard = Hex("D62828");

        /// <summary>Moving obstacles: cold turquoise, the complement of the warm ramps.</summary>
        public static readonly Color Moving = Hex("2A7FD4");

        /// <summary>Static blocks: mid-value neutral, deliberately quieter than hazards.</summary>
        public static readonly Color Obstacle = Hex("AFB3B8");

        /// <summary>Loose crates.</summary>
        public static readonly Color Crate = Hex("C87F35");

        /// <summary>Structural accents: posts, collars, buttresses.</summary>
        public static readonly Color Accent = Hex("6B6259");

        // --- Car ---------------------------------------------------------------------
        public static readonly Color CarBody = Hex("D93B3B");
        public static readonly Color CarTrim = Hex("23212E");
        public static readonly Color CarGlass = Hex("15233D");
        public static readonly Color CarRim = Hex("D8DCE6");
        public static readonly Color Tyre = Hex("18181D");
        public static readonly Color Headlight = Hex("FFF6DC");
        public static readonly Color Taillight = Hex("FF3B30");

        // --- Ring map -----------------------------------------------------------------
        public static readonly Color Ground = Hex("74965C");
        public static readonly Color RingRoad = Hex("4B4F55");
        public static readonly Color Building = Hex("D6C6A8");

        // --- Sky and light ---------------------------------------------------------------
        // The sky's lower half is what fills the screen below the horizon. Leaving it dark
        // draws a heavy band across the frame and hides everything standing against it, so it
        // is kept close to the fog colour: the distance then fades out instead of ending.
        public static readonly Color SkyTint = Hex("7FAAD4");
        public static readonly Color SkyGround = Hex("A9B6C9");
        public static readonly Color SunLight = Hex("FFEFD0");

        /// <summary>Ambient from above: cool, so shadows read blue against warm sunlight.</summary>
        public static readonly Color AmbientSky = Hex("A8C6E0");

        public static readonly Color AmbientEquator = Hex("8D9099");
        public static readonly Color AmbientGround = Hex("4E4C48");
        public static readonly Color Fog = Hex("AEBCCE");

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
