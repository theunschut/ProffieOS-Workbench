using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace ProffieOS.Workbench.Components;

public partial class ControlsLayout : IDisposable
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        State.StateChanged += OnStateChanged;
        Connection.StateChanged += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private async Task TurnOn()
    {
        try { await State.TurnOnAsync(); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task TurnOff()
    {
        try { await State.TurnOffAsync(); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task Cmd(string cmd)
    {
        try { await State.SendControlAsync(cmd); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task StopTrack()
    {
        try { await State.StopTrackAsync(); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    public void Dispose()
    {
        State.StateChanged -= OnStateChanged;
        Connection.StateChanged -= OnStateChanged;
    }
}
