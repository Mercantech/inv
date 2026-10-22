# Udstyrsinventar

Grundstruktur til Version 1 af udstyrssystemet, bygget med ASP.NET Core 10, EF Core 10 og PostgreSQL 17. Datamodellen dækker udstyr, kategorier, udlån og fejlrapporter fra [backloggen](https://github.com/Mercantech/inv/issues). API'ets forretningsfunktioner og frontend bygges oven på denne struktur.

## Projektstruktur

```text
Inv.slnx
src/
  Inv.Domain/           Entiteter og statusværdier; ingen databaseafhængigheder
  Inv.Infrastructure/   DbContext, tabelkonfiguration og EF Core-migrationer
  Inv.Api/              HTTP, Mercantec JWT-validering og health checks
tests/
  Inv.Database.Tests/   Integrationstests mod en rigtig PostgreSQL-container
docs/
  database.md           Relationer, regler og datamodel
  development.md        Migrationer, konfiguration og drift
```

Afhængighederne går fra API til Infrastructure og videre til Domain. Nye funktioner kan samles i feature-mapper i API'et. Der er endnu intet behov for et ekstra servicelag eller generiske repositories oven på EF Core.

## Start lokalt med Docker

Forudsætninger: Docker med Linux-containere og Docker Compose. Kopiér miljøskabelonen, hvis `.env` ikke allerede findes, og vælg et lokalt databasepassword:

```powershell
Copy-Item .env.example .env
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d --build
```

Lokal opsætning bruger sit eget Docker-netværk og kræver ikke `dokploy-network`. PostgreSQL starter først, `migrate` anvender ventende migrationer, og API'et starter derefter. En mislykket migration forhindrer API'et i at starte. Data gemmes i et navngivet Docker-volume.

| Adresse | Funktion |
| --- | --- |
| `http://localhost:8080/health/live` | Processen kører; kræver ikke login |
| `http://localhost:8080/health/ready` | Databasen kan kontaktes, og alle migrationer er anvendt |
| `http://localhost:8080/openapi/v1.json` | OpenAPI-dokument, kun i Development |
| `http://localhost:8080/api/me` | Den indloggede brugers `sub`, navn og roller; kræver gyldigt Bearer-token |

```powershell
docker compose -f docker-compose.yml -f docker-compose.local.yml logs migrate api
docker compose -f docker-compose.yml -f docker-compose.local.yml down
```

`down` bevarer databasen. Tilføj kun `--volumes`, hvis alle lokale data bevidst skal slettes.

## Build og test

Til udvikling uden for API-containeren kræves .NET 10 SDK:

```powershell
dotnet tool restore
dotnet build Inv.slnx --configuration Release
dotnet test Inv.slnx --configuration Release
```

Integrationstestene kræver en kørende Docker-motor. Testcontainers opretter en separat, midlertidig PostgreSQL-database og fjerner den efter testen. Testene bruger ikke udviklings- eller produktionsdata.

GitHub Actions bygger løsningen, kontrollerer at modellen matcher migrationerne, kører databasetestene og bygger API'ets Docker-image ved push til `main` og ved pull requests.

## Auth og næste funktioner

API'et validerer Mercantec Auth-tokens med RS256, JWKS, `kid`, issuer, audience, udløb og et gyldigt UUID i `sub`. Brugerreferencer gemmes som `sub`; der er ingen lokal brugertabel med passwords. `EquipmentManager`-policyen bruger rollen fra `EQUIPMENT_MANAGER_ROLE`, som skal aftales med og tildeles i Mercantec Auth.

Frontendens login med authorization code + PKCE, klientregistrering og CRUD-funktioner er fortsat opgaver i backloggen. Logout i den kommende frontend skal rydde lokale tokens og vise appens egen login-side.

Se [datamodellen](docs/database.md) og [udviklings- og driftsvejledningen](docs/development.md).
