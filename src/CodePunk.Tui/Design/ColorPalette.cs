using Spectre.Console;

namespace CodePunk.Tui.Design;

/// <summary>
/// CodePunk TUI Color Palette
///
/// All UI elements should use colors from this palette to maintain consistent branding.
/// </summary>
public static class ColorPalette
{
    /// <summary>
    /// Primary brand colors - use these for the main UI elements
    /// </summary>
    public static class Brand
    {
        /// <summary>Dark Blue-Green (#264653) - Use for primary headers and emphasis</summary>
        public static readonly Color DarkTeal = new Color(0x26, 0x46, 0x53);

        /// <summary>Teal (#2A9D8F) - Use for interactive elements and accents</summary>
        public static readonly Color Teal = new Color(0x2A, 0x9D, 0x8F);

        /// <summary>Yellow/Gold (#E9C46A) - Use for highlights and warnings</summary>
        public static readonly Color Gold = new Color(0xE9, 0xC4, 0x6A);

        /// <summary>Orange (#F4A261) - Use for secondary accents</summary>
        public static readonly Color Orange = new Color(0xF4, 0xA2, 0x61);

        /// <summary>Coral/Red-Orange (#E76F51) - Use for errors and important alerts</summary>
        public static readonly Color Coral = new Color(0xE7, 0x6F, 0x51);
    }

    /// <summary>
    /// Logo gradient - array of colors for use in the CodePunk logo
    /// Colors transition from dark teal through to coral
    /// </summary>
    public static readonly Color[] LogoGradient = new[]
    {
        Brand.DarkTeal,
        Brand.Teal,
        Brand.Gold,
        Brand.Orange,
        Brand.Coral
    };
}
