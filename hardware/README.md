# Arduino Oplà hardwaretester

Hardwaretesteren er portet fra [Mercantech/Arduino-Opla](https://github.com/Mercantech/Arduino-Opla/tree/2633e373230a5bbc9196a57348d93c0343566d8e/Hardwaretestere/HardwareTest), commit `2633e373230a5bbc9196a57348d93c0343566d8e`.

Firmwaren er **host-styret**: boardet venter på kommandoer over USB Serial (9600 baud). Inventarsystemets side `/hardware/run` (Chrome/Edge + Web Serial) kan:

1. **Installér firmware** direkte i browseren (SAM-BA/bossac over Web Serial — ingen Arduino IDE/PlatformIO på PC'en)
2. Starte testen, vise visuelle bekræftelser og gemme rapporten

## Brug for operatører

1. Log ind på inventaret → **Kør test**
2. Klik **Installér firmware** (første gang / ved opdatering): vælg port → vent → vælg bootloader-port
3. Klik **Forbind board** → **Start test**

Dobbelttryk Reset på boardet, hvis bootloader-porten ikke dukker op.

## Flash lokalt (udviklere)

```powershell
cd hardware/HardwareTest
pio run -e mkrwifi1010
pio run -e mkrwifi1010 -t upload
```

Firmwaren til browser-flash ligger i `src/Inv.Web/wwwroot/firmware/opla-hardwaretest.bin` (commits når firmwaren ændres). CI bygger stadig firmwaren via `hardware/Dockerfile`.

## Serial-protokol

Kommandoer (newline-termineret): `START`, `OK` / `FAIL`, `ABORT`, `STATUS` (gentag READY/IDS).

Events er JSON-linjer, fx:

- `{"v":1,"type":"ready"}`
- `{"v":1,"type":"ids","samd_id":"...","mac":"...","ecc_serial":"..."}`
- `{"v":1,"type":"step","name":"OLED","phase":"begin|awaiting_confirm|result",...}`
- `{"v":1,"type":"done","passed":8,"failed":0,"tests":[...]}`

`ENABLE_SOUND_TEST` er som standard `false`. `ENABLE_API_REPORT` er `false` — browseren poster til API'et.

## Kompilering i Docker

```powershell
docker build -f hardware/Dockerfile -t inv-hardware:local .
```
