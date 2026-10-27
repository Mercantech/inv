// Adapted from Vail-CW/vail-adapter + Arduino BOSSA SAM-BA (MIT).
// WebSerial flasher for SAMD21 (MKR WiFi 1010 / Oplà). Flash offset 0x2000.
//
// Vigtigt (BOSSA/Chromium):
// - S-kommando og binær payload må IKKE lande i samme USB-pakke → pause efter S#.
// - Chromium kan splitte writes ved buffer-grænser → små chunks + stort bufferSize.

const FLASH_APP_ADDR = 0x2000;
/** Arduino bossac staging-buffer for SAMD21 (ikke 0x20004000). */
const SRAM_BUFFER = 0x20005000;
/** 256 bytes = én flash-row; mere pålideligt over Web Serial end 4 KiB. */
const CHUNK = 0x100;
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

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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
        // Stort bufferSize reducerer Chromiums tendens til at splitte writes midt i en chunk.
        await port.open({ baudRate, bufferSize: 16 * 1024 });
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

    async _sendStr(s) {
        await this.writer.write(new TextEncoder().encode(s));
    }

    async _sendBytes(u8) {
        // Eksplicit kopi — undgå SharedArrayBuffer/subarray-kanttilfælde i Chromium.
        const copy = u8 instanceof Uint8Array ? u8.slice() : new Uint8Array(u8);
        await this.writer.write(copy);
    }

    async _waitFor(token, timeoutMs = 5000) {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            const idx = this.rx.indexOf(token);
            if (idx !== -1) {
                const out = this.rx.slice(0, idx + token.length);
                this.rx = this.rx.slice(idx + token.length);
                return out;
            }
            await sleep(10);
        }
        const tail = this.rx.slice(-40).replace(/[\r\n]/g, '·');
        throw new Error(
            `Timeout i bootloader-svar (ventede "${token.replace(/[\r\n]/g, '?')}"` +
            `${tail ? `, rx="…${tail}"` : ', tom rx'})`);
    }

    async _expectY(timeoutMs) {
        // BOSSA forventer "Y\n\r" — match på Y er nok; ryd rest af linjen.
        await this._waitFor('Y', timeoutMs);
        const nl = this.rx.search(/[\n\r]/);
        if (nl !== -1) {
            let i = nl;
            while (i < this.rx.length && (this.rx[i] === '\n' || this.rx[i] === '\r')) i++;
            this.rx = this.rx.slice(i);
        }
    }

    async connect() {
        try {
            await this.port.setSignals({ dataTerminalReady: true, requestToSend: true });
        } catch { /* ignore */ }

        let version = '';
        for (let attempt = 1; attempt <= 5 && !version; attempt++) {
            this.rx = '';
            await this._sendStr('N#');
            await sleep(120);
            await this._sendStr('V#');
            try {
                const line = await this._waitFor('\n', 1200);
                await sleep(150);
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
        await this._waitFor('X', 20000);
        this.rx = '';
        // Bootloader er travl efter chip-erase.
        await sleep(300);
        this.log('Sletning færdig.');
    }

    /**
     * Stage chunk i SRAM (S#) derefter Y-copy til flash.
     * Pause efter S# er påkrævet (SAM firmware USB packet bug / BOSSA flush).
     */
    async writeFirmware(bin, startAddr = FLASH_APP_ADDR) {
        const total = bin.length;
        let offset = 0;
        this.log(`Skriver ${total} bytes i ${this.chunkSize}-byte bidder…`);

        while (offset < total) {
            const size = Math.min(this.chunkSize, total - offset);
            const chunk = bin.subarray(offset, offset + size);
            this.rx = '';

            await this._sendStr(`S${hex8(SRAM_BUFFER)},${hex8(size)}#`);
            // Flush-ækvivalent: undgå at S# og payload kombineres i samme USB-pakke.
            await sleep(40);
            await this._sendBytes(chunk);
            // Vent til USB CDC har sendt payload, før næste kommando.
            await sleep(20);

            let staged = false;
            for (let attempt = 1; attempt <= 3 && !staged; attempt++) {
                this.rx = '';
                await this._sendStr(`Y${hex8(SRAM_BUFFER)},0#`);
                try {
                    await this._expectY(4000);
                    staged = true;
                } catch (err) {
                    if (attempt === 3) throw err;
                    this.log(`SRAM-stage timeout — prøver igen (${attempt}/3)…`);
                    await sleep(50);
                }
            }

            this.rx = '';
            await this._sendStr(`Y${hex8(startAddr + offset)},${hex8(size)}#`);
            await this._expectY(10000);

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
            await sleep(10);
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
