# Arduino Oplà hardwaretester

Hardwaretesteren er portet fra [Mercantech/Arduino-Opla](https://github.com/Mercantech/Arduino-Opla/tree/2633e373230a5bbc9196a57348d93c0343566d8e/Hardwaretestere/HardwareTest), commit `2633e373230a5bbc9196a57348d93c0343566d8e`.

Firmwaren er **host-styret**: boardet venter på kommandoer over USB Serial (9600 baud). Inventarsystemets side `/hardware/run` (Chrome/Edge + Web Serial) starter testen, viser visuelle bekræftelser og gemmer rapporten. WiFi/`Secrets.h` er ikke nødvendigt til den normale web-flow.

## Flash (én gang pr. board)

Med PlatformIO:

```powershell
cd hardware/HardwareTest
pio run -e mkrwifi1010
pio run -e mkrwifi1010 -t upload
```

Derefter: log ind på inventaret → **Kør test** → Forbind board → Start test.

## Serial-protokol

Kommandoer (newline-termineret): `START`, `OK` / `FAIL`, `ABORT`.

Events er JSON-linjer, fx:

- `{"v":1,"type":"ready"}`
- `{"v":1,"type":"ids","samd_id":"...","mac":"...","ecc_serial":"..."}`
- `{"v":1,"type":"step","name":"OLED","phase":"begin|awaiting_confirm|result",...}`
- `{"v":1,"type":"done","passed":8,"failed":0,"tests":[...]}`

`ENABLE_SOUND_TEST` er som standard `false` (buzzer = ikke testet). `ENABLE_API_REPORT` er `false` — browseren poster til API'et. Sæt `ENABLE_API_REPORT true` og udfyld WiFi i `Secrets.h` kun hvis boardet selv skal sende via HTTPS.

## Kompilering i Docker

Fra repoets rod:

```powershell
docker build -f hardware/Dockerfile -t inv-hardware:local .
```

Buildet kompilerer firmwaren uden lokal secrets og uden fysisk hardware. Flash kræver et tilsluttet MKR WiFi 1010 + MKR IoT Carrier.
