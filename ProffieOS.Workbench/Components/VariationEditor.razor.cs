using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace ProffieOS.Workbench.Components;

public partial class VariationEditor
{
    [Parameter, EditorRequired] public int PresetIndex { get; set; }

    private int _value;

    protected override void OnParametersSet()
    {
        if (PresetIndex >= 0 && PresetIndex < State.Presets.Count)
        {
            if (int.TryParse(State.Presets[PresetIndex].Variation, out var v))
                _value = v;
        }
    }

    private async Task OnSliderChanged(int val)
    {
        _value = val;
        await Save();
    }

    private async Task OnInputChanged(int val)
    {
        _value = val & 32767;
        await Save();
    }

    private async Task Decrement()
    {
        _value = (_value - 1) & 32767;
        await Save();
    }

    private async Task Increment()
    {
        _value = (_value + 1) & 32767;
        await Save();
    }

    private async Task Save()
    {
        try { await State.SaveVariationAsync(PresetIndex, _value); }
        catch (Exception ex) { Snackbar.Add($"Variation error: {ex.Message}", Severity.Error); }
    }
}