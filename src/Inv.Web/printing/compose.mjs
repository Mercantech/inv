// Adapted from Mercantech/Dymo2026, d21e82d9c59d6e58fb89d6054f1f163e19f6bc2a.
import QRCode from 'qrcode';
export const HEAD_ROWS = 32;
export const ASPECT = 2.55;
export function equipmentUrl(baseUrl, shortId) {
    if (!Number.isSafeInteger(shortId) || shortId < 1) throw new Error('Ugyldigt udstyrs-ID.');
    const base = new URL(baseUrl);
    if (!['http:', 'https:'].includes(base.protocol) || base.username || base.password || base.search || base.hash || base.pathname !== '/')
        throw new Error('Labeladressen skal være appens HTTP(S)-origin uden sti, loginoplysninger eller parametre.');
    // Uppercase is deliberate: QR alphanumeric encoding is much smaller than byte encoding.
    // The /e route and DNS are case-insensitive. No token or arbitrary QR content is accepted.
    return new URL('e/' + shortId, base).href.toUpperCase();
}
export function qrForEquipment(baseUrl, shortId) {
    const url = equipmentUrl(baseUrl, shortId);
    const qr = QRCode.create([{ data: url, mode: 'alphanumeric' }], { errorCorrectionLevel: 'L' });
    if (qr.modules.size > 25) throw new Error('Appens adresse er for lang til en læsbar QR-kode på LT-200B. Brug en kortere offentlig adresse.');
    return { url, qr };
}
export function composeEquipmentLabel(shortId, _name, baseUrl, createCanvas = () => document.createElement('canvas')) {
    const { url, qr } = qrForEquipment(baseUrl, shortId);
    const canvas = createCanvas();
    const measure = canvas.getContext('2d');
    if (!measure) throw new Error('Browseren kan ikke lave labelens forhåndsvisning.');
    measure.font = '700 24px sans-serif';
    const idWidth = Math.ceil(measure.measureText(String(shortId)).width * ASPECT);
    const textWidth = idWidth;
    const padding = 20, gap = 15, moduleFeed = 3, quiet = 4;
    const qrWidth = (qr.modules.size + quiet * 2) * moduleFeed;
    canvas.width = Math.max(197, padding * 2 + textWidth + gap + qrWidth);
    canvas.height = HEAD_ROWS;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = '#000'; ctx.imageSmoothingEnabled = false;
    ctx.save(); ctx.translate(padding, 0); ctx.scale(ASPECT, 1); ctx.textBaseline = 'middle';
    ctx.font = '700 24px sans-serif'; ctx.fillText(String(shortId), 0, HEAD_ROWS / 2);
    ctx.restore();
    const qrX = padding + textWidth + gap + quiet * moduleFeed;
    const qrY = Math.floor((HEAD_ROWS - qr.modules.size) / 2);
    for (let row = 0; row < qr.modules.size; row++)
        for (let col = 0; col < qr.modules.size; col++)
            if (qr.modules.get(row, col)) ctx.fillRect(qrX + col * moduleFeed, qrY + row, moduleFeed, 1);
    const landscape = ctx.getImageData(0, 0, canvas.width, HEAD_ROWS);
    // Same head-aligned orientation as Dymo2026; preserve every QR module before encoding.
    const data = new Uint8Array(canvas.width * HEAD_ROWS * 4);
    for (let y = 0; y < HEAD_ROWS; y++) for (let x = 0; x < canvas.width; x++) {
        const from = (y * canvas.width + x) * 4;
        const to = (x * HEAD_ROWS + HEAD_ROWS - 1 - y) * 4;
        data.set(landscape.data.subarray(from, from + 4), to);
    }
    const preview = createCanvas();
    preview.width = Math.round(canvas.width / ASPECT * 5); preview.height = HEAD_ROWS * 5;
    const pctx = preview.getContext('2d'); pctx.imageSmoothingEnabled = false;
    pctx.drawImage(canvas, 0, 0, preview.width, preview.height);
    return { image: { width: HEAD_ROWS, height: canvas.width, data }, previewUrl: preview.toDataURL('image/png'), url,
        qrSize: qr.modules.size, qrX, qrY, feedWidth: canvas.width };
}
