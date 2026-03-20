window.BluetoothInterop = (() => {
    let _device = null;
    let _tx = null;
    let _pw = null;
    let _server = null;
    let _dotnetRef = null;
    let _savedProfiles = null;
    let _rx = null;
    let _statusChar = null;
    let _rxHandler = null;
    let _statusHandler = null;
    let _disconnectHandler = null;
    let _connectSeq = 0;

    const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));

    function deviceLabel(device = _device) {
        if (!device) return 'no-device';
        return device.name || 'BLE Device';
    }

    function logInfo(message, extra) { console.info('[BluetoothInterop]', message, extra ?? ''); }
    function logWarn(message, extra) { console.warn('[BluetoothInterop]', message, extra ?? ''); }
    function logError(message, extra) { console.error('[BluetoothInterop]', message, extra ?? ''); }

    async function detachHandlers() {
        try {
            if (_rx && _rxHandler)
                _rx.removeEventListener('characteristicvaluechanged', _rxHandler);
        } catch { }

        try {
            if (_statusChar && _statusHandler)
                _statusChar.removeEventListener('characteristicvaluechanged', _statusHandler);
        } catch { }

        try {
            if (_device && _disconnectHandler)
                _device.removeEventListener('gattserverdisconnected', _disconnectHandler);
        } catch { }

        _rxHandler = null;
        _statusHandler = null;
        _disconnectHandler = null;
    }

    async function stopNotifications() {
        try {
            if (_rx)
                await _rx.stopNotifications();
        } catch { }

        try {
            if (_statusChar)
                await _statusChar.stopNotifications();
        } catch { }
    }

    async function resetConnectionState() {
        await stopNotifications();
        await detachHandlers();
        _tx = null;
        _pw = null;
        _server = null;
        _rx = null;
        _statusChar = null;
    }

    async function connectInternal(dotnetRef, profiles) {
        _dotnetRef = dotnetRef;
        const seq = ++_connectSeq;

        logInfo('Starting BLE connect/reconnect flow', { seq, device: deviceLabel() });
        await resetConnectionState();

        if (_device?.gatt?.connected) {
            try { _device.gatt.disconnect(); } catch { }
        }

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

        _rx = await service.getCharacteristic(rxUuid);
        _tx = await service.getCharacteristic(txUuid);

        if (pwUuid) {
            try { _pw = await service.getCharacteristic(pwUuid); } catch { _pw = null; }
        }

        if (statusUuid) {
            try {
                _statusChar = await service.getCharacteristic(statusUuid);
                await _statusChar.startNotifications();
                _statusHandler = (e) => {
                    if (seq !== _connectSeq) return;
                    const data = new TextDecoder().decode(e.target.value);
                    dotnetRef.invokeMethodAsync('OnStatusReceived', data);
                };
                _statusChar.addEventListener('characteristicvaluechanged', _statusHandler);
            } catch {
                _statusChar = null;
            }
        }

        await _rx.startNotifications();
        _rxHandler = (e) => {
            if (seq !== _connectSeq) return;
            const data = new TextDecoder().decode(e.target.value);
            dotnetRef.invokeMethodAsync('OnDataReceived', data);
        };
        _rx.addEventListener('characteristicvaluechanged', _rxHandler);

        _disconnectHandler = () => {
            if (seq !== _connectSeq) return;
            logWarn('BLE disconnect event received', { seq, device: deviceLabel() });
            resetConnectionState().catch(() => { });
            dotnetRef.invokeMethodAsync('OnDisconnected');
        };
        _device.addEventListener('gattserverdisconnected', _disconnectHandler);

        logInfo('BLE connected', { seq, device: deviceLabel(), txUuid, rxUuid, statusUuid, hasPw: !!pwUuid });
    }

    return {
        async requestDevice(filters) {
            logInfo('Requesting BLE device', filters);
            _device = await navigator.bluetooth.requestDevice({ filters });
            logInfo('BLE device selected', deviceLabel());
            return _device.name ?? 'Unknown Device';
        },

        async connect(dotnetRef, profiles) {
            _savedProfiles = profiles;
            await connectInternal(dotnetRef, profiles);
        },

        async reconnect(dotnetRef) {
            if (!_device || !_savedProfiles) throw new Error('No device to reconnect to');
            logInfo('Reconnect requested', deviceLabel());
            await connectInternal(dotnetRef, _savedProfiles);
        },

        async writeChunk(bytes) {
            if (!_tx) throw new Error('Not connected');

            let lastError = null;
            for (let attempt = 0; attempt < 3; attempt++) {
                try {
                    await _tx.writeValue(new Uint8Array(bytes));
                    return;
                } catch (e) {
                    lastError = e;
                    logWarn('BLE writeChunk failed', { attempt: attempt + 1, byteLength: bytes?.length ?? 0, error: e });
                    await delay(20 * (attempt + 1));
                }
            }

            logError('BLE writeChunk failed after retries', lastError);
            throw lastError ?? new Error('BLE write failed');
        },

        async sendPassword(password) {
            if (!_pw) return;
            try {
                await _pw.writeValue(new TextEncoder().encode(password));
                logInfo('BLE password sent');
            } catch (e) {
                logWarn('BLE password send failed', e);
                throw e;
            }
        },

        async selectKnownDevice(index) {
            if (!navigator.bluetooth.getDevices) throw new Error('getDevices not supported');
            const devices = await navigator.bluetooth.getDevices();
            if (index >= devices.length) throw new Error('Device not found');
            _device = devices[index];
            logInfo('Known BLE device selected', deviceLabel());
            return _device.name ?? 'BLE Device';
        },

        async getKnownDevices() {
            if (typeof navigator.bluetooth === 'undefined' || !navigator.bluetooth.getDevices) return [];
            try {
                const devices = await navigator.bluetooth.getDevices();
                return devices.map((d, i) => ({ name: d.name || 'BLE Device', type: 'ble', index: i }));
            } catch {
                return [];
            }
        }
    };
})();
