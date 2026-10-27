# Udvikling og drift

## Konfiguration

Konfiguration læses fra miljøet. Runtime-forbindelsen vælges i rækkefølgen `CONNECTION_STRING` og derefter `ConnectionStrings__Default`. Der er ingen databaseforbindelse i `appsettings.json`, som kan overskrive Dokploy-konfigurationen.

| Variabel | Brug |
| --- | --- |
| `CONNECTION_STRING` | Valgfri fuld Npgsql-forbindelse i Compose; påkrævet ved host-kørsel, medmindre `ConnectionStrings__Default` er sat |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Compose-database og automatisk sammensat API-forbindelse |
| `AUTH_ISSUER` | `https://auth.mercantec.tech` |
| `AUTH_AUDIENCE` | `mercantec-apps` |
| `JWKS_URI` | `https://auth.mercantec.tech/.well-known/jwks.json` |
| `EQUIPMENT_MANAGER_ROLE` | Kommaseparerede Auth-roller til administration; standard `EquipmentManager,Admin` |
| `EQUIPMENT_MANAGER_SUBJECTS` | Valgfri kommaseparerede Auth-`sub` GUID'er med manageradgang uden rolleclaim |
| `CORS_ORIGINS` | Kommaseparerede, eksakte frontend-origins; tom giver ingen adgang fra andre origins |
| `ASPNETCORE_ENVIRONMENT` | `Development` lokalt; `Production` i drift |
| `COMPOSE_PROJECT_NAME` | Adskiller containere, image og database-volume pr. miljø |
| `NETWORK_ALIAS_PREFIX` | Adskiller database- og API-aliaser på det delte Dokploy-netværk |
| `IMAGE_TAG` | Docker-image-tag |
| `API_PORT`, `POSTGRES_PORT` | Lokale hostporte; bruges kun af lokal override |
| `WEB_PORT` | Lokal frontend-port; standard `3000` |
| `API_BASE_URL` | Offentlig API-base til Blazor; standard `/api/`, med afsluttende skråstreg |

`.env` og `.env.*` ignoreres af Git. Kun `.env.example` er versionsstyret. Ved lokal Development-opstart indlæser API'et `.env` fra repository-roden med DotNetEnv. Variabler, som allerede er sat i processen, overskrives ikke. Uden en eksplicit forbindelsesstreng sammensættes den lokale forbindelse med Npgsqls connection-string-builder og de lokale PostgreSQL-variabler. I containere og uden for Development læses ingen lokal `.env`. Undlad at logge forbindelsesstrenge.

## Kør API'et på hosten

Start først en lokal database med miljøfilens password:

```powershell
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d db

dotnet run --project src/Inv.Api -- --migrate
dotnet run --project src/Inv.Api
```

Launch-profilen sætter Development og port `8080`. PostgreSQL-oplysninger og Auth-adresser hentes fra `.env`. Ønskes en ekstern database, sættes `CONNECTION_STRING` eller `ConnectionStrings__Default` eksplicit. Brug `dotnet run --project src/Inv.Api -- --urls http://localhost:8081`, hvis port `8080` allerede bruges af Docker-API'et.

## Blazor-frontend

`Inv.Web` er en selvstændig Blazor WebAssembly-app. Docker bygger de statiske filer og serverer dem med Nginx som ikke-root på port `3000`. `/api/` videresendes med fuld URI til det unikke API-alias. Nginx bruger Docker DNS ved request-tid og kan derfor starte, selvom backendens DNS endnu ikke er klar. Blazor-ruter falder tilbage til `index.html`; manglende framework-filer giver `404`.

Ved containerstart skrives `API_BASE_URL` til den offentlige `client-config.json`. Standardværdien `/api/` bruger samme origin som frontend, så Docker-opsætningen ikke kræver CORS. Filen og `index.html` får `Cache-Control: no-store`. Læg aldrig secrets, forbindelsesstrenge eller tokens i frontendens konfiguration. `API_UPSTREAM` er kun Nginx-konfiguration og sættes af Compose ud fra `NETWORK_ALIAS_PREFIX`.

Til frontendudvikling med Blazors DevServer kan du køre `dotnet run --project src/Inv.Web -- --urls http://localhost:3000`. DevServer har ikke Nginx-proxyen: sæt da `ApiBaseUrl` i `src/Inv.Web/wwwroot/client-config.json` til den lokale API-adresse med `/api/` til sidst og sæt frontendens origin i API'ets `CORS_ORIGINS` via `.env`. Gendan klientkonfigurationen til `/api/` før commit. Den normale Docker-opstart kræver ikke denne ændring.

## Test i Docker

Hvis Windows blokerer lokale assemblies, kan schema- og databasetestene køres i Linux:

```powershell
docker build -f tests/Inv.Database.Tests/Dockerfile -t inv-database-tests:local .
docker run --rm --add-host host.docker.internal:host-gateway -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -v /var/run/docker.sock:/var/run/docker.sock inv-database-tests:local
```

Testcontaineren bruger Docker-socket til at oprette og rydde op i sin egen midlertidige PostgreSQL-container. Den bruger ingen udviklings- eller produktionsdata. Konfigurationstest kontrollerer desuden indlæsning af lokal `.env`, forrang for eksisterende variabler og udelukkelse af lokal config i produktion og containere.

## Migrationer

Migrationer er versionsstyret i `src/Inv.Infrastructure/Data/Migrations`. Runtime opretter ikke schema med `EnsureCreated` og kører ikke migrationer fra normale HTTP-processer. Det separate `migrate`-job og kommandolinjeflaget `--migrate` anvender ventende EF Core-migrationer og afslutter bagefter.

```powershell
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet ef migrations add BeskrivAendringen --project src/Inv.Infrastructure --startup-project src/Inv.Api
dotnet ef migrations has-pending-model-changes --project src/Inv.Infrastructure --startup-project src/Inv.Api
dotnet ef migrations script --idempotent --project src/Inv.Infrastructure --startup-project src/Inv.Api --output artifacts/migrations.sql
```

Generering og modelsammenligning kræver ikke kontakt til PostgreSQL. En syntaktisk gyldig forbindelsesstreng skal stadig være tilgængelig. `InitialInventory` indeholder schemaet for de fem tabeller. Den første migrations historiske model er samlet i dens `.Designer.cs`; den må ikke ændres til at bruge de aktuelle entity-konfigurationer. Nye migrationer genereres normalt med EF-værktøjet.

Databasetestene anvender den faktiske migration i en tom PostgreSQL-container. CI kontrollerer også, at modelsnapshot og den aktuelle model stemmer overens. Ved schemaændringer gennemgås SQL og konsekvenser for eksisterende data inden deployment. Tag backup før ændringer af produktionsdatabasen.

## Dokploy

Grund-Compose genbruger det eksisterende `dokploy-network` som standardnetværk. Der oprettes ingen nye bridge-net på Dokploy-hosten. Forbindelser bruger `${NETWORK_ALIAS_PREFIX}-db` og `${NETWORK_ALIAS_PREFIX}-api`; brug aldrig de generiske navne `db` eller `api` på det delte netværk.

Opret separate Compose-apps for dev og prod med samme filer, separat branch og separat miljøkonfiguration:

| Miljø | Branch | `COMPOSE_PROJECT_NAME` | `NETWORK_ALIAS_PREFIX` |
| --- | --- | --- | --- |
| Dev | `dev`, når miljøet oprettes | `inv-dev` | `inv-dev` |
| Prod | `main` | `inv-prod` | `inv-prod` |

Brug forskellige databasepasswords og passende origins pr. miljø. Produktion er konfigureret i Dokploy-projektet `INV`, Compose-appen `INV Production`, fra `Mercantech/inv` på `main`. Domænet er `https://inv.mercantec.tech`, service `web`, port `3000`, med TLS termineret i Cloudflare. Der er endnu ikke oprettet en `dev`-branch eller et separat dev-miljø.

Produktionsmiljøet bruger `NETWORK_ALIAS_PREFIX=inv-prod`, så interne adresser er `inv-prod-api` og `inv-prod-db`. Databasepassword og øvrige runtime-variabler er gemt i Dokploy; der er ingen produktionshemmeligheder i repositoryet. Dokploy bruger Compose-projektnavnet `inv-prod-bqt1yi`, som også afgrænser database-volumet.

Deployment startes manuelt via Dokploy MCP for Compose-id `_DlanLrFZMU4DlQ7-V_RZ`. Push først til `origin/main`, deploy derefter, gennemgå deployment-loggen og kontrollér `/api/health/ready` på det offentlige domæne. Push alene udløser CI, men ikke automatisk redeployment.

Deploy med:

```text
docker compose up -d --build
```

API'et lytter på port `8080` og kører som den indbyggede ikke-root-bruger i .NET-imaget. `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` er sat i både image og Compose. Base-Compose publicerer ingen hostporte. Dokploy-domænet konfigureres til service `web`, port `3000`; for `*.mags.dk` bruges `certificateType: none` og `https: false` bag Cloudflare. Frontendens Nginx router `/api` til det unikke API-alias via Docker DNS. API'et eksponeres gennem frontendens proxy.

## Mercantec Auth

Integrationen følger [platformens manifest](https://auth.mercantec.tech/.well-known/mercantec-auth.json). JWT-valideringen bruger ASP.NET Cores middleware og IdentityModels JWKS-cache med automatisk refresh ved nøglerotation. Den bygger ikke en lokal login- eller tokenudsteder.

API'et har som standard en fallback-policy, som kræver login. Health checks og OpenAPI i Development er eksplicit offentlige. Nye administrative endpoints skal bruge `RequireAuthorization("EquipmentManager")`; det giver `401` ved manglende/ugyldigt token og `403` ved manglende rolle. Log ikke tokens.

Når frontend tilføjes, skal OAuth-klient og eksakte callback-URIs registreres i Mercantec Auth. Login skal bruge fuld browsernavigation og authorization code + PKCE S256, og frontend skal validere `state`. Tokens gemmes i sessionStorage eller memory, refresh-tokens roteres, og lokal logout rydder tokens og navigerer til appens egen login-side. Der kaldes ikke central signout ved almindelig logout.
