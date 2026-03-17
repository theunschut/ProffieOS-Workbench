namespace ProffieOS.Workbench.Helpers;

/// <summary>
/// Converts between the saber's 16-bit linear color format and standard HTML hex colors,
/// with gamma correction (gamma 2.2).
/// </summary>
public static class ColorConverter
{
    public static string From16BitColor(string val)
    {
        var parts = val.Split(',');
        if (parts.Length < 3) return "#000000";
        return "#" + From16BitLinear(parts[0]) + From16BitLinear(parts[1]) + From16BitLinear(parts[2]);
    }

    private static string From16BitLinear(string val)
    {
        if (!int.TryParse(val.Trim(), out var v)) return "00";
        var result = (int)Math.Round(Math.Pow(v / 65535.0, 1.0 / 2.2) * 255.0);
        result = Math.Clamp(result, 0, 255);
        return result.ToString("x2");
    }

    public static string To16BitColor(string hex)
    {
        if (hex.Length < 7) return "0,0,0";
        return To16BitLinear(hex.Substring(1, 2)) + "," +
               To16BitLinear(hex.Substring(3, 2)) + "," +
               To16BitLinear(hex.Substring(5, 2));
    }

    private static int To16BitLinear(string hex)
    {
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return 0;
        return (int)Math.Round(Math.Pow(v / 255.0, 2.2) * 65535);
    }
}
