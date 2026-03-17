using ProffieOS.Workbench.Services;

namespace ProffieOS.Workbench.Pages;

public partial class EditPreset : IDisposable
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
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private void OnConnectionStateChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        State.StateChanged -= OnStateChanged;
        Connection.StateChanged -= OnConnectionStateChanged;
    }
}