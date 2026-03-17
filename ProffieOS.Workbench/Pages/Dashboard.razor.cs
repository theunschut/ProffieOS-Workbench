using MudBlazor;
using ProffieOS.Workbench.Services;

namespace ProffieOS.Workbench.Pages;

public partial class Dashboard : IDisposable
{
    protected override void OnInitialized()
    {
        if (Connection.State == ConnectionState.Disconnected)
        {
            Nav.NavigateTo("");
            return;
        }

        State.StateChanged += OnStateChanged;
        Connection.StateChanged += OnConnectionStateChanged;
        Commands.OnError += OnCommandError;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private void OnConnectionStateChanged() => InvokeAsync(StateHasChanged);

    private void OnCommandError(string msg)
        => InvokeAsync(() => Snackbar.Add(msg, Severity.Error));

    private async Task OnVolumeChanged(int value)
    {
        try
        {
            await State.SetVolumeAsync(value);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Volume error: {ex.Message}", Severity.Warning);
        }
    }

    public void Dispose()
    {
        State.StateChanged -= OnStateChanged;
        Connection.StateChanged -= OnConnectionStateChanged;
        Commands.OnError -= OnCommandError;
    }
}