# Hardwaretests fra Arduino Oplà

Inventarsystemet overtager modtagelse og visning af Oplà-testrapporter i .NET/PostgreSQL. FastAPI- og webserveren fra det gamle repo kopieres ikke. Hardwaretesteren findes i `hardware/HardwareTest`, med oprindelse og uploadvejledning i [hardware/README.md](../hardware/README.md).

## Brug

Åbn `/hardware` efter login med Mercantec Auth. Oversigten viser hvert board, tilknyttet udstyr, seneste test og fejlede komponenter. Søgning gælder navn, SAMD-chip-ID og udstyrsnavn. **Kun fejl i seneste test** udelader boards, hvis seneste test er bestået eller som endnu ikke er testet.

På `/hardware/{samdId}` vises seneste test og hele historikken i sider af 20 rapporter. Hver rapport har tidspunkt i UTC, antal beståede/fejlede tests, komponentresultater og noter. Sprungne tests vises som **Ikke testet** og tælles ikke som beståede eller fejlede. Gamle rapporter uden komponentdetaljer bevares med deres opsummering.

Under **Knyt boardet til udstyr** kan brugeren vælge eksisterende udstyr eller vælge **Opret som udstyr**. Det opretter kategorien Arduino Oplà ved behov, bruger chip-ID som serienummer og sætter tilstanden til ukendt. Der gættes ikke på sammenhængen mellem eksisterende serienumre og boards. Et udstyr kan have højst ét board, og et board højst ét udstyr. Tilknytningen kan ændres eller fjernes uden at slette rapporter.

Udstyrets detaljeside viser seneste hardwaretest og et link til hele historikken. En bestået test ændrer ikke den manuelt vurderede tilstand og lukker ingen manuelle fejlrapporter. Hardwaretests er observationer, adskilt fra fejlmodulets arbejdsgang og historik.

## API og adgang

| Metode | Endpoint | Adgang / funktion |
| --- | --- | --- |
| POST | `/api/devices` | Enhedsnøgle; registrér/opdatér board på SAMD-ID |
| POST | `/api/devices/{samdId}/tests` | Enhedsnøgle; gem ny test |
| GET | `/api/hardware/devices?equipmentId={uuid}` | Mercantec-login; boards med seneste test; valgfrit udstyrsfilter |
| GET | `/api/hardware/devices/{samdId}/tests?skip=0&take=20` | Mercantec-login; historik, højst 100 rapporter pr. request |
| POST | `/api/hardware/devices/{samdId}/equipment` | Mercantec-login; `{ "equipmentId": "uuid" }` eller `null` for at fjerne tilknytningen |
| POST | `/api/hardware/devices/{samdId}/create-equipment` | Mercantec-login; opret og tilknyt udstyr, eller returnér eksisterende tilknytning |
| POST | `/api/hardware/import` | Mercantec-login; importér JSON-array fra det gamle `/api/devices`, maks. 10 MB |

Samme midlertidige adgangsmodel som udstyr/kategorier: alle indloggede brugere kan importere og ændre tilknytning. Boardets nøgle giver kun adgang til de to modtagelses-endpoints. Den er ingen brugeridentitet og kan ikke læse eller ændre inventar, manuelle fejl eller tilknytninger. Nøglen skal ligge i env/Dokploy og i boardets ignorerede `Secrets.h`; aldrig i frontend eller Git. Manglende konfiguration deaktiverer modtagelsen med 503, forkert/manglende nøgle giver 401. De to endpoints har en samlet grænse på 120 requests/minut pr. API-proces og 64 KiB pr. request.

Sæt `OPLA_INGEST_KEY` til mindst 32 tilfældige tegn i miljøets env. Compose videresender den til API'et. Boardet sender den som `X-Device-Key`. Se [hardwarevejledningen](../hardware/README.md) for konfiguration, TLS-certifikat og upload. Firmware, som stadig sender til det gamle domæne uden nøgle, skal opdateres før den sender direkte til inventarsystemet.

En registrering bruger det eksisterende format: `samd_id`, `mac`, `ecc_serial`, `label`. En test bruger `passed`, `passed_count`, `failed_count` og `report.tests` med `name`, `ok`, `skipped`, `note`. Ny indsendelse kræver gyldige, entydige komponentresultater, der stemmer med opsummeringen. Testtidspunktet for nye rapporter sættes af serveren. Hvert accepteret POST er en ny test; firmwaren genforsøger ikke automatisk en rapport. Der er ingen antagelse om, at identiske målinger betyder samme test.

## Import af eksisterende historik

Importen skriver i én transaktion og sletter intet. Oprindelige testtidspunkter, resultater, rå detaljer og komponentdata bevares. Kombinationsnøglen `arduino-opla` + oprindeligt rapport-ID gør gentagne importer sikre. Importen overskriver ikke eksisterende boardoplysninger eller udstyrstilknytninger. Ukendte boards importeres uden tilknytning, så eksisterende udstyr ikke duplikeres automatisk.

### Fra browseren

Gem JSON-svaret fra det gamle systems `/api/devices` i en fil. Åbn `/hardware`, fold **Importér eksisterende Oplà-historik** ud og vælg filen. Resultatet viser nye boards, nye rapporter og allerede importerede rapporter. Dette kan også bruges i produktion uden adgang til databasecredentials.

### Fra Docker

Start først stakken, så migrationen er anvendt. Importér fra det gamle systems HTTPS-endpoint:

```powershell
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.local.yml run --rm -e OPLA_IMPORT_SOURCE=https://opla.mercantec.tech/api/devices import-opla
```

Importværktøjet bruger samme database som miljøet, og slutter efter importen. Til en fil kan en ekstra read-only volume monteres og `OPLA_IMPORT_SOURCE` sættes til filens containersti. Import fra terminalen kræver ikke et brugertoken, fordi processen allerede har direkte databaseadgang. Kommandoen kan også køres som `dotnet Inv.Api.dll --import-opla` i API-imaget med `CONNECTION_STRING` og `OPLA_IMPORT_SOURCE` i miljøet.

Behold det gamle system, mens boards opdateres, og kør en sidste import efter overgangen. Denne ændring flytter ikke domænet, flasher ikke boards og slukker ikke den gamle database. Forvent derfor flere rapporter i det gamle system indtil den fysiske overgang er udført.

## Datamodel og kontrol

`HardwareDevices` har SAMD-ID som primærnøgle samt MAC, ECC-serienummer, navn, oprindelig registrering, senest set og valgfrit udstyrs-ID. `HardwareTestReports` har UUIDv7, chip-ID, testtidspunkt, opsummering, JSONB-rapport, rå detaljer samt eventuelt importkilde og gammelt rapport-ID. Fremmednøgler bruger RESTRICT. Hardwareimport og registrering serialiseres med en PostgreSQL-transaktionslås, så samtidige registreringer/importer ikke giver dubletter.

Integrationstestene dækker adgang, opsummeringsvalidering, ikke testede komponenter, historik/paginering, samtidig registrering, tilknytning, gentaget import, bevarede tidspunkter og afvisning af ugyldig import. CI bygger både .NET/Docker og Arduino-firmwaren. Fysisk afprøvning kræver et board.
