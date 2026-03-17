using Microsoft.AspNetCore.Components;
using MudBlazor;
using ProffieOS.Workbench.Helpers;
using ProffieOS.Workbench.Models;

namespace ProffieOS.Workbench.Components;

public partial class StyleEditor
{
    [Parameter, EditorRequired] public int Blade { get; set; }
    [Parameter, EditorRequired] public string StyleValue { get; set; } = "";
    [Parameter, EditorRequired] public int PresetIndex { get; set; }

    private string _selectedStyle = "";
    private NamedStyle? _namedStyle;
    private string[] _argValues = [];

    protected override void OnParametersSet()
    {
        ParseStyleValue(StyleValue);
    }

    private void ParseStyleValue(string value)
    {
        var parts = value.Split(' ');
        var style = parts[0];
        var args = parts.Skip(1).ToArray();

        if (style == "builtin" && parts.Length >= 3)
        {
            var candidate = $"builtin {parts[1]} {parts[2]}";
            if (State.NamedStyles.ContainsKey(candidate))
            {
                style = candidate;
                args = parts.Skip(3).ToArray();
            }
        }

        _selectedStyle = style;
        _namedStyle = State.NamedStyles.GetValueOrDefault(style);
        InitArgValues(args);
    }

    private void InitArgValues(string[] args)
    {
        if (_namedStyle is null) { _argValues = []; return; }
        _argValues = new string[_namedStyle.Args.Count];
        for (var i = 0; i < _namedStyle.Args.Count; i++)
        {
            var raw = i < args.Length ? args[i] : _namedStyle.Args[i].DefaultValue;
            _argValues[i] = _namedStyle.Args[i].Type == "COLOR"
                ? ColorConverter.From16BitColor(raw)
                : raw;
        }
    }

    private void OnStyleSelected(string newStyle)
    {
        _selectedStyle = newStyle;
        _namedStyle = State.NamedStyles.GetValueOrDefault(newStyle);
        if (_namedStyle is not null) InitArgValues([]);
        _ = SaveStyle();
    }

    private void HandleColorChange(int idx, ChangeEventArgs e)
    {
        _argValues[idx] = e.Value?.ToString() ?? "#000000";
        _ = SaveStyle();
    }

    private void HandleIntChange(int idx, string val)
    {
        _argValues[idx] = val;
        _ = SaveStyle();
    }

    private string GetArgLabel(StyleArgument arg, int index)
    {
        if (!string.IsNullOrWhiteSpace(arg.Description)) return arg.Description;
        return arg.Type == "COLOR" ? $"color {index + 1}" : $"value {index + 1}";
    }

    private async Task SaveStyle()
    {
        if (_namedStyle is null) return;
        var parts = new List<string> { _selectedStyle };
        for (var i = 0; i < _namedStyle.Args.Count; i++)
        {
            var val = i < _argValues.Length ? _argValues[i] : _namedStyle.Args[i].DefaultValue;
            if (_namedStyle.Args[i].Type == "COLOR" && val.StartsWith('#'))
                val = ColorConverter.To16BitColor(val);
            parts.Add(val);
        }
        try { await State.SaveStyleAsync(PresetIndex, Blade, string.Join(" ", parts)); }
        catch (Exception ex) { Snackbar.Add($"Save style failed: {ex.Message}", Severity.Error); }
    }
}