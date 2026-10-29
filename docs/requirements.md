# Aftalte krav og sprintstatus

Opdateret 29. oktober 2026 på baggrund af brugerens beslutninger. Ved modstrid med ældre historiske beskrivelser i README gælder disse krav.

## Roller og fejlrapportering

- Almindelige indloggede brugere kan rapportere hardwarefejl ved at køre hardwaretest. Testen gemmes på udstyret via boardets SAMD-ID som serienummer; manglende udstyr oprettes automatisk.
- Kun EquipmentManager/Admin må oprette manuelle fejl eller rapporter, administrere fejlkategorier og behandle manuelle fejlrapporter.
- Hardwarefejl beregnes fra seneste test; tidligere tests bevares som historik. En bestået gentest fjerner tidligere aktuelle hardwarefejl, men lukker ikke manuelle rapporter.
- Manuelle rapporter har status Åben, Under behandling eller Løst. Løste rapporter bevares som historik og tæller ikke med i aktuelle fejl.

## Fejlantal erstatter tilstand

Tilstandsfeltet er bevidst fjernet fra datamodel, formularer, API og returnering. Det skal ikke genindføres for at opfylde ældre issuebeskrivelser.

Antal aktuelle fejl er summen af fejlede, ikke oversprungne komponenter i seneste hardwaretest og åbne/igangværende manuelle fejl. Tallet beregnes og kan ikke redigeres manuelt. Udlånsstatus er separat: et udstyr kan være både udlånt og fejlrapporteret. Nul registrerede fejl er ikke en garanti for, at udstyret er testet.

Oprettelse og redigering bruger navn, serienummer og kategori. Et registreret Arduino-boards serienummer og tilknytning er låst til boardets identitet. Returnering registrerer udlåns-ID og valgfri returnote; tidspunkt og ansvarlig sættes af serveren.

## Sprint 3

| Issue | Aftalt status |
| --- | --- |
| [#12 – Fejlkategorier](https://github.com/Mercantech/inv/issues/12) | Testet og accepteret af brugeren den 29. oktober 2026; afsluttet. |
| [#9 – Rapportere fejl](https://github.com/Mercantech/inv/issues/9) | Krav opdateret med hardwaretest for almindelige brugere og manuelle rapporter kun for EquipmentManager/Admin. Afventer accept. |
| [#15 – Behandle fejl](https://github.com/Mercantech/inv/issues/15) | Afventer accept af statusforløb, løsningsnote, ansvarlig/tid og bevaret historik. |
| [#10 – Samlet status](https://github.com/Mercantech/inv/issues/10) | På standby til afslutningen af Version 1; ikke en forudsætning for accept af fejlrapporteringen i Sprint 3. |

## Afsluttende dashboard

#10 samler de færdige funktioner i et dashboard i frontend ved afslutningen af Version 1. Dashboardet skal vise samlet antal udstyr, ledigt udstyr, udlånt udstyr og antal udstyr med aktuelle fejl. Et udstyr med flere fejl tæller kun én gang i tallet for udstyr med fejl.

Udlån og fejl vises separat og må overlappe. Der skal være adgang til relevante udstyrs-/fejlvisninger og filtre. Tallene skal afspejle aktuelle registreringer ved opdatering. Den konkrete frontendudformning afklares, når dashboardarbejdet starter.
