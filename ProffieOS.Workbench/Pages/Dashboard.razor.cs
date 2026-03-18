using MudBlazor;
using ProffieOS.Workbench.Services;

namespace ProffieOS.Workbench.Pages;

public partial class Dashboard : IDisposable
{
    private enum CenterView
    {
        Reticle,
        Edit,
        Settings
    }

    private CenterView _centerView = CenterView.Reticle;
    private bool _isSettingsLoading;

    private bool IsEditView => _centerView == CenterView.Edit;
    private bool IsSettingsView => _centerView == CenterView.Settings;
    private bool HasCenterOverlay => _centerView != CenterView.Reticle;

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

    private async Task ToggleEditView()
    {
        if (_centerView == CenterView.Edit)
        {
            _centerView = CenterView.Reticle;
        }
        else
        {
            _centerView = CenterView.Edit;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleSettingsView()
    {
        if (_centerView == CenterView.Settings)
        {
            _centerView = CenterView.Reticle;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _centerView = CenterView.Settings;
        await EnsureSettingsLoadedAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task EnsureSettingsLoadedAsync()
    {
        if (State.SettingsLoaded)
        {
            _isSettingsLoading = false;
            return;
        }

        _isSettingsLoading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            await State.LoadSettingsAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load settings: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isSettingsLoading = false;
        }
    }

    private async Task OnVolumeChanged(int value)
    {
        try { await State.SetVolumeAsync(value); }
        catch (Exception ex) { Snackbar.Add($"Volume error: {ex.Message}", Severity.Warning); }
    }

    public void Dispose()
    {
        State.StateChanged -= OnStateChanged;
        Connection.StateChanged -= OnConnectionStateChanged;
        Commands.OnError -= OnCommandError;
    }
}
