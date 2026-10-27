let port = null;
let reader = null;
let readAbort = null;
let dotnetRef = null;
let readLoopPromise = null;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export function isSupported() {
    return typeof navigator !== 'undefined' && 'serial' in navigator;
}

export async function connect(dotnetHelper) {
    if (!isSupported()) {
        throw new Error('Web Serial kræver Chrome eller Edge.');
    }
    await disconnect();
    port = await navigator.serial.requestPort();
    await port.open({ baudRate: 9600 });
    // Mange Arduino-boards resetter når DTR toggles — så vi får et frisk READY.
    try {
        await port.setSignals({ dataTerminalReady: false, requestToSend: false });
        await sleep(120);
        await port.setSignals({ dataTerminalReady: true, requestToSend: true });
    } catch {
        /* nogle porte understøtter ikke signals */
    }
    dotnetRef = dotnetHelper;
    readAbort = new AbortController();
    readLoopPromise = readLoop(readAbort.signal);
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
