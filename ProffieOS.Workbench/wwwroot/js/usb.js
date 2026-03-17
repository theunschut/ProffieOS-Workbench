window.UsbInterop = (() => {
    let _device = null;
    let _endpointOut = -1;
    let _endpointIn = -1;
    let _interfaceNumber = -1;
    let _reading = false;

    return {
        async requestDevice(filters) {
            const devices = await navigator.usb.getDevices();
            _device = devices.length === 1 ? devices[0]
                : await navigator.usb.requestDevice({ filters });
            return _device.productName ?? 'ProffieOS Device';
        },

        async connect(dotnetRef) {
            await _device.open();

            if (_device.configuration === null)
                await _device.selectConfiguration(1);

            _endpointOut = -1;
            _endpointIn = -1;
            _interfaceNumber = -1;

            for (const iface of _device.configuration.interfaces) {
                for (const alt of iface.alternates) {
                    if (alt.interfaceClass === 0xff) {
                        _interfaceNumber = iface.interfaceNumber;
                        for (const ep of alt.endpoints) {
                            if (ep.direction === 'out') _endpointOut = ep.endpointNumber;
                            if (ep.direction === 'in')  _endpointIn  = ep.endpointNumber;
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

            navigator.usb.ondisconnect = () => dotnetRef.invokeMethodAsync('OnDisconnected');

            // Prime the connection
            await _device.transferOut(_endpointOut, new TextEncoder().encode('\n\n'));

            // Start receive loop
            _reading = true;
            (async () => {
                while (_reading) {
                    try {
                        const result = await _device.transferIn(_endpointIn, 64);
                        const data = new TextDecoder().decode(result.data);
                        dotnetRef.invokeMethodAsync('OnDataReceived', data);
                    } catch (e) {
                        if (_reading) {
                            _reading = false;
                            dotnetRef.invokeMethodAsync('OnDisconnected');
                        }
                        break;
                    }
                }
            })();
        },

        async write(bytes) {
            if (_endpointOut === -1) throw new Error('USB not connected');
            await _device.transferOut(_endpointOut, new Uint8Array(bytes));
        },

        stop() { _reading = false; }
    };
})();
