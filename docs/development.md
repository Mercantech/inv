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
| `EQUIPMENT_MANAGER_ROLE` | Mercantec-rolle for den kommende administration; standard `EquipmentManager` |
| `CORS_ORIGINS` | Kommaseparerede, eksakte frontend-origins; tom giver ingen adgang fra andre origins |
| `ASPNETCORE_ENVIRONMENT` | `Development` lokalt; `Production` i drift |
| `COMPOSE_PROJECT_NAME` | Adskiller containere, image og database-volume pr. miljø |
| `NETWORK_ALIAS_PREFIX` | Adskiller database- og API-aliaser på det delte Dokploy-netværk |
| `IMAGE_TAG` | Docker-image-tag |
| `API_PORT`, `POSTGRES_PORT` | Lokale hostporte; bruges kun af lokal override |

`.env` og `.env.*` ignoreres af Git. Kun `.env.example` er versionsstyret. .NET indlæser ikke Compose-`.env` automatisk; ved `dotnet run` skal variablerne sættes i shellen, som vist nedenfor. Undlad at logge forbindelsesstrenge.

## Kør API'et på hosten

Start først en lokal database med miljøfilens password:

```powershell
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d db

$env:CONNECTION_STRING='Host=localhost;Port=5432;Database=inv;Username=inv;Password=<password-fra-din-lokale-env>'
$env:AUTH_ISSUER='https://auth.mercantec.tech'
$env:AUTH_AUDIENCE='mercantec-apps'
$env:JWKS_URI='https://auth.mercantec.tech/.well-known/jwks.json'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE='false'
$env:ASPNETCORE_URLS='http://localhost:8080'

dotnet run --project src/Inv.Api -- --migrate
dotnet run --project src/Inv.Api
```

Indsæt det faktiske lokale password i variablen. Ved ændrede port-/databasenavne skal forbindelsesstrengen følge med.

## Migrationer

Migrationer er versionsstyret i `src/Inv.Infrastructure/Data/Migrations`. Runtime opretter ikke schema med `EnsureCreated` og kører ikke migrationer fra normale HTTP-processer. Det separate `migrate`-job og kommandolinjeflaget `--migrate` anvender ventende EF Core-migrationer og afslutter bagefter.

```powershell
dotnet tool restore
# Sæt CONNECTION_STRING i miljøet først.
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

Brug forskellige databasepasswords og passende origins pr. miljø. Der er endnu ikke oprettet en `dev`-branch, hosting eller domæner som del af grundstrukturen.

Deploy med:

```text
docker compose up -d --build
```

API'et lytter på port `8080` og kører som den indbyggede ikke-root-bruger i .NET-imaget. `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` er sat i både image og Compose. Base-Compose publicerer ingen hostporte. Hvis API'et eksponeres direkte, konfigureres Dokploy-domænet til service `api`, port `8080`; for `*.mags.dk` bruges `certificateType: none` og `https: false` bag Cloudflare. En kommende frontend skal route `/api` til det unikke API-alias via Docker DNS med request-time resolution og selv lytte på port `3000`.

## Mercantec Auth

Integrationen følger [platformens manifest](https://auth.mercantec.tech/.well-known/mercantec-auth.json). JWT-valideringen bruger ASP.NET Cores middleware og IdentityModels JWKS-cache med automatisk refresh ved nøglerotation. Den bygger ikke en lokal login- eller tokenudsteder.

API'et har som standard en fallback-policy, som kræver login. Health checks og OpenAPI i Development er eksplicit offentlige. Nye administrative endpoints skal bruge `RequireAuthorization("EquipmentManager")`; det giver `401` ved manglende/ugyldigt token og `403` ved manglende rolle. Log ikke tokens.

Når frontend tilføjes, skal OAuth-klient og eksakte callback-URIs registreres i Mercantec Auth. Login skal bruge fuld browsernavigation og authorization code + PKCE S256, og frontend skal validere `state`. Tokens gemmes i sessionStorage eller memory, refresh-tokens roteres, og lokal logout rydder tokens og navigerer til appens egen login-side. Der kaldes ikke central signout ved almindelig logout.
