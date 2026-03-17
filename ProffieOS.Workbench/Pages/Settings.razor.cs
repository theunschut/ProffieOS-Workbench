using MudBlazor;
using ProffieOS.Workbench.Services;

namespace ProffieOS.Workbench.Pages;

public partial class Settings : IDisposable
{
    private bool _isLoading;

    protected override void OnInitialized()
    {
        if (Connection.State == ConnectionState.Disconnected)
        {
            Nav.NavigateTo("");
            return;
        }

        State.StateChanged += OnStateChanged;
        Connection.StateChanged += OnConnectionStateChanged;

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        if (State.SettingsLoaded)
        {
            _isLoading = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _isLoading = true;
        await InvokeAsync(StateHasChanged);

        var loadTask = State.LoadSettingsAsync();
        var completed = await Task.WhenAny(loadTask, Task.Delay(2000));

        _isLoading = false;
        await InvokeAsync(StateHasChanged);

        if (completed == loadTask)
        {
            try
            {
                await loadTask;
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to load settings: {ex.Message}", Severity.Error);
            }
        }
        else
        {
            _ = ObserveLoadTask(loadTask);
        }
    }

    private async Task ObserveLoadTask(Task loadTask)
    {
        try
        {
            await loadTask;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load settings: {ex.Message}", Severity.Error);
        }
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private void OnConnectionStateChanged()
    {
        _ = InvokeAsync(async () =>
        {
            if (Connection.State == ConnectionState.Connected && !State.SettingsLoaded && !_isLoading)
                await LoadSettingsAsync();

            StateHasChanged();
        });
    }

    public void Dispose()
    {
        State.StateChanged -= OnStateChanged;
        Connection.StateChanged -= OnConnectionStateChanged;
    }
}