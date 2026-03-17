using Microsoft.AspNetCore.Components;

namespace ProffieOS.Workbench.Components;

public partial class ControlButton
{
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter] public bool Active { get; set; }
    [Parameter] public Func<Task>? OnClick { get; set; }

    private async Task HandleClick()
    {
        if (OnClick is null) return;
        await OnClick();
    }
}