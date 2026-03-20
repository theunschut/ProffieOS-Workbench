window.UsbInterop = (() => {
    let _device = null;
    let _endpointOut = -1;
    let _endpointIn = -1;
    let _interfaceNumber = -1;
    let _reading = false;
    let _dotnetRef = null;
    let _disconnectHandler = null;
    let _disconnectNotified = false;
    let _requestedFilters = [];

    const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));

    function deviceLabel(device = _device) {
        if (!device) return 'no-device';
        const name = device.productName || 'USB Device';
        const vid = device.vendorId?.toString(16)?.padStart(4, '0') ?? '----';
        const pid = device.productId?.toString(16)?.padStart(4, '0') ?? '----';
        return `${name} [${vid}:${pid}]`;
    }

    function logInfo(message, extra) { console.info('[UsbInterop]', message, extra ?? ''); }
    function logWarn(message, extra) { console.warn('[UsbInterop]', message, extra ?? ''); }
    function logError(message, extra) { console.error('[UsbInterop]', message, extra ?? ''); }

    function sameDevice(a, b) {
        if (!a || !b) return false;
        if (a === b) return true;
        if (a.serialNumber && b.serialNumber)
            return a.vendorId === b.vendorId && a.productId === b.productId && a.serialNumber === b.serialNumber;
        return a.vendorId === b.vendorId && a.productId === b.productId;
    }

    function deviceMatchesFilter(device, filter) {
        if (!device || !filter) return false;
        if (filter.vendorId !== undefined && device.vendorId !== filter.vendorId) return false;
        if (filter.productId !== undefined && device.productId !== filter.productId) return false;
        if (filter.classCode !== undefined && device.deviceClass !== filter.classCode) return false;
        if (filter.subclassCode !== undefined && device.deviceSubclass !== filter.subclassCode) return false;
        if (filter.protocolCode !== undefined && device.deviceProtocol !== filter.protocolCode) return false;
        return true;
    }

    function deviceMatchesAnyFilter(device, filters) {
        if (!filters || filters.length === 0) return true;
        return filters.some(f => deviceMatchesFilter(device, f));
    }

    async function cleanupConnectionState(closeDevice = false) {
        _reading = false;

        if (_disconnectHandler) {
            try { navigator.usb.removeEventListener('disconnect', _disconnectHandler); } catch { }
            _disconnectHandler = null;
        }

        if (_device?.opened && _interfaceNumber !== -1) {
            try { await _device.releaseInterface(_interfaceNumber); } catch { }
        }

        if (closeDevice && _device?.opened) {
            try {
                await _device.close();
                logInfo('USB device closed', deviceLabel());
            } catch (e) {
                logWarn('USB device close failed', e);
            }
        }

        _endpointOut = -1;
        _endpointIn = -1;
        _interfaceNumber = -1;
    }

    function notifyDisconnected() {
        if (_disconnectNotified) return;
        _disconnectNotified = true;
        _reading = false;
        logWarn('Notifying .NET disconnect', deviceLabel());
        if (_dotnetRef)
            _dotnetRef.invokeMethodAsync('OnDisconnected');
    }

    async function resolveCurrentDevice() {
        if (!_device) throw new Error('No USB device selected');

        const devices = await navigator.usb.getDevices();
        if (!devices.some(d => d === _device)) {
            const replacement = devices.find(d => sameDevice(d, _device) && deviceMatchesAnyFilter(d, _requestedFilters));
            if (!replacement)
                throw new Error('Device not found');
            _device = replacement;
            logInfo('Re-bound to authorized USB device', deviceLabel());
        }

        if (!deviceMatchesAnyFilter(_device, _requestedFilters))
            throw new Error('Selected USB device does not match required filter');

        return _device;
    }

    async function connectInternal(dotnetRef) {
        _dotnetRef = dotnetRef;
        _disconnectNotified = false;

        logInfo('Starting USB connect/reconnect flow');
        await cleanupConnectionState(true);
        await resolveCurrentDevice();

        await _device.open();

        if (_device.configuration === null)
            await _device.selectConfiguration(1);

        for (const iface of _device.configuration.interfaces) {
            for (const alt of iface.alternates) {
                if (alt.interfaceClass === 0xff) {
                    _interfaceNumber = iface.interfaceNumber;
                    for (const ep of alt.endpoints) {
                        if (ep.direction === 'out') _endpointOut = ep.endpointNumber;
                        if (ep.direction === 'in') _endpointIn = ep.endpointNumber;
                    }
                }
            }
        }

        if (_endpointOut === -1 || _endpointIn === -1)
            throw new Error('No WebUSB interface found on device');

        await _device.claimInterface(_interfaceNumber);
        await _device.selectAlternateInterface(_interfaceNumber, 0);
        await _device.controlTransferOut({
            requestType: 'class',
            recipient: 'interface',
            request: 0x22,
            value: 0x01,
            index: _interfaceNumber
        });

        logInfo('USB connected', { device: deviceLabel(), endpointOut: _endpointOut, endpointIn: _endpointIn, interfaceNumber: _interfaceNumber });

        _disconnectHandler = (event) => {
            if (!sameDevice(event.device, _device)) return;
            logWarn('Browser USB disconnect event received', deviceLabel(event.device));
            notifyDisconnected();
            cleanupConnectionState(true).catch(() => { });
        };
        navigator.usb.addEventListener('disconnect', _disconnectHandler);

        await _device.transferOut(_endpointOut, new TextEncoder().encode('\n\n'));

        _reading = true;
        (async () => {
            let readErrors = 0;
            while (_reading) {
                try {
                    const result = await _device.transferIn(_endpointIn, 64);
                    if (!_reading) break;

                    if (result.status !== 'ok') {
                        readErrors++;
                        logWarn(`USB read status '${result.status}'`, { attempt: readErrors, device: deviceLabel() });
                        if (readErrors <= 3) {
                            await delay(50);
                            continue;
                        }
                        throw new Error(`USB read failed: ${result.status}`);
                    }

                    readErrors = 0;
                    const data = new TextDecoder().decode(result.data);
                    if (data.length > 0)
                        _dotnetRef.invokeMethodAsync('OnDataReceived', data);
                } catch (e) {
                    if (!_reading) break;
                    readErrors++;
                    logWarn('USB read exception', { attempt: readErrors, error: e });
                    if (readErrors <= 3) {
                        await delay(100);
                        continue;
                    }

                    logError('USB read loop terminating after repeated failures', e);
                    notifyDisconnected();
                    await cleanupConnectionState(true);
                    break;
                }
            }
        })();
    }

    return {
        async requestDevice(filters) {
            _requestedFilters = Array.from(filters);
            logInfo('Requesting USB device', _requestedFilters);
            const devices = await navigator.usb.getDevices();
            const matching = devices.filter(d => deviceMatchesAnyFilter(d, _requestedFilters));

            _device = matching.length === 1
                ? matching[0]
                : await navigator.usb.requestDevice({ filters: _requestedFilters });

            logInfo('USB device selected', deviceLabel());
            return _device.productName ?? 'ProffieOS Device';
        },

        async connect(dotnetRef) {
            await connectInternal(dotnetRef);
        },

        async reconnect(dotnetRef) {
            logInfo('Reconnect requested');
            await connectInternal(dotnetRef);
        },

        async write(bytes) {
            if (_endpointOut === -1 || !_device) throw new Error('USB not connected');

            let lastError = null;
            for (let attempt = 0; attempt < 3; attempt++) {
                try {
                    const result = await _device.transferOut(_endpointOut, new Uint8Array(bytes));
                    if (result.status === 'ok') return;
                    lastError = new Error(`USB write failed: ${result.status}`);
                    logWarn(`USB write status '${result.status}'`, { attempt: attempt + 1, byteLength: bytes?.length ?? 0 });
                } catch (e) {
                    lastError = e;
                    logWarn('USB write exception', { attempt: attempt + 1, error: e });
                }

                await delay(25 * (attempt + 1));
            }

            logError('USB write failed after retries; disconnecting', lastError);
            notifyDisconnected();
            await cleanupConnectionState(true);
            throw lastError ?? new Error('USB write failed');
        },

        stop() {
            logInfo('USB stop requested');
            cleanupConnectionState(true).catch(() => { });
        },

        async selectKnownDevice(index) {
            const devices = await navigator.usb.getDevices();
            if (index >= devices.length) throw new Error('Device not found');
            _device = devices[index];
            return _device.productName ?? 'ProffieOS Device';
        },

        async getKnownDevices() {
            if (typeof navigator.usb === 'undefined') return [];
            try {
                const devices = await navigator.usb.getDevices();
                return devices.map((d, i) => ({ name: d.productName || 'USB Device', type: 'usb', index: i }));
            } catch {
                return [];
            }
        }
    };
})();
