# Udstyrsinventar

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

Blazor-frontenden har login med authorization code + PKCE, callback og automatisk token-refresh. Klientregistrering og CORS hos Auth-platformen skal passe til miljøets adresser. Logout afslutter Auth-sessionen som specificeret i integrationsopgaven. Se [auth-vejledningen](docs/auth.md) for opsætning, test og logout-adfærd. Oprettelse af kategorier og udstyr er implementeret; de øvrige funktioner følges i backloggen.

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
