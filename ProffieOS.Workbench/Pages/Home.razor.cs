using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using ProffieOS.Workbench.Components;
using ProffieOS.Workbench.Services;

namespace ProffieOS.Workbench.Pages;

public partial class Home
{
    private bool _busy;
    private bool _showPassword;
    private string _password = "";
    private string _connectingVia = "";

    protected override async Task OnInitializedAsync()
    {
        await Connection.InitAsync();
        if (Connection.State == ConnectionState.Connected)
            Nav.NavigateTo("/dashboard");
    }

    private void TogglePasswordField()
    {
        if (!_showPassword)
        {
            _showPassword = true;
        }
        else
        {
            _ = ConnectBleAsync();
        }
    }

    private async Task OnPasswordKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await ConnectBleAsync();
    }

    private async Task ConnectBleAsync()
    {
        _busy = true;
        _connectingVia = "ble";
        try
        {
            await Connection.ConnectBleAsync(_password);
            await State.StartAsync();
            Nav.NavigateTo("/dashboard");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"BLE connection failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _busy = false;
            _connectingVia = "";
        }
    }

    private async Task ConnectUsbAsync()
    {
        _busy = true;
        _connectingVia = "usb";
        try
        {
            await Connection.ConnectUsbAsync();
            await State.StartAsync();
            Nav.NavigateTo("/dashboard");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"USB connection failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _busy = false;
            _connectingVia = "";
        }
    }

    private async Task ConnectKnownAsync(KnownDevicesPanel.KnownDeviceItem device)
    {
        _busy = true;
        _connectingVia = device.type;
        try
        {
            if (device.type == "usb")
                await Connection.ConnectKnownUsbAsync(device.index);
            else
                await Connection.ConnectKnownBleAsync(device.index, _password);

            await State.StartAsync();
            Nav.NavigateTo("/dashboard");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Connection failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _busy = false;
            _connectingVia = "";
        }
    }
}