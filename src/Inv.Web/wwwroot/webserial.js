let port = null;
let reader = null;
let readAbort = null;
let dotnetRef = null;
let readLoopPromise = null;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Arduino / Official / Atmel SAM-BA */
export const ARDUINO_FILTERS = [
    { usbVendorId: 0x2341 },
    { usbVendorId: 0x2A03 },
    { usbVendorId: 0x03EB },
];

const ARDUINO_VIDS = new Set(ARDUINO_FILTERS.map((f) => f.usbVendorId));
const LAST_PORT_KEY = 'inv.serial.lastArduino';

export function isSupported() {
    return typeof navigator !== 'undefined' && 'serial' in navigator;
}

function portInfo(p) {
    try {
        return p.getInfo?.() ?? {};
    } catch {
        return {};
    }
}

export function isArduinoPort(p) {
    const vid = portInfo(p).usbVendorId;
    return vid != null && ARDUINO_VIDS.has(vid);
}

function rememberPort(p) {
    const info = portInfo(p);
    if (info.usbVendorId == null) return;
    try {
        localStorage.setItem(LAST_PORT_KEY, JSON.stringify({
            usbVendorId: info.usbVendorId,
            usbProductId: info.usbProductId ?? null,
        }));
    } catch { /* private mode */ }
}

function loadLastPort() {
    try {
        return JSON.parse(localStorage.getItem(LAST_PORT_KEY) || 'null');
    } catch {
        return null;
    }
}

function scorePort(p, last) {
    if (!last) return 0;
    const info = portInfo(p);
    if (info.usbVendorId === last.usbVendorId && info.usbProductId === last.usbProductId) return 2;
    if (info.usbVendorId === last.usbVendorId) return 1;
    return 0;
}

/** Tidligere godkendt Arduino-port uden browser-dialog. */
export async function findGrantedArduinoPort({ preferBootloader = false } = {}) {
    if (!isSupported()) return null;
    const ports = (await navigator.serial.getPorts()).filter(isArduinoPort);
    if (ports.length === 0) return null;
    if (ports.length === 1) return ports[0];

    const last = loadLastPort();
    const ranked = [...ports].sort((a, b) => {
        if (preferBootloader) {
            const aBoot = portInfo(a).usbVendorId === 0x03EB ? 1 : 0;
            const bBoot = portInfo(b).usbVendorId === 0x03EB ? 1 : 0;
            if (aBoot !== bBoot) return bBoot - aBoot;
        }
        return scorePort(b, last) - scorePort(a, last);
    });
    return ranked[0];
}

async function openConnectedPort(selected, helper) {
    port = selected;
    await port.open({ baudRate: 9600 });
    try {
        await port.setSignals({ dataTerminalReady: false, requestToSend: false });
        await sleep(120);
        await port.setSignals({ dataTerminalReady: true, requestToSend: true });
    } catch {
        /* nogle porte understøtter ikke signals */
    }
    rememberPort(port);
    dotnetRef = helper;
    readAbort = new AbortController();
    readLoopPromise = readLoop(readAbort.signal);
}

/**
 * Genforbind uden dialog, hvis browseren allerede har givet adgang til Oplà.
 * @returns {'connected'|'none'}
 */
export async function tryReconnect(dotnetHelper) {
    if (!isSupported()) {
        throw new Error('Web Serial kræver Chrome eller Edge.');
    }
    if (port) return 'connected';
    const granted = await findGrantedArduinoPort();
    if (!granted) return 'none';
    await disconnect();
    await openConnectedPort(granted, dotnetHelper);
    return 'connected';
}

/**
 * Forbind: genbrug godkendt port hvis muligt, ellers USB-dialog (kun første gang / ny enhed).
 * @param {{ forcePicker?: boolean }} [options]
 */
export async function connect(dotnetHelper, options = {}) {
    if (!isSupported()) {
        throw new Error('Web Serial kræver Chrome eller Edge.');
    }
    await disconnect();

    let selected = null;
    if (!options.forcePicker) {
        selected = await findGrantedArduinoPort();
    }
    if (!selected) {
        try {
            selected = await navigator.serial.requestPort({ filters: ARDUINO_FILTERS });
        } catch (err) {
            if (err?.name === 'NotFoundError') {
                throw new Error('Ingen port valgt. Vælg Oplà / MKR WiFi 1010 i dialogen.');
            }
            throw err;
        }
    }

    await openConnectedPort(selected, dotnetHelper);
}

async function readLoop(signal) {
    const decoder = new TextDecoder();
    let buffer = '';
    try {
        while (port?.readable && !signal.aborted) {
            reader = port.readable.getReader();
            try {
                while (true) {
                    const { value, done } = await reader.read();
                    if (done) break;
                    buffer += decoder.decode(value, { stream: true });
                    buffer = buffer.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
                    let newline;
                    while ((newline = buffer.indexOf('\n')) >= 0) {
                        const line = buffer.slice(0, newline);
                        buffer = buffer.slice(newline + 1);
                        if (line && dotnetRef) {
                            await dotnetRef.invokeMethodAsync('OnSerialLine', line);
                        }
                    }
                }
            } finally {
                reader.releaseLock();
                reader = null;
            }
        }
    } catch (error) {
        if (!signal.aborted && dotnetRef) {
            await dotnetRef.invokeMethodAsync('OnSerialError', error?.message || 'Serialforbindelsen blev afbrudt.');
        }
    }
}

export async function writeLine(text) {
    if (!port?.writable) {
        throw new Error('Ingen aktiv serialforbindelse.');
    }
    const writer = port.writable.getWriter();
    try {
        await writer.write(new TextEncoder().encode(`${text}\n`));
    } finally {
        writer.releaseLock();
    }
}

export async function disconnect() {
    readAbort?.abort();
    readAbort = null;
    try {
        await reader?.cancel();
    } catch {
        /* ignore */
    }
    reader = null;
    try {
        await readLoopPromise;
    } catch {
        /* ignore */
    }
    readLoopPromise = null;
    if (port) {
        try {
            await port.close();
        } catch {
            /* ignore */
        }
    }
    port = null;
    if (dotnetRef) {
        try {
            dotnetRef.dispose();
        } catch {
            /* ignore */
        }
    }
    dotnetRef = null;
}
