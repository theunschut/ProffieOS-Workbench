namespace ProffieOS.Workbench.Models;

public class Preset
{
    public string Name { get; set; } = "";
    public string Font { get; set; } = "";
    public string Track { get; set; } = "";
    public string Variation { get; set; } = "0";
    public Dictionary<int, string> Styles { get; set; } = new();

    public static Preset FromDictionary(Dictionary<string, string> data)
    {
        var preset = new Preset
        {
            Name = data.GetValueOrDefault("NAME", ""),
            Font = data.GetValueOrDefault("FONT", ""),
            Track = data.GetValueOrDefault("TRACK", ""),
            Variation = data.GetValueOrDefault("VARIATION", "0"),
        };

        for (var i = 1; data.ContainsKey($"STYLE{i}"); i++)
            preset.Styles[i] = data[$"STYLE{i}"];

        return preset;
    }
}
