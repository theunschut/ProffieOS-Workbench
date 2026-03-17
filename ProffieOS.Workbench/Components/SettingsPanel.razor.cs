using MudBlazor;

namespace ProffieOS.Workbench.Components;

public partial class SettingsPanel : IDisposable
{
    protected override void OnInitialized()
    {
        State.StateChanged += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private async Task SaveSd(bool val)
    {
        try { await State.SaveSdAsync(val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveBrightness(int percent)
    {
        try { await State.SaveBrightnessAsync(percent); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveClashThreshold(float val)
    {
        try { await State.SaveClashThresholdAsync(val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveBladeLength(int blade, int length)
    {
        try { await State.SaveBladeLengthAsync(blade, length); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveBoolGesture(int idx, bool val)
    {
        try { await State.SaveBoolGestureAsync(idx, val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveIntGesture(int idx, int val)
    {
        try { await State.SaveIntGestureAsync(idx, val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    public void Dispose() => State.StateChanged -= OnStateChanged;
}