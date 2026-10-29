# Udstyrslabels på DYMO LetraTag LT-200B

Printerdelen er portet fra [Mercantech/Dymo2026](https://github.com/Mercantech/Dymo2026), commit d21e82d9c59d6e58fb89d6054f1f163e19f6bc2a. Vi genbruger den browserbaserede BLE-driver og labelorienteringen i Blazor-appen; der kører ingen separat React-app, lokal printserver eller ny loginløsning.

## Brug

1. Åbn det konkrete udstyr som EquipmentManager/Admin og vælg **Print label**.
2. Kontrollér forhåndsvisningen: kort udstyrs-ID (fx 109), udstyrsnavn og QR-kode. Lange navne forkortes med ellipse; ID og QR beskæres ikke.
3. Kontrollér **Appens offentlige adresse**. Den skal være den origin, hvor denne appversion er deployet, fx https://inv.mercantec.tech. Vælg **Opdatér forhåndsvisning**, hvis adressen ændres.
4. Tænd LT-200B med 12 mm LT-tape, og brug Chrome eller Edge på HTTPS eller localhost med Bluetooth aktiveret. Firefox og Safari understøtter ikke Web Bluetooth-print. Scanning og visning af udstyr kræver ikke Web Bluetooth.
5. Vælg **Vælg og forbind printer**, og vælg din LetraTag i browserens Bluetooth-dialog. Denne dialog åbnes kun fra brugerens klik.
6. Vælg **Print label 109**. Der sendes én label ad gangen. Kontrollér den fysiske udskrift og scan QR-koden med telefonen.

Print genforsøges aldrig automatisk: printeren kan have modtaget labelen, selvom et Bluetooth-svar går tabt. Kontrollér printeren før et nyt forsøg. Manglende printerkvittering vises særskilt. Printerforbindelsen kan genbruges mellem udstyr i samme browserfane eller afsluttes med **Afbryd printer**.

## QR og login

QR indeholder kun en stabil adresse som HTTPS://INV.MERCANTEC.TECH/E/109, ingen tokens, brugeroplysninger eller Auth-parametre. Store bogstaver anvendes med vilje, så QR kan bruge det mere kompakte alfanumeriske format. DNS, URL-skema og appens rute er ikke følsomme over for store/små bogstaver.

Ruten /e/{kort-ID} åbner den aktuelle udstyrsdetaljeside via det faste ShortId. Opslaget på /api/equipment/by-number/{kort-ID} kræver login. Hvis brugeren ikke er logget ind ved scanning, gemmes en valideret lokal returadresse i PKCE-flowet, og brugeren vender tilbage til det scannede udstyr efter login. Eksterne returadresser og loginløkker afvises. Ukendte eller slettede ID'er viser en forståelig besked; QR-koden overtager ikke et nyt udstyrs ID.

## Tape og QR-begrænsninger

LT-200B har et 32-rækkers protokolbillede med ca. 30 brugbare rækker. Derfor bruges en kort udstyrsrute, QR-fejlkorrektion L og højst 25 moduler på tværs. Længere adresser afvises frem for at beskære QR. Layoutet bevarer Dymo2026-kalibreringen med feed/head-forhold 2,55 og bruger mindst 25 mm feedlængde plus driverens fremføring.

QR-version 2 fylder 25 rækker og har 3/4 blanke protokolrækker over/under sig; den fulde standard-quiet-zone på fire moduler kan derfor ikke garanteres på tværs af dette printhoved. Fysisk læsbarhed afhænger også af kassettens uprintede kant, dotgeometri og kontrast. Den digitale label er QR-dekodet i tests, men det erstatter ikke en prøve på netop printeren/tapen. Hvis scanning er upålidelig, kræves en kortere offentlig origin, så QR kan være version 1 med større margener, eller en printer med større printbredde. Vi markerer ikke den fysiske accept som bestået uden en rigtig prøve.

## Konfiguration og build

LABEL_PUBLIC_BASE_URL angiver den offentlige app-origin i runtime-konfigurationen. Tom værdi bruger browserens egen origin. Ved lokal udvikling bør den sættes til den offentlige appadresse, da telefonens localhost ikke er udviklingscomputeren. QR-ruten og loginreturen skal være deployet på den valgte adresse, før end-to-end scanning kan virke der.

Kilde: src/Inv.Web/printing/compose.mjs og session.mjs. Browserbundlen wwwroot/letratag.js og licensnotitser ligger i repoet, så almindelig dotnet build ikke kræver Node. Docker bygger selv bundlen med Node 24 og npm ci i et separat buildtrin; runtime er fortsat Nginx/Blazor og .NET API. Der kræves hverken Bluetooth på Dokploy-serveren, nye netværk eller databaseændringer.

Ved ændring af JavaScript:

    npm ci --prefix src/Inv.Web/printing
    npm run build --prefix src/Inv.Web/printing
    node --test tests/auth.test.mjs tests/labels.test.mjs
    dotnet test Inv.slnx --configuration Release

Biblioteker er låst via package-lock.json: @thermal-label/letratag-web og @thermal-label/transport 0.6.1 samt qrcode 1.5.4. Tredjepartslicenser følger bundlen i wwwroot/letratag.LICENSE.txt. CI bygger bundlen og tester label-, Bluetooth- og loginlogik.

## Verifikation og resterende accept

- 76 .NET-tests bestået, herunder stabilt kort-ID-opslag, loginbeskyttelse og renderet printpanel med eksplicit forbind/print og annullering.
- 16 JavaScript-tests bestået, herunder dekodning af den reelle rasterforhåndsvisning, alle QR-moduler bevaret i printbilledet, Bluetooth-pakkegrænser, dubletbeskyttelse, ingen automatisk retry og retur til scannet udstyr efter PKCE-login.
- **Afventer fysisk accept:** Udskriv på den konkrete LT-200B/12 mm-kassette, kontrollér ID/navn og scan både med og uden eksisterende login. #13 forbliver åbent indtil dette er bekræftet.

Driverdokumentation: https://thermal-label.github.io/letratag/web og https://thermal-label.github.io/letratag/core.
