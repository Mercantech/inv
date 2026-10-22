# Mercantec Auth

Frontend er Blazor WebAssembly med authorization code + PKCE S256. API'et validerer RS256-signatur mod cachet JWKS, præcist issuer/audience, udløb, `kid` og et GUID i `sub`. API-roller læses fra `role`. Brugerreferencer i databasen er fortsat `sub`, aldrig e-mail.

## Konfiguration og registrering

- `AUTH_CLIENT_ID`: offentlig OAuth-klient; klienten `inv` er registreret til denne app. Ingen client secret i frontend.
- `AUTH_REDIRECT_URI`: præcis registreret callback-adresse. Tom værdi giver `<frontend-origin>/auth/callback`, fx `http://localhost:3000/auth/callback` lokalt.
- `AUTH_ISSUER`, `AUTH_AUDIENCE`, `JWKS_URI`: backend-validering, se `.env.example`.
- `EQUIPMENT_MANAGER_ROLE`: rollen der giver adgang til `/api/manager-access`.
- `CORS_ORIGINS`: egne tilladte frontend-origins hvis API og SPA kører på forskellige origins.

Registrér klienten som offentlig med PKCE og alle eksakte callback-URI'er hos Mercantec Auth. Tilføj frontendens dev- og produktionsorigin i Auth-platformens `Cors:SpaOrigins`. Disse eksterne registreringer er nødvendige for login og logout-retur. Kontakt: mags@mercantec.dk. Klienten `inv` er oprettet med PKCE og callback-adresserne `http://localhost:3000/auth/callback` og `https://inv.mercantec.tech/auth/callback`. CORS for `https://inv.mercantec.tech` er bekræftet. `http://localhost:3000` mangler i Auth-platformens `Cors:SpaOrigins` og skal tilføjes af platformadministratoren, før lokalt login kan fuldføres. Auth MCP understøtter ikke CORS-opdatering; Dokploy returnerer den relevante miljøkonfiguration som `[REDACTED]`, så den kan ikke sikkert flettes og gemmes.

Docker genererer `client-config.json` fra miljøvariabler ved opstart. Ved Blazor DevServer uden Docker redigeres den offentlige `src/Inv.Web/wwwroot/client-config.json`; standarden bruger `inv` og den aktuelle origin.

## Brugerflow

Åbn `/login` eller vælg **Min konto**. Login bruger fuld navigation til Auth-serveren. `/auth/callback` kontrollerer state og en maksimal flowalder på ti minutter, fjerner callback-parametrene og bytter koden med den oprindelige verifier. Tokens gemmes kun i sessionStorage. `/konto` henter den validerede bruger via `/api/me` og kan kontrollere den konfigurerede administratorrolle (403 uden rollen).

Den beskyttede GET-wrapper `MercantecAuthClient.GetAsync` sender kun tokens til det konfigurerede API, fornyer før udløb og forsøger én refresh og én genafsendelse ved 401. Samtidige refresh-kald deler ét token-kald, og det roterede refresh-token erstatter det gamle. Netværksfejl genafsender ikke automatisk API-kald. Til fremtidige skrivende endpoints skal wrapperen udvides med eksplicit metode/body og hensyn til genafsendelse.

**Log ud** rydder lokale tokens og navigerer til Auth-serverens `/signout?returnUrl=...`, som den konkrete integrationsopgave og manifestet foreskriver. Dette afslutter den fælles Auth-session og kan påvirke andre Mercantec-apps; det afviger bevidst fra projektets generelle regel om lokalt SPA-logout. Returadressen er appens `/login`.

## Verifikation

```powershell
node --test tests/auth.test.mjs
dotnet test Inv.slnx --configuration Release
```

JavaScript-tests dækker PKCE, state, refresh-rotation, samtidighed, begrænset retry, token-modtager og logout. `AuthenticationTests` dækker ugyldig signatur, algoritme, issuer, audience, udløb, ukendt kid, ugyldigt sub samt rolleadgang. De øvrige integrationstests kræver Docker. CI kører begge testsæt og bygger containerne.

Autoritative kilder kontrolleret 22. oktober 2026:

- [Integrationsmanifest](https://auth.mercantec.tech/.well-known/mercantec-auth.json)
- [OIDC discovery](https://auth.mercantec.tech/.well-known/openid-configuration)
- [JWKS](https://auth.mercantec.tech/.well-known/jwks.json)
