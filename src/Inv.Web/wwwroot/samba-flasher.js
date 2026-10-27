// Adapted from Vail-CW/vail-adapter online-updater/samba-flasher.js (MIT).
// WebSerial SAM-BA / BOSSA flasher for SAMD21 (Arduino MKR WiFi 1010 uses offset 0x2000).
// Importeres af firmware-flash.js — nginx sender Cache-Control: no-cache for disse scripts.

const FLASH_APP_ADDR = 0x2000;
const SRAM_BUFFER = 0x20004000;
const CHUNK = 0x1000;
const AIRCR = 0xE000ED0C;
const AIRCR_RESET = 0x05FA0004;

const hex8 = (n) => (n >>> 0).toString(16).toUpperCase().padStart(8, '0');

const CRC16_TABLE = (() => {
    const t = new Uint16Array(256);
    for (let i = 0; i < 256; i++) {
        let c = (i << 8) & 0xFFFF;
        for (let j = 0; j < 8; j++) {
            c = (c & 0x8000) ? (((c << 1) ^ 0x1021) & 0xFFFF) : ((c << 1) & 0xFFFF);
        }
        t[i] = c;
    }
    return t;
})();

function crc16(data) {
    let crc = 0;
    for (let i = 0; i < data.length; i++) {
        crc = ((crc << 8) & 0xFFFF) ^ CRC16_TABLE[((crc >> 8) ^ data[i]) & 0xFF];
    }
    return crc;
}

export class SAMBAFlasher {
    constructor({ log, progress, chunkSize } = {}) {
        this.log = log || (() => {});
        this.progress = progress || (() => {});
        this.chunkSize = chunkSize || CHUNK;
        this.port = null;
        this.reader = null;
        this.writer = null;
        this.rx = '';
        this._pump = null;
    }

    async open(port, baudRate = 115200) {
        this.port = port;
        await port.open({ baudRate });
        this.writer = port.writable.getWriter();
        this.reader = port.readable.getReader();
        this.rx = '';
        this._pump = (async () => {
            try {
                while (true) {
                    const { value, done } = await this.reader.read();
                    if (done) break;
                    if (value) {
                        for (let i = 0; i < value.length; i++) {
                            this.rx += String.fromCharCode(value[i]);
                        }
                    }
                }
            } catch {
                /* cancelled */
            }
        })();
    }

    async close() {
        try { if (this.reader) { await this.reader.cancel(); this.reader.releaseLock(); } } catch { /* ignore */ }
        try { if (this.writer) this.writer.releaseLock(); } catch { /* ignore */ }
        try { if (this.port) await this.port.close(); } catch { /* ignore */ }
        this.reader = this.writer = this.port = null;
    }

    async _sendStr(s) { await this.writer.write(new TextEncoder().encode(s)); }
    async _sendBytes(u8) { await this.writer.write(u8); }

    async _waitFor(token, timeoutMs = 5000) {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            const idx = this.rx.indexOf(token);
            if (idx !== -1) {
                const out = this.rx.slice(0, idx + token.length);
                this.rx = this.rx.slice(idx + token.length);
                return out;
            }
            await new Promise((r) => setTimeout(r, 15));
        }
        throw new Error(`Timeout i bootloader-svar (ventede "${token.replace(/[\r\n]/g, '?')}")`);
    }

    async connect() {
        try {
            await this.port.setSignals({ dataTerminalReady: true, requestToSend: true });
        } catch { /* ignore */ }

        let version = '';
        for (let attempt = 1; attempt <= 5 && !version; attempt++) {
            this.rx = '';
            await this._sendStr('N#');
            await new Promise((r) => setTimeout(r, 120));
            await this._sendStr('V#');
            try {
                const line = await this._waitFor('\n', 1200);
                await new Promise((r) => setTimeout(r, 150));
                version = (line + this.rx).replace(/\0/g, '').replace(/[\r\n]/g, ' ').trim();
            } catch {
                this.log(`Ingen bootloader-svar endnu (${attempt}/5)…`);
            }
        }
        this.rx = '';
        if (!version) {
            throw new Error('Ingen svar fra bootloader. Dobbelttryk Reset på boardet, vælg den nye COM-port, og prøv igen.');
        }
        this.log(`Bootloader: ${version}`);
        return version;
    }

    async eraseApp() {
        this.log('Sletter applikationsflash…');
        this.rx = '';
        await this._sendStr(`X${hex8(FLASH_APP_ADDR)}#`);
        await this._waitFor('X', 15000);
        this.log('Sletning færdig.');
    }

    async writeFirmware(bin, startAddr = FLASH_APP_ADDR) {
        const total = bin.length;
        let offset = 0;
        while (offset < total) {
            const size = Math.min(this.chunkSize, total - offset);
            const chunk = bin.subarray(offset, offset + size);
            await this._sendStr(`S${hex8(SRAM_BUFFER)},${hex8(size)}#`);
            await new Promise((r) => setTimeout(r, 15));
            await this._sendBytes(chunk);
            await this._sendStr(`Y${hex8(SRAM_BUFFER)},0#`);
            await this._waitFor('Y', 5000);
            await this._sendStr(`Y${hex8(startAddr + offset)},${hex8(size)}#`);
            await this._waitFor('Y', 8000);
            offset += size;
            this.progress(offset, total);
        }
        this.log(`Skrev ${total} bytes.`);
    }

    async _waitForBytes(count, timeoutMs = 3000) {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            if (this.rx.length >= count) {
                const out = [];
                for (let i = 0; i < count; i++) out.push(this.rx.charCodeAt(i) & 0xFF);
                this.rx = this.rx.slice(count);
                return out;
            }
            await new Promise((r) => setTimeout(r, 15));
        }
        throw new Error(`Timeout ved læsning af ${count} bytes`);
    }

    async readWord(addr) {
        this.rx = '';
        await this._sendStr(`w${hex8(addr)},4#`);
        const b = await this._waitForBytes(4, 3000);
        return (b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)) >>> 0;
    }

    async crcRegion(addr, size) {
        this.rx = '';
        await this._sendStr(`Z${hex8(addr)},${hex8(size)}#`);
        const reply = await this._waitFor('#', 20000);
        const m = reply.match(/Z([0-9A-Fa-f]{8})#/);
        if (!m) throw new Error('Ugyldigt CRC-svar fra bootloader');
        return parseInt(m[1], 16) >>> 0;
    }

    async _sampleMismatches(bin, startAddr) {
        const offsets = new Set([0, 4]);
        for (let o = 0; o < bin.length; o += 1024) offsets.add(o);
        offsets.add(bin.length - 4);
        const bad = [];
        for (const raw of [...offsets].sort((a, b) => a - b)) {
            const off = raw & ~3;
            if (off < 0 || off + 4 > bin.length) continue;
            const dev = await this.readWord(startAddr + off);
            const local = (bin[off] | (bin[off + 1] << 8) | (bin[off + 2] << 16) | (bin[off + 3] << 24)) >>> 0;
            if (dev !== local) bad.push(off);
        }
        return bad;
    }

    async verifyFirmware(bin, startAddr = FLASH_APP_ADDR) {
        this.log('Verificerer flash…');
        let crcMatch = null;
        try {
            const dev = await this.crcRegion(startAddr, bin.length);
            crcMatch = ((dev & 0xFFFF) === crc16(bin));
        } catch {
            this.log('CRC ikke understøttet — tjekker stikprøver.');
        }
        if (crcMatch === true) {
            this.log('Verifikation OK (CRC).');
            return;
        }
        const bad = await this._sampleMismatches(bin, startAddr);
        if (bad.length) {
            throw new Error(`Verifikation fejlede (første afvigelse ved 0x${bad[0].toString(16)}).`);
        }
        this.log('Verifikation OK (stikprøver).');
    }

    async resetDevice() {
        this.log('Genstarter boardet…');
        try {
            await this._sendStr(`W${hex8(AIRCR)},${hex8(AIRCR_RESET)}#`);
        } catch { /* port drops on reset */ }
    }
}

export const FLASH_OFFSET = FLASH_APP_ADDR;
