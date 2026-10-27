import { SAMBAFlasher, FLASH_OFFSET } from './samba-flasher.js?v=1';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const ARDUINO_FILTERS = [
    { usbVendorId: 0x2341 },
    { usbVendorId: 0x2A03 },
];

async function touch1200(port) {
    await port.open({ baudRate: 1200 });
    try {
        await port.setSignals({ dataTerminalReady: false, requestToSend: false });
        await sleep(120);
    } catch { /* ignore */ }
    try {
        await port.close();
    } catch { /* device may reset immediately */ }
}

/**
 * Flash hosted firmware onto MKR WiFi 1010 via Web Serial + SAM-BA.
 * @param {any} dotnetHelper DotNetObjectReference with OnFlashLog / OnFlashProgress
 * @param {string} [firmwareUrl]
 */
export async function flashFirmware(dotnetHelper, firmwareUrl = './firmware/opla-hardwaretest.bin') {
    const log = async (message) => {
        if (dotnetHelper) await dotnetHelper.invokeMethodAsync('OnFlashLog', message);
    };
    const progress = async (done, total) => {
        if (dotnetHelper) await dotnetHelper.invokeMethodAsync('OnFlashProgress', done, total);
    };

    await log('Henter firmware…');
    const response = await fetch(firmwareUrl, { cache: 'no-cache' });
    if (!response.ok) {
        throw new Error(`Firmware kunne ikke hentes (${response.status}).`);
    }
    const bin = new Uint8Array(await response.arrayBuffer());
    if (bin.length < 256 || bin.length > 262144) {
        throw new Error(`Ugyldig firmware-størrelse (${bin.length} bytes).`);
    }
    await log(`Firmware: ${bin.length} bytes (skrives ved 0x${FLASH_OFFSET.toString(16)}).`);

    await log('Vælg Oplà / MKR WiFi 1010 i dialogboksen…');
    let port = await navigator.serial.requestPort({ filters: ARDUINO_FILTERS });

    await log('Sender boardet i bootloader (1200 baud)…');
    await touch1200(port);
    await sleep(1500);

    await log('Vælg boardet igen (bootloader-port)…');
    port = await navigator.serial.requestPort({ filters: ARDUINO_FILTERS });

    const flasher = new SAMBAFlasher({
        log: (m) => { void log(m); },
        progress: (d, t) => { void progress(d, t); },
    });
    try {
        await flasher.open(port, 115200);
        await flasher.connect();
        await flasher.eraseApp();
        await flasher.writeFirmware(bin, FLASH_OFFSET);
        await flasher.verifyFirmware(bin, FLASH_OFFSET);
        await flasher.resetDevice();
        await log('Firmware installeret. Vent et øjeblik, og tryk derefter Forbind board.');
    } finally {
        await flasher.close();
    }
}

export function isSupported() {
    return typeof navigator !== 'undefined' && 'serial' in navigator;
}
