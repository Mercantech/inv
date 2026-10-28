# Udstyrsinventar

## Aktive udlån (V1-06)

Åbn **Aktive udlån** i navigationen (`/udlaan`). Oversigten viser kun udlån uden `ReturnedAt`, med udstyr, serienummer, låner og lokalt udlånstidspunkt; nyeste udlån vises først. Klik på en række for at åbne udstyrets detaljeside. Søg på dele af udstyrsnavn, serienummer, lånernavn eller e-mail, eller på eksakt udstyrsnummer/bruger-ID. Søgningen ignorerer store/små bogstaver og omgivende mellemrum. **Nulstil** viser alle aktive udlån igen.

`GET /api/loans?search=...` kræver login og returnerer `{ loans, directoryUnavailable }`. Lånernavne hentes fra det eksisterende Auth-register. Ved manglende konto vises bruger-ID; ved Auth-fejl vises også en besked om begrænset søgning. Ingen ny migration eller konfiguration kræves. Returnerede udlån udelades ved næste søgning/opdatering eller genåbning af siden. **Søg / opdatér** genindlæser oversigten, også efter returnering fra en anden browser.

Integrationstestene i `LoanTests` dækker aktive/returnerede udlån, udstyr og lånerdata, søgefelter, ingen resultater, manglende konto, Auth-fejl og loginbeskyttelse. Manuel browserkontrol: åbn oversigten, søg efter både udstyr og låner, åbn detaljesiden, returnér udstyret og kontrollér, at det er væk, når oversigten åbnes igen.

Teknisk validering den 28. oktober 2026: Release-build uden fejl/advarsler, alle 8 udlånstests bestået i Linux/Docker mod midlertidig PostgreSQL, lokal Compose rebuild/opstart gennemført og API-readiness `Healthy`. Manuel kontrol i en indlogget browser er endnu ikke gennemført.

## Arduino Oplà-hardwaretests

Oplà-rapportmodtagelsen er portet til .NET. På `/hardware` kan indloggede brugere se boards, seneste komponentfejl og testhistorik, importere gamle rapporter og knytte boards til udstyr. Udstyrets detaljeside viser derefter seneste test. Hardwaretesteren findes i `hardware/HardwareTest`. Se [integrations- og importvejledningen](docs/hardware.md) for enhedsnøgle, historikimport og upload af firmware.

Grundstruktur til Version 1 af udstyrssystemet, bygget med Blazor WebAssembly på .NET 10, ASP.NET Core 10, EF Core 10 og PostgreSQL 17. Datamodellen dækker udstyr, kategorier, udlån og fejlrapporter fra [backloggen](https://github.com/Mercantech/inv/issues). Blazor har en dansk startside og en forbindelseskontrol. Login med Mercantec Auth er implementeret; forretningsfunktionerne bygges oven på denne struktur.

## Projektstruktur

```text
Inv.slnx
src/
  Inv.Domain/           Entiteter og statusværdier; ingen databaseafhængigheder
  Inv.Infrastructure/   DbContext, tabelkonfiguration og EF Core-migrationer
  Inv.Api/              HTTP, Mercantec JWT-validering og health checks
  Inv.Web/              Blazor WebAssembly, layout, sider og HTTP-klient
tests/
  Inv.Database.Tests/   Integrationstests mod en rigtig PostgreSQL-container
docs/
  database.md           Relationer, regler og datamodel
  development.md        Migrationer, konfiguration og drift
```

Afhængighederne går fra API til Infrastructure og videre til Domain. Blazor kommunikerer med API'et over HTTP og har ingen databaseafhængigheder. Nye funktioner kan samles i feature-mapper i API'et og som Razor-komponenter i frontend.

## Start lokalt med Docker

Forudsætninger: Docker med Linux-containere og Docker Compose. Kopiér miljøskabelonen, hvis `.env` ikke allerede findes, og vælg et lokalt databasepassword:

```powershell
Copy-Item .env.example .env
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d --build
```

Lokal opsætning bruger sit eget Docker-netværk og kræver ikke `dokploy-network`. PostgreSQL starter først, `migrate` anvender ventende migrationer, og API'et starter derefter. En mislykket migration forhindrer API'et i at starte. Data gemmes i et navngivet Docker-volume.

| Adresse | Funktion |
| --- | --- |
| `http://localhost:3000` | Blazor-frontend |
| `http://localhost:3000/forbindelse` | Kontrollér API og database fra browseren |
| `http://localhost:3000/api/health/ready` | Readiness via frontendens API-proxy |
| `http://localhost:8080/health/live` | Processen kører; kræver ikke login |
| `http://localhost:8080/health/ready` | Databasen kan kontaktes, og alle migrationer er anvendt |
| `http://localhost:8080/openapi/v1.json` | OpenAPI-dokument, kun i Development |
| `http://localhost:8080/api/me` | Den indloggede brugers `sub`, navn og roller; kræver gyldigt Bearer-token |

```powershell
docker compose -f docker-compose.yml -f docker-compose.local.yml logs migrate api web
docker compose -f docker-compose.yml -f docker-compose.local.yml down
```

`down` bevarer databasen. Tilføj kun `--volumes`, hvis alle lokale data bevidst skal slettes.

## Start API'et fra IDE eller terminal

Når databasen skal køre i Docker og API'et lokalt:

```powershell
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d db
dotnet run --project src/Inv.Api -- --migrate
dotnet run --project src/Inv.Api
```

API'ets launch-profil bruger `Development` og læser automatisk projektets `.env`. Den lokale forbindelse sammensættes fra `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` og `POSTGRES_PORT` med host `127.0.0.1`. Eksisterende forbindelsesstrenge og miljøvariabler har forrang. Docker og produktion bruger fortsat deres egne miljøvariabler. Hvis hele Compose-stakken allerede kører, skal den lokale API-proces bruge en anden port, fx `-- --urls http://localhost:8081`.

Ved Windows Application Control, som blokerer de lokalt byggede programmer, bruges den fulde Docker-opstart ovenfor.

## Build og test

Til udvikling uden for API-containeren kræves .NET 10 SDK:

```powershell
dotnet tool restore
dotnet build Inv.slnx --configuration Release
dotnet test Inv.slnx --configuration Release
```

Integrationstestene kræver en kørende Docker-motor. Testcontainers opretter en separat, midlertidig PostgreSQL-database og fjerner den efter testen. Testene bruger ikke udviklings- eller produktionsdata.

GitHub Actions bygger løsningen, kontrollerer at modellen matcher migrationerne, kører testene og bygger både API- og Blazor-imaget ved push til `main` og ved pull requests. Testene kan også køres i Linux-containeren beskrevet i [udviklingsvejledningen](docs/development.md).

## Auth og næste funktioner

API'et validerer Mercantec Auth-tokens med RS256, JWKS, `kid`, issuer, audience, udløb og et gyldigt UUID i `sub`. Brugerreferencer gemmes som `sub`; der er ingen lokal brugertabel med passwords. `EquipmentManager`-policyen bruger rollen fra `EQUIPMENT_MANAGER_ROLE`, som skal aftales med og tildeles i Mercantec Auth.

Blazor-frontenden har login med authorization code + PKCE, callback og automatisk token-refresh. Klientregistrering og CORS hos Auth-platformen skal passe til miljøets adresser. Logout rydder appens egne tokens og åbner dens login-side; den fælles Mercantec Auth-session og andre apps bevares. Se [auth-vejledningen](docs/auth.md) for opsætning, test og logout-adfærd. Oprettelse af kategorier og udstyr er implementeret; de øvrige funktioner følges i backloggen.

Se [datamodellen](docs/database.md) og [udviklings- og driftsvejledningen](docs/development.md).

## Tilføj udstyr (V1-02)

Åbn `/udstyr` for at se oversigten, og vælg **Opret udstyr**. Formularen på `/udstyr/opret` kræver navn, serienummer, en eksisterende kategori og tilstand. Navn og serienummer må højst indeholde 200 tegn. Efter oprettelse åbnes detaljesiden på `/udstyr/{id}`, og udstyret kan findes i oversigten.

API'et tildeler automatisk et entydigt ID samt oprettelses- og ændringstidspunkt. Funktionen bruger den eksisterende udstyrstabel og kræver ingen ny migration.

| Metode | Endpoint | Funktion |
| --- | --- | --- |
| GET | `/api/equipment/` | Hent udstyrsoversigten |
| GET | `/api/equipment/{id}` | Hent ét udstyr; ukendt ID giver 404 |
| POST | `/api/equipment/` | Opret udstyr |

POST modtager felterne `name`, `serialNumber`, `equipmentCategoryId` og `condition`. Tilstand sendes som et tal: 0 = ukendt, 1 = god, 2 = slidt, 3 = beskadiget og 4 = ude af drift. Ugyldige feltværdier giver 400 med valideringsbeskeder.

**Midlertidig adgangsbeslutning:** Alle endpoints kræver login, men oprettelse kræver foreløbigt ingen bestemt rolle, ligesom oprettelse af kategorier. Rollekravet `EquipmentManager` er fjernet efter aftale med brugeren. Rollebegrænsning skal genindføres, når den ønskede adgangsmodel aktiveres.

**Validering den 23. oktober 2026:** Brugeren har gennemført den aftalte manuelle afprøvning og meldt testene bestået: oprettelse, oversigt og detaljeside, påkrævede felter og ugyldige værdier, ID og tidsstempler samt login uden rollekrav. Release-build og lokal Docker-opstart er gennemført uden fejl. Produktionsdeployment af commit `abe1313` er gennemført i Dokploy.

## Søgning og filtrering af udstyr (V1-05)

På `/udstyr` kan indloggede brugere søge på dele af navn eller serienummer uden hensyn til store og små bogstaver. Kategori og tilstand kan vælges som filtre og kombineres med søgningen. Vælg **Søg** for at anvende valgene; **Nulstil** rydder søgning og filtre og viser alt udstyr igen. Oversigten viser antal resultater, links til detaljesider og en tydelig besked, hvis ingen udstyr matcher.

`GET /api/equipment/` accepterer følgende valgfrie query-parametre:

| Parameter | Betydning |
| --- | --- |
| `search` | Del af navn eller serienummer; indledende og afsluttende mellemrum fjernes |
| `categoryId` | ID på den ønskede kategori |
| `condition` | Tilstand: 0 = ukendt, 1 = god, 2 = slidt, 3 = beskadiget, 4 = ude af drift |

Eksempel: `/api/equipment/?search=arduino&condition=1`. Filtrene kombineres, så udstyret skal opfylde alle valgte betingelser. Særlige søgetegn som `%` og `_` behandles som almindelig tekst. En ugyldig tilstand afvises med 400. Datamodellen er uændret, og der kræves ingen ny migration.

**Manuel afprøvning den 26. oktober 2026:** Brugeren har meldt søgning og kategori- og tilstandsfiltrering bestået efter afprøvning lokalt i Docker, herunder kombinerede filtre, tilstanden ukendt, ingen resultater og nulstilling.

**Resterende arbejde:** V1-05 forbliver åbent. Visning af udlånsstatus og filter på udlånsstatus afventer udlånsmodulet i Sprint 2. Tilstand er en samlet vurdering; konkrete fejl på eksempelvis skærm eller føler håndteres senere i fejlmodulet med mulighed for flere aktuelle fejl og historik over udbedrede fejl. Filtrering på konkrete fejl er endnu ikke implementeret.

## Redigering af udstyr (V1-03)

Åbn et udstyr fra `/udstyr`, og vælg **Rediger udstyr**. Formularen på `/udstyr/{id}/rediger` viser de gemte værdier for navn, serienummer, kategori og tilstand. **Gem ændringer** validerer og gemmer oplysningerne og åbner detaljesiden igen. **Annuller** vender tilbage til detaljesiden uden at sende ændringer til API'et.

`PUT /api/equipment/{id}` modtager de samme fire felter som oprettelse og returnerer det opdaterede udstyr med status 200. Tomme eller for lange navne/serienumre, en manglende eller ukendt kategori og en ugyldig tilstand afvises med 400. Et ukendt udstyrs-ID giver 404. Feltvalidering og kategorikontrol deles med oprettelse.

ID, kort ID og oprettelsestidspunkt bevares; ændringstidspunktet opdateres ved hver gemning. Eksisterende udlån og deres noter, godkendere og tidsstempler ændres ikke. Funktionen kræver login, men har foreløbigt samme aftalte adgangsmodel som oprettelse: alle indloggede brugere kan redigere. Der kræves ingen migration eller ny miljøkonfiguration.

Automatiske integrationstests i `EquipmentEditingTests` dækker gemning, efterfølgende læsning, bevarelse af identitet og udlånshistorik, gemning uden feltændringer, feltvalidering, ukendt kategori/udstyr, loginbeskyttelse og regression af oprettelse.

Manuel browserkontrol før endelig godkendelse:

1. Åbn et udstyr og vælg **Rediger udstyr**; kontrollér, at alle fire gemte værdier vises.
2. Ret alle fire felter, gem, og kontrollér værdierne på detaljesiden og i oversigten.
3. Forsøg at gemme tomt navn/serienummer eller manglende kategori/tilstand; kontrollér fejlbeskederne.
4. Ret felterne og vælg **Annuller**; kontrollér, at de oprindelige værdier og ændringstidspunktet er bevaret.
5. Åbn `/udstyr/{et-ukendt-guid}/rediger`; kontrollér beskeden om ukendt udstyr.

Browserkontrollen er endnu ikke meldt gennemført; V1-03 holdes derfor åbent til endelig kontrol.

**Teknisk validering den 26. oktober 2026:** Release-build gennemført uden fejl eller advarsler. Alle 32 integrationstests bestået i Linux/Docker mod en midlertidig PostgreSQL-database. EF-kontrol fandt ingen modelændringer. Lokal Compose-opstart med rebuild er gennemført, og API'ets `/health/ready` svarer `Healthy`.

## Udlåne udstyr (V1-07, Sprint 2)

Åbn et udstyrs detaljeside og vælg **Udlån udstyr**. Søg på navn, e-mail eller bruger-ID, vælg en låner og tilføj eventuelt en note (højst 4000 tegn). **Registrér udlån** gemmer lånerens Mercantec Auth-`sub`, den indloggede brugers `sub` som godkender og serverens aktuelle UTC-tid. **Annuller** lukker formularen uden at gemme.

Låneropslaget omfatter alle konti i Mercantec Auth, også konti som aldrig har logget ind i udstyrssystemet. Deaktiverede konti vises, men kan ikke vælges til nye udlån. Låneren kontrolleres igen hos Auth ved gemning. Backend bruger Auths eksisterende `GET /api/admin/users-directory` med den eksisterende centrale MCP-nøgle i miljøvariablen `AUTH_MCP_API_KEY`; der oprettes ingen nye API-nøgler eller lokale brugerkonti. Nøglen må kun findes i `.env`/Dokploy, aldrig i git eller frontend. Kun navn, e-mail, `sub` og kontostatus sendes til frontend; provider- og klienthistorik videresendes ikke.

Udstyret vises som **Udlånt** på detaljesiden og i udstyrsoversigten. Det aktuelle udlån viser låner, tidspunkt, godkenderens bruger-ID og note. Hvis Auth er utilgængelig, vises eksisterende udlån stadig med lånerens bruger-ID, mens nye udlån afvises med 503. Der bruges ingen lokal kopi af brugerregistret. Samme midlertidige adgangsmodel som udstyrsredigering gælder: alle indloggede brugere kan se lånere og registrere udlån. Rollebegrænsning afventer den aftalte senere aktivering.

| Metode | Endpoint | Funktion |
| --- | --- | --- |
| GET | `/api/borrowers?search=...` | Auth-brugere; valgfri søgning på navn, e-mail eller eksakt `sub` |
| GET | `/api/equipment/{id}/loan` | Aktuelt udlån; 204 hvis udstyret er ledigt, 404 ved ukendt udstyr |
| POST | `/api/equipment/{id}/loans` | Opret med `borrowerSubject` og valgfri `note`; svar 201 |

Ugyldig låner/note giver 400, manglende login 401, ukendt udstyr 404 og allerede udlånt udstyr 409. Det eksisterende unikke databaseindeks sikrer også mod dobbeltudlån ved samtidige forespørgsler. Afsluttede udlån i `Loans` bevares ved nye udlån. Ingen migration er nødvendig. Udlånsoversigt (#6), returnering (#8), historikvisning (#14) og udlånsfilteret fra #5 følger som separate opgaver.

**Teknisk validering den 26. oktober 2026:** Release-build uden fejl/advarsler, 46 automatiske tests bestået i Linux/Docker og ingen ventende modelændringer. Testene dækker bl.a. låner uden tidligere app-login, validering, loginbeskyttelse, Auth-fejl, samtidige udlån, visning af udlånsstatus efter redigering og bevarelse af afsluttede udlån. Den eksisterende centrale Auth-adgang er verificeret mod det rigtige brugerregister og konfigureret i den ignorerede lokale `.env`.

Manuel browserkontrol før issue #7 afsluttes:

1. Åbn et ledigt udstyr, vælg **Udlån udstyr**, og find en Auth-bruger, som ikke tidligere har brugt appen.
2. Tilføj en note, registrér udlånet, og kontrollér låner, tidspunkt, godkender og note på detaljesiden efter genindlæsning.
3. Åbn udstyrsoversigten, og kontrollér status **Udlånt**. Udstyret må ikke kunne udlånes igen.
4. Åbn formularen på et andet ledigt udstyr og vælg **Annuller**; udstyret skal stadig være ledigt.
5. Kontrollér søgning uden resultater, valg af en aktiv konto og fejlbesked hvis Auth-opslaget ikke kan hentes.

Issue #7 forbliver åbent til manuel kontrol. Produktionsmiljøet skal have den eksisterende Auth MCP-nøgle konfigureret som `AUTH_MCP_API_KEY`, før funktionen tages i brug dér.
