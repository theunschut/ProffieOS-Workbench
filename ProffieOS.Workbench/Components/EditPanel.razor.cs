using MudBlazor;
using ProffieOS.Workbench.Models;

namespace ProffieOS.Workbench.Components;

public partial class EditPanel : IDisposable
{
    private Preset? _preset;
    private string _name = "";
    private string _font = "";
    private string _track = "";

    protected override void OnInitialized()
    {
        State.StateChanged += OnStateChanged;
        Refresh();
    }

    private void OnStateChanged()
    {
        Refresh();
        InvokeAsync(StateHasChanged);
    }

    private void Refresh()
    {
        var idx = State.CurrentPresetIndex;
        if (idx < 0 || idx >= State.Presets.Count)
        {
            _preset = null;
            return;
        }

        _preset = State.Presets[idx];
        _name = _preset.Name;
        _font = _preset.Font;
        _track = _preset.Track;
    }

    private async Task SaveName()
    {
        try { await State.SaveNameAsync(State.CurrentPresetIndex, _name); }
        catch (Exception ex) { Snackbar.Add($"Save name failed: {ex.Message}", Severity.Error); }
    }

    private async Task SaveFont(string font)
    {
        _font = font;
        try { await State.SaveFontAsync(State.CurrentPresetIndex, font); }
        catch (Exception ex) { Snackbar.Add($"Save font failed: {ex.Message}", Severity.Error); }
    }

    private async Task SaveTrack(string track)
    {
        _track = track;
        try { await State.SaveTrackAsync(State.CurrentPresetIndex, track); }
        catch (Exception ex) { Snackbar.Add($"Save track failed: {ex.Message}", Severity.Error); }
    }

    public void Dispose() => State.StateChanged -= OnStateChanged;
}