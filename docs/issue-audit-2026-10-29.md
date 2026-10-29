# Issuegennemgang – 29. oktober 2026

Gennemgangen omfatter alle 19 GitHub-issues og sammenholder de opdaterede krav med kode, tests og tidligere brugeraccept. Den er ikke en ny manuel browser-/hardwaretest eller en produktionsverifikation.

## Resultat

#9 og sprintissue #16 er lukket. Ingen tidligere lukkede issues er genåbnet: der er ikke fundet konkrete mangler i deres aftalte acceptkriterier, som begrunder genåbning. Tilstand er bevidst erstattet af beregnet fejlantal, og Arduino-serienumre er bevidst låst; disse aftalte ændringer er ikke regressioner.

Der er nu 12 afsluttede og 3 åbne funktionsissues. Med sprintissues medregnet er 14 af 19 lukket.

| Issue | Status efter gennemgang | Begrundelse |
| --- | --- | --- |
| [#1 – Login](https://github.com/Mercantech/inv/issues/1) | Forbliver lukket | PKCE, tokenvalidering, beskyttelse og lokal logout er implementeret og testet. |
| [#2 – Tilføj udstyr](https://github.com/Mercantech/inv/issues/2) | Forbliver lukket | Formular, validering, kategori, ID og tidsstempler findes; oprettelse er testet. |
| [#3 – Rediger udstyr](https://github.com/Mercantech/inv/issues/3) | Forbliver lukket | Eksisterende værdier, gemning, annullering og bevaret historik findes. Arduino-serienummer er låst efter aftale. |
| [#4 – Detaljer](https://github.com/Mercantech/inv/issues/4) | Forbliver lukket | Oplysninger, fejlantal, hardware-/manuelle fejl og aktuelle udlån vises. |
| [#5 – Find udstyr](https://github.com/Mercantech/inv/issues/5) | Forbliver lukket | Søgning, kategori og udlånsfilter findes. Filterbinding og forældede resultater er rettet; API-filtertest består. |
| [#6 – Aktive udlån](https://github.com/Mercantech/inv/issues/6) | Forbliver lukket | Aktive udlån, søgning, lånere og navigation findes; returnerede udlån udelades. |
| [#7 – Udlån](https://github.com/Mercantech/inv/issues/7) | Forbliver lukket | Låner fra Auth, godkender/tid, note og beskyttelse mod samtidige dobbeltudlån er testet. |
| [#8 – Returnering](https://github.com/Mercantech/inv/issues/8) | Forbliver lukket | Serverstyret godkendelse/tid, særskilt note og beskyttelse mod dobbeltreturnering er testet. Tilstand er udgået efter aftale. |
| [#9 – Rapportere fejl](https://github.com/Mercantech/inv/issues/9) | Lukket ved gennemgangen | Hardwaretest for indloggede brugere og manuelle rapporter kun for EquipmentManager/Admin. Kategori, beskrivelse, sub/tid, åbne rapporter og aktuelle fejlantal er implementeret. Tests dækker identitet, historik, kategorier og uafhængighed mellem hardware- og manuelle fejl. |
| [#10 – Dashboard](https://github.com/Mercantech/inv/issues/10) | Forbliver åbent; standby | Det samlede dashboard er aftalt til afslutningen af V1. Eksisterende filtre er ikke det færdige dashboard. |
| [#11 – Udstyrskategorier](https://github.com/Mercantech/inv/issues/11) | Forbliver lukket | Kategorier, validering, dubletkontrol samt brug i formularer og filtre findes. |
| [#12 – Fejlkategorier](https://github.com/Mercantech/inv/issues/12) | Forbliver lukket | Brugeren har accepteret funktionen; globale og afgrænsede kategorier er også dækket af tests. |
| [#13 – QR/DYMO](https://github.com/Mercantech/inv/issues/13) | Forbliver åbent | Printfunktionen mangler. Printermodel, labelstørrelse, faktisk print og scanning/loginretur er ikke afklaret/verificeret. |
| [#14 – Udlånshistorik](https://github.com/Mercantech/inv/issues/14) | Forbliver lukket | Historik, rækkefølge, lånere/godkendere, særskilte noter og genudlån er dækket. |
| [#15 – Behandle fejl](https://github.com/Mercantech/inv/issues/15) | Forbliver åbent | Frontend sender altid løsningsnoten "Løst" og mangler mulighed for tilbage til Åben/genåbning. API-funktioner alene opfylder ikke brugerflowet. |
| [#16 – Sprint 1](https://github.com/Mercantech/inv/issues/16) | Lukket ved gennemgangen | Alle seks stories og det tekniske grundlag er leveret. Udestående tidlig DYMO-afklaring følges i #13; den er ikke markeret gennemført. |
| [#17 – Sprint 2](https://github.com/Mercantech/inv/issues/17) | Forbliver lukket | Alle fire stories er afsluttet, og udlåns-/retur-/historiktests består. |
| [#18 – Sprint 3](https://github.com/Mercantech/inv/issues/18) | Forbliver åbent | #9 og #12 er afsluttet. #15 og afprøvning af det fulde statusforløb mangler. #10 er flyttet til afsluttende V1-arbejde. |
| [#19 – Sprint 4](https://github.com/Mercantech/inv/issues/19) | Forbliver åbent | #10 og #13 samt samlet slutafprøvning/produktionskontrol mangler. CI-punktet er markeret færdigt. |

## Validering

- Release-build af hele løsningen: 0 fejl og 0 advarsler.
- Alle 67 .NET-tests mod PostgreSQL består.
- Alle 10 JavaScript-auth-tests består.
- Seneste GitHub Actions-kørsel på main er bestået: [40852430509](https://github.com/Mercantech/inv/actions/runs/40852430509), commit 7defcce. Workflowet omfatter migrationskontrol og container-/firmwarebuild.
- Ingen applikationskode eller produktionskonfiguration er ændret ved denne gennemgang.

## Næste arbejde

Færdiggør #15 med brugerdefineret løsningsnote og det fulde statusforløb i frontend; afprøv derefter og afslut Sprint 3. Afklar og implementér QR/DYMO i #13. Dashboardet #10 forbliver på standby til afslutningen af V1 som aftalt.
