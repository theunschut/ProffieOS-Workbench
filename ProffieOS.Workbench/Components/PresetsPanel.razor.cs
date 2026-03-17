using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace ProffieOS.Workbench.Components;

public partial class PresetsPanel : IDisposable
{
    [Parameter] public object Mode { get; set; } = "Normal";

    protected override void OnInitialized() => State.StateChanged += OnStateChanged;

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private bool IsEditMode => Mode?.ToString() == "Edit";

    private int _dragging = -1;

    private async Task SelectPreset(int index)
    {
        try { await State.SetPresetAsync(index); }
        catch (Exception ex) { Snackbar.Add($"Failed to set preset: {ex.Message}", Severity.Error); }
    }

    private void StartDrag(int index) => _dragging = index;

    private async Task Drop(int index)
    {
        if (_dragging < 0 || _dragging == index) return;
        try
        {
            await State.MovePresetAsync(_dragging, index);
            _dragging = -1;
        }
        catch (Exception ex) { Snackbar.Add($"Move failed: {ex.Message}", Severity.Error); }
    }

    private async Task DropDelete()
    {
        if (_dragging < 0) return;
        await DoDelete(_dragging);
        _dragging = -1;
    }

    private void EditPreset(int index) { }

    private async Task AddPreset()
    {
        try { await State.AddPresetAsync(); }
        catch (Exception ex) { Snackbar.Add($"Failed to add preset: {ex.Message}", Severity.Error); }
    }

    private bool _confirmDelete;

    private async Task ConfirmDelete()
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            Snackbar.Add("Click trash again to confirm deletion", Severity.Warning);
            await Task.Delay(3000);
            _confirmDelete = false;
        }
        else
        {
            _confirmDelete = false;
            await DoDelete(State.CurrentPresetIndex);
        }
    }

    private async Task DoDelete(int index)
    {
        try { await State.DeletePresetAsync(index); }
        catch (Exception ex) { Snackbar.Add($"Delete failed: {ex.Message}", Severity.Error); }
    }

    public void Dispose() => State.StateChanged -= OnStateChanged;
}