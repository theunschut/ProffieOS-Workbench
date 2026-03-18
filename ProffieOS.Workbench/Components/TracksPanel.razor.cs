using MudBlazor;

namespace ProffieOS.Workbench.Components;

public partial class TracksPanel : IDisposable
{
    protected override void OnInitialized() => State.StateChanged += OnStateChanged;

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private async Task PlayTrack(string track)
    {
        try { await State.PlayTrackAsync(track); }
        catch (Exception ex) { Snackbar.Add($"Play failed: {ex.Message}", Severity.Error); }
    }

    public void Dispose() => State.StateChanged -= OnStateChanged;
}