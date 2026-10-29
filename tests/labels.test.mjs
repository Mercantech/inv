import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { writeFile } from 'node:fs/promises';
import { composeEquipmentLabel, equipmentUrl, qrForEquipment } from '../src/Inv.Web/printing/compose.mjs';
const require = createRequire(new URL('../src/Inv.Web/printing/package.json', import.meta.url));
const { createCanvas, loadImage } = require('@napi-rs/canvas');
const jsQR = require('jsqr');
const base = 'https://inv.mercantec.tech/';
const canvas = () => createCanvas(1, 1);

test('printed label preview decodes to the stable short equipment URL, including long names', async () => {
    for (const id of [109, 99999]) {
        const expectedQr = qrForEquipment(base, id).qr;
        const label = composeEquipmentLabel(id, 'Arduino Oplà med en meget lang beskrivelse '.repeat(8), base, canvas);
        assert.equal(label.image.width, 32);
        assert.equal(label.image.data.length, label.image.width * label.image.height * 4);
        assert.ok(label.qrSize <= 25);
        assert.ok(label.qrY >= 3);
        assert.ok(label.qrY + label.qrSize < 32);
        const image = await loadImage(label.previewUrl);
        const bitmap = createCanvas(image.width, image.height); const ctx = bitmap.getContext('2d'); ctx.drawImage(image, 0, 0);
        const pixels = ctx.getImageData(0, 0, image.width, image.height);
        const decoded = jsQR(pixels.data, image.width, image.height);
        assert.equal(decoded?.data, 'HTTPS://INV.MERCANTEC.TECH/E/' + id);
        assert.equal(new URL(decoded.data).search, '');
        // Ensure the head-aligned print bitmap has exactly the same QR pixels as the preview input.
        for (let row = 0; row < label.qrSize; row++) for (let col = 0; col < label.qrSize; col++) {
            const x = label.qrX + col * 3, y = label.qrY + row;
            const offset = (x * 32 + 31 - y) * 4;
            assert.equal(label.image.data[offset], expectedQr.modules.get(row, col) ? 0 : 255);
            assert.equal(label.image.data[offset + 3], 255);
        }
        if (process.env.LABEL_PREVIEW_PATH && id === 109) await writeFile(process.env.LABEL_PREVIEW_PATH, bitmap.toBuffer('image/png'));
    }
});

test('rejects arbitrary QR payloads, token-bearing URLs, invalid IDs and oversized codes', () => {
    for (const url of ['javascript:alert(1)', 'https://user:secret@app.test/', 'https://app.test/?token=secret', 'https://app.test/#token', 'https://app.test/udstyr/'])
        assert.throws(() => equipmentUrl(url, 109));
    for (const id of [0, -1, NaN, 1.5, '109']) assert.throws(() => equipmentUrl(base, id));
    assert.throws(() => composeEquipmentLabel(109, 'Test', 'https://' + 'a'.repeat(60) + '.example/', canvas), /for lang/);
});

let session, device, writes, requestCount, failWrite;
beforeEach(async () => {
    globalThis.document = { createElement: () => canvas() };
    globalThis.isSecureContext = true;
    writes = []; requestCount = 0; failWrite = false;
    const rx = new EventTarget(); rx.startNotifications = async () => {}; rx.stopNotifications = async () => {};
    const tx = { writeValueWithoutResponse: async data => {
        writes.push(new Uint8Array(data));
        if (failWrite) throw new Error('Bluetooth write failed');
        rx.value = new DataView(new Uint8Array([0x1b, 0x52, 1]).buffer);
        rx.dispatchEvent(new Event('characteristicvaluechanged'));
    } };
    const service = { uuid: 'be3dd650-2b3d-42f1-99c1-f0f749dd0678', getCharacteristic: async id => id.startsWith('be3dd651') ? tx : rx };
    device = new EventTarget(); device.name = 'Letratag Test';
    device.gatt = { connected: false, connect: async () => { device.gatt.connected = true; return device.gatt; },
        getPrimaryServices: async () => [service], disconnect: () => { device.gatt.connected = false; device.dispatchEvent(new Event('gattserverdisconnected')); } };
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { bluetooth: { requestDevice: async () => { requestCount++; return device; } } } });
    session = await import('../src/Inv.Web/printing/session.mjs?' + Math.random());
});

test('chooser starts directly on click and driver prints once; wrong equipment and concurrent jobs are rejected', async () => {
    const connecting = session.connect(); assert.equal(requestCount, 1); await connecting;
    const label = session.prepare(109, 'Arduino Oplà', base);
    await assert.rejects(session.print(110, label.url), /Forhåndsvis/);
    await assert.rejects(session.print(109, 'https://other.test/'), /Forhåndsvis/);
    const printing = session.print(109, label.url);
    await assert.rejects(session.print(109, label.url), /allerede/);
    assert.match(await printing, /printeren har svaret/);
    assert.ok(writes.length > 1);
    assert.ok(writes.every(w => w.length <= 247));
    await session.disconnect(); assert.equal(session.connection(), null);
    await assert.rejects(session.print(109, label.url), /Forbind/);
});

test('Bluetooth failures are not retried and unsupported browsers cannot connect', async () => {
    await session.connect(); const label = session.prepare(109, 'Test', base); failWrite = true;
    await assert.rejects(session.print(109, label.url), /Bluetooth write failed/);
    assert.equal(writes.length, 1);
    globalThis.isSecureContext = false;
    assert.equal(session.isSupported(), false);
    await assert.rejects(session.connect(), /Chrome eller Edge/);
});
