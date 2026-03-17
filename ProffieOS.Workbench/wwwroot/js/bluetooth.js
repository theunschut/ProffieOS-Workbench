window.BluetoothInterop = (() => {
    let _device = null;
    let _tx = null;
    let _pw = null;
    let _server = null;
    let _dotnetRef = null;
    let _savedProfiles = null;

    async function connectInternal(dotnetRef, profiles) {
        _dotnetRef = dotnetRef;
        _server = await _device.gatt.connect();

        let service = null;
        let txUuid, pwUuid, statusUuid, rxUuid;

        for (const profile of profiles) {
            try {
                service = await _server.getPrimaryService(profile.serviceUuid);
                rxUuid = profile.rxUuid;
                txUuid = profile.txUuid;
                pwUuid = profile.pwUuid;
                statusUuid = profile.statusUuid;
                break;
            } catch {
                service = null;
            }
        }

        if (!service) throw new Error('No matching BLE service found');

        const rx = await service.getCharacteristic(rxUuid);
        _tx = await service.getCharacteristic(txUuid);

        if (pwUuid) {
            try { _pw = await service.getCharacteristic(pwUuid); } catch { _pw = null; }
        }

        if (statusUuid) {
            try {
                const statusChar = await service.getCharacteristic(statusUuid);
                await statusChar.startNotifications();
                statusChar.addEventListener('characteristicvaluechanged', (e) => {
                    const data = new TextDecoder().decode(e.target.value);
                    dotnetRef.invokeMethodAsync('OnStatusReceived', data);
                });
            } catch { /* status char optional */ }
        }

        await rx.startNotifications();
        rx.addEventListener('characteristicvaluechanged', (e) => {
            const data = new TextDecoder().decode(e.target.value);
            dotnetRef.invokeMethodAsync('OnDataReceived', data);
        });

        _device.addEventListener('gattserverdisconnected', () => {
            dotnetRef.invokeMethodAsync('OnDisconnected');
        });
    }

    return {
        async requestDevice(filters) {
            _device = await navigator.bluetooth.requestDevice({ filters });
            return _device.name ?? 'Unknown Device';
        },

        async connect(dotnetRef, profiles) {
            _savedProfiles = profiles;
            await connectInternal(dotnetRef, profiles);
        },

        async reconnect(dotnetRef) {
            if (!_device || !_savedProfiles) throw new Error('No device to reconnect to');
            await connectInternal(dotnetRef, _savedProfiles);
        },

        async writeChunk(bytes) {
            if (!_tx) throw new Error('Not connected');
            await _tx.writeValue(new Uint8Array(bytes));
        },

        async sendPassword(password) {
            if (!_pw) return;
            await _pw.writeValue(new TextEncoder().encode(password));
        }
    };
})();
