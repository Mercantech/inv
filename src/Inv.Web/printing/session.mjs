// Port of Mercantech/Dymo2026 web/src/printer/session.ts (d21e82d).
import { DEVICES, LT_PAPER_WHITE, LetraTagPrinter } from '@thermal-label/letratag-web';
import { WebBluetoothTransport } from '@thermal-label/transport/web';
import { composeEquipmentLabel } from './compose.mjs';
const SERVICE = 'be3dd650-2b3d-42f1-99c1-f0f749dd0678';
let printer = null, deviceName = null, prepared = null, operation = false;
export function isSupported() { return !!globalThis.isSecureContext && !!globalThis.navigator?.bluetooth; }
export function connection() { return printer?.connected ? deviceName : null; }
export function prepare(shortId, name, baseUrl) {
    prepared = null;
    const result = composeEquipmentLabel(shortId, name, baseUrl);
    prepared = { shortId, ...result };
    return { previewUrl: result.previewUrl, url: result.url, shortId };
}
async function bind(device) {
    if (!device.gatt) throw new Error('Printeren har ingen Bluetooth GATT-forbindelse.');
    const server = device.gatt.connected ? device.gatt : await device.gatt.connect();
    const services = await server.getPrimaryServices();
    const matched = services.find(s => s.uuid.startsWith('be3dd650-'));
    if (!matched) throw new Error('Den valgte enhed er ikke en LetraTag LT-200B.');
    const tail = matched.uuid.slice(8);
    const tx = await matched.getCharacteristic('be3dd651' + tail);
    const rx = await matched.getCharacteristic('be3dd652' + tail);
    await rx.startNotifications();
    printer = new LetraTagPrinter(DEVICES.LT_200B, WebBluetoothTransport.fromCharacteristics(device, tx, rx,
        DEVICES.LT_200B.transports['bluetooth-gatt']?.mtu ?? 247));
    deviceName = device.name || 'DYMO LT-200B';
    return deviceName;
}
export async function connect() {
    if (!isSupported()) throw new Error('Brug Chrome eller Edge med Bluetooth på HTTPS eller localhost.');
    if (operation) throw new Error('Vent på den igangværende printerhandling.');
    operation = true;
    try {
        // Invoke the chooser before any awaited operation so the browser keeps the click gesture.
        const selected = navigator.bluetooth.requestDevice({ filters: [
            { namePrefix: 'Letratag ' }, { namePrefix: 'LetraTag ' }, { namePrefix: 'DYMO LT-200B' }
        ], optionalServices: [SERVICE] });
        const device = await selected;
        if (printer?.connected) await printer.close();
        printer = null; deviceName = null;
        try { return await bind(device); }
        catch (error) { device.gatt?.disconnect(); throw error; }
    } finally { operation = false; }
}
export async function disconnect() {
    if (operation) throw new Error('Vent på den igangværende printerhandling.');
    try { if (printer) await printer.close(); }
    finally { printer = null; deviceName = null; }
}
export async function print(shortId, expectedUrl) {
    if (operation) throw new Error('Et print er allerede i gang.');
    if (!printer?.connected) throw new Error('Forbind først din DYMO LT-200B.');
    if (!prepared || prepared.shortId !== shortId || prepared.url !== expectedUrl) throw new Error('Forhåndsvis labelen igen, før du printer.');
    operation = true;
    let notification = false, replay = true;
    const unsubscribe = printer.onStatus(() => { if (!replay) notification = true; }); replay = false;
    try {
        // No automatic retry: a failed Bluetooth reply does not mean the label was not printed.
        await printer.print(prepared.image, LT_PAPER_WHITE, { copies: 1, autoCut: true, rotate: 0 });
        return notification ? 'Labelen er sendt, og printeren har svaret. Kontrollér label og QR-scanning.'
            : 'Labelen er sendt, men printerkvitteringen mangler. Kontrollér printeren før et nyt forsøg.';
    } finally { unsubscribe(); operation = false; }
}
