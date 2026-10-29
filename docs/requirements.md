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
| [#9 – Rapportere fejl](https://github.com/Mercantech/inv/issues/9) | Afsluttet ved gennemgangen den 29. oktober 2026 efter kontrol af kode og tests mod de opdaterede krav. |
| [#15 – Behandle fejl](https://github.com/Mercantech/inv/issues/15) | Færdiggjort den 29. oktober 2026: formular med alle tre statusser, brugerdefineret løsningsnote, annullering og genåbning. Backend og renderet frontend er testet mod API/PostgreSQL. |
| [#10 – Samlet status](https://github.com/Mercantech/inv/issues/10) | Implementeret den 29. oktober 2026 som manager-/admindashboard på forsiden; nøgletal, kategorifordeling, fejloversigt og filtrerede genveje. |

## Afsluttende dashboard

#10 samler nu de færdige funktioner i et dashboard på forsiden for EquipmentManager/Admin. Dashboardet viser samlet antal udstyr, ledigt udstyr, udlånt udstyr og antal udstyr med aktuelle fejl. Et udstyr med flere fejl tæller kun én gang i tallet for udstyr med fejl.

Udlån og fejl vises separat og må overlappe. Der skal være adgang til relevante udstyrs-/fejlvisninger og filtre. Tallene skal afspejle aktuelle registreringer ved opdatering. Frontend viser seks nøgletal, kategorifordeling, udstyr med aktuelle fejl og filtrerede genveje. Opdatering sker ved åbning og via Opdatér overblik.

## Samlet issuegennemgang

Se [gennemgangen af alle 19 issues](issue-audit-2026-10-29.md) for status, begrundelser og validering.

## Afslutning af fejlbehandling

#15 er færdiggjort efter issuegennemgangen: alle 70 .NET-tests består, inklusive renderet formulartest og statusforløb. Manuel browserkontrol er ikke udført på grund af computer-use-begrænsningen. Det aktive Sprint 3-omfang (#9, #12 og #15) er dermed leveret; dashboardet #10 er efterfølgende implementeret, mens QR/DYMO #13 fortsat er åbent.

## Dashboardvalidering

Alle 73 .NET-tests består. Dashboardtests kontrollerer tomt register, adgangskontrol, samme udstyr med flere fejl, kombinerede filtre, seneste hardwaretest, manuelle rapporter, historiske udlån, returnering, genudlån, gentest og opdatering efter fejlretning. En renderet komponenttest kontrollerer visninger, genveje, hentefejl og genforsøg.
