using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ProffieOS.Workbench.Components;

public partial class KnownDevicesPanel
{
    public record KnownDeviceItem(string name, string type, int index);

    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback<KnownDeviceItem> OnConnect { get; set; }

    private List<KnownDeviceItem> _devices = [];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            var usb = await JS.InvokeAsync<KnownDeviceItem[]>("UsbInterop.getKnownDevices");
            var ble = await JS.InvokeAsync<KnownDeviceItem[]>("BluetoothInterop.getKnownDevices");
            _devices = [.. usb, .. ble];
            StateHasChanged();
        }
        catch { }
    }
}