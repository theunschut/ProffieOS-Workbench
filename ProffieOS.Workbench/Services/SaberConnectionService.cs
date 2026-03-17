using Microsoft.JSInterop;
using ProffieOS.Workbench.Models;

namespace ProffieOS.Workbench.Services;

public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }

/// <summary>
/// Manages BLE and USB connection lifecycle.
/// Discovers available transports, connects, handles reconnect.
/// </summary>
public class SaberConnectionService(IJSRuntime js, SaberCommandService commands)
{
    private static readonly UartProfile[] BleProfiles =
    [
        new("713d0000-389c-f637-b1d7-91b361ae7678",
            "713d0002-389c-f637-b1d7-91b361ae7678",
            "713d0003-389c-f637-b1d7-91b361ae7678",
            "713d0004-389c-f637-b1d7-91b361ae7678",
            "713d0005-389c-f637-b1d7-91b361ae7678"),
        new("6e400001-b5a3-f393-e0a9-e50e24dcca9e",
            "6e400002-b5a3-f393-e0a9-e50e24dcca9e",
            "6e400003-b5a3-f393-e0a9-e50e24dcca9e"),
        new("49535343-fe7d-4ae5-8fa9-9fafd205e455",
            "49535343-8841-43f4-a8d4-ecbe34729bb3",
            "49535343-1e4d-4bd9-ba61-23c647249616"),
        new("0000fff0-0000-1000-8000-00805f9b34fb",
            "0000fff1-0000-1000-8000-00805f9b34fb",
            "0000fff2-0000-1000-8000-00805f9b34fb"),
        new("0000ffe0-0000-1000-8000-00805f9b34fb",
            "0000ffe1-0000-1000-8000-00805f9b34fb",
            "0000ffe1-0000-1000-8000-00805f9b34fb"),
        new("0000fefb-0000-1000-8000-00805f9b34fb",
            "00000001-0000-1000-8000-008025000000",
            "00000002-0000-1000-8000-008025000000"),
        new("569a1101-b87f-490c-92cb-11ba5ea5167c",
            "569a2001-b87f-490c-92cb-11ba5ea5167c",
            "569a2000-b87f-490c-92cb-11ba5ea5167c"),
    ];

    private bool _isBle;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public bool BluetoothAvailable { get; private set; }
    public bool UsbAvailable { get; private set; }
    public string? ConnectedDeviceName { get; private set; }

    public event Action? StateChanged;

    public async Task InitAsync()
    {
        BluetoothAvailable = await js.InvokeAsync<bool>("eval", "typeof navigator.bluetooth !== 'undefined'");
        UsbAvailable = await js.InvokeAsync<bool>("eval", "typeof navigator.usb !== 'undefined'");
        commands.OnDisconnectedAsync += HandleDisconnect;
    }

    public async Task ConnectBleAsync(string? password = null)
    {
        SetState(ConnectionState.Connecting);
        try
        {
            var filters = BleProfiles.Select(p => new { services = new[] { p.ServiceUuid } }).ToArray();
            ConnectedDeviceName = await js.InvokeAsync<string>("BluetoothInterop.requestDevice", filters);

            var profiles = BleProfiles.Select(p => new
            {
                serviceUuid = p.ServiceUuid,
                rxUuid = p.RxUuid,
                txUuid = p.TxUuid,
                pwUuid = p.PwUuid,
                statusUuid = p.StatusUuid
            }).ToArray();

            await js.InvokeVoidAsync("BluetoothInterop.connect", commands.DotNetRef, profiles);

            commands.SendBytesAsync = bytes => js.InvokeVoidAsync("BluetoothInterop.writeChunk", bytes).AsTask();
            _isBle = true;

            if (!string.IsNullOrEmpty(password))
            {
                var status = await commands.SendPasswordAndWait(password,
                    pw => js.InvokeVoidAsync("BluetoothInterop.sendPassword", pw).AsTask());
                if (status != "OK")
                    throw new Exception("Wrong password");
            }

            commands.MarkConnected();
            SetState(ConnectionState.Connected);
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            throw;
        }
    }

    public async Task ConnectUsbAsync()
    {
        SetState(ConnectionState.Connecting);
        try
        {
            var filters = new[] { new { vendorId = 0x1209, productId = 0x6668 } };
            ConnectedDeviceName = await js.InvokeAsync<string>("UsbInterop.requestDevice", filters);
            await js.InvokeVoidAsync("UsbInterop.connect", commands.DotNetRef);

            commands.SendBytesAsync = bytes => js.InvokeVoidAsync("UsbInterop.write", bytes).AsTask();
            _isBle = false;

            commands.MarkConnected();
            SetState(ConnectionState.Connected);
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            throw;
        }
    }

    public async Task ReconnectBleAsync()
    {
        SetState(ConnectionState.Reconnecting);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(5000);
            try
            {
                await js.InvokeVoidAsync("BluetoothInterop.reconnect", commands.DotNetRef);
                commands.MarkConnected();
                SetState(ConnectionState.Connected);
                return;
            }
            catch { /* keep retrying */ }
        }
        SetState(ConnectionState.Disconnected);
    }

    private async Task HandleDisconnect()
    {
        if (_isBle)
        {
            await ReconnectBleAsync();
        }
        else
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
