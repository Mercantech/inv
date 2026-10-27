import { SAMBAFlasher, FLASH_OFFSET } from './samba-flasher.js';
import { ARDUINO_FILTERS, findGrantedArduinoPort } from './webserial.js';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Held between prepareFlash (1. klik) and completeFlash (2. klik). */
let pendingBin = null;

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

function portError(err, step) {
    const msg = err?.message ?? String(err);
    if (/user gesture|permission request/i.test(msg) || err?.name === 'SecurityError') {
        return new Error(
            'Browseren kræver et friskt klik for USB-dialogen. Tryk knappen igen (anden gang: «Vælg bootloader-port»).');
    }
    if (/no port/i.test(msg) || err?.name === 'NotFoundError') {
        return new Error(
            step === 'bootloader'
                ? 'Ingen bootloader-port valgt. Dobbelttryk Reset på boardet, og vælg den nye COM-port i dialogen (ofte en anden end før).'
                : 'Ingen port valgt. Vælg Oplà / MKR WiFi 1010 i browserens USB-dialog (Chrome eller Edge).');
    }
    return err instanceof Error ? err : new Error(msg);
}

async function requestArduinoPort(step) {
    try {
        // Genbrug tidligere godkendt port når muligt — undgår USB-dialog.
        const granted = await findGrantedArduinoPort({ preferBootloader: step === 'bootloader' });
        if (granted) return granted;

        // Bootloader-enumeration kan skifte VID — dialog uden filter, så porten altid vises.
        if (step === 'bootloader') {
            return await navigator.serial.requestPort();
        }
        return await navigator.serial.requestPort({ filters: ARDUINO_FILTERS });
    } catch (err) {
        throw portError(err, step);
    }
}

function makeLog(dotnetHelper) {
    return async (message) => {
        if (dotnetHelper) await dotnetHelper.invokeMethodAsync('OnFlashLog', message);
    };
}

function makeProgress(dotnetHelper) {
    return async (done, total) => {
        if (dotnetHelper) await dotnetHelper.invokeMethodAsync('OnFlashProgress', done, total);
    };
}

/**
 * Trin 1 (skal kaldes direkte fra et klik): vælg app-port, 1200-baud reset, hent firmware.
 * Browseren tillader kun requestPort inde i et user gesture — derfor er trin 2 et nyt klik.
 * Vigtigt: requestPort før andre awaits (inkl. DotNet-log), ellers mister Chrome gesturen.
 */
export async function prepareFlash(dotnetHelper, firmwareUrl = './firmware/opla-hardwaretest.bin') {
    const log = makeLog(dotnetHelper);
    pendingBin = null;

    const port = await requestArduinoPort('app');
    await log('Port valgt. Henter firmware…');

    const response = await fetch(firmwareUrl, { cache: 'no-cache' });
    if (!response.ok) {
        throw new Error(`Firmware kunne ikke hentes (${response.status}). Genindlæs siden, eller tjek at deploy indeholder firmware-bin.`);
    }
    const bin = new Uint8Array(await response.arrayBuffer());
    if (bin.length < 256 || bin.length > 262144) {
        throw new Error(`Ugyldig firmware-størrelse (${bin.length} bytes).`);
    }
    await log(`Firmware: ${bin.length} bytes (skrives ved 0x${FLASH_OFFSET.toString(16)}).`);

    await log('Sender boardet i bootloader (1200 baud)…');
    await touch1200(port);
    await sleep(1500);

    pendingBin = bin;
    await log('Boardet er i bootloader. Tryk «Vælg bootloader-port» (dobbelttryk Reset hvis porten mangler).');
    return true;
}

/**
 * Trin 2 (nyt klik): vælg bootloader-COM og skriv firmware.
 * requestPort skal være første await — ingen DotNet-kald før dialogen.
 */
export async function completeFlash(dotnetHelper) {
    const log = makeLog(dotnetHelper);
    const progress = makeProgress(dotnetHelper);

    if (!pendingBin) {
        throw new Error('Ingen ventende firmware. Start med «Installér firmware» igen.');
    }
    const bin = pendingBin;

    const port = await requestArduinoPort('bootloader');
    await log('Bootloader-port valgt. Skriver firmware…');

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
        pendingBin = null;
        await log('Firmware installeret. Vent et øjeblik, og tryk derefter Forbind board.');
    } catch (err) {
        // Behold pendingBin, så «Vælg bootloader-port» kan prøves igen uden ny prepare.
        throw err;
    } finally {
        await flasher.close();
    }
}

export function cancelFlash() {
    pendingBin = null;
}

export function hasPendingFlash() {
    return pendingBin !== null;
}

/** @deprecated Brug prepareFlash + completeFlash (to klik pga. browser user-gesture). */
export async function flashFirmware(dotnetHelper, firmwareUrl) {
    await prepareFlash(dotnetHelper, firmwareUrl);
    await completeFlash(dotnetHelper);
}

export function isSupported() {
    return typeof navigator !== 'undefined' && 'serial' in navigator;
}
