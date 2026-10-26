# Arduino Oplà hardwaretester

Hardwaretesteren er portet fra [Mercantech/Arduino-Opla](https://github.com/Mercantech/Arduino-Opla/tree/2633e373230a5bbc9196a57348d93c0343566d8e/Hardwaretestere/HardwareTest), commit `2633e373230a5bbc9196a57348d93c0343566d8e`.
Sensor- og komponenttestene er bevaret. API-afsendelsen bruger nu inventarsystemets modtagelse med en separat enhedsnøgle, kontrollerer HTTP-status og opbygger JSON uden den tidligere faste rapportbuffer. WiFi-navnet sendes ikke i nye rapporter.

## Konfiguration og upload

1. Kopiér `HardwareTest/src/Secrets.h.example` til `HardwareTest/src/Secrets.h`.
2. Udfyld WiFi, `API_HOST` (kun værtsnavnet) og `API_DEVICE_KEY`. Nøglen skal matche API'ets `OPLA_INGEST_KEY` og være mindst 32 tilfældige tegn. Filen er ignoreret af Git og Docker-builds.
3. Opdatér NINA-firmware og upload SSL-rodcertifikatet for det valgte API-værtsnavn med Arduino IDE/Firmware Updater. Boardet bruger HTTPS på port 443.
4. Med PlatformIO installeret:

```powershell
cd hardware/HardwareTest
pio run -e mkrwifi1010
pio run -e mkrwifi1010 -t upload
pio device monitor
```

Boardet registreres på `POST /api/devices` og sender derefter rapporten til `POST /api/devices/{samd_id}/tests`. Begge requests bruger `X-Device-Key`. Chip-ID'et er permanent; MAC og ECC-serienummer gemmes som yderligere oplysninger. En afvist eller mislykket request vises som API-fejl på boardet.

`ENABLE_SOUND_TEST` er som standard `false`: buzzer vises som **Ikke testet** og tælles ikke med i beståede eller fejlede tests. `ENABLE_API_REPORT=false` deaktiverer afsendelse.

## Kompilering i Docker

Fra repoets rod:

```powershell
docker build -f hardware/Dockerfile -t inv-hardware:local .
```

Buildet kompilerer firmwaren uden lokale secrets og uden fysisk hardware. Upload og afprøvning af sensorer kræver et tilsluttet MKR WiFi 1010 + MKR IoT Carrier. Docker-buildet flasher ikke enheder.
