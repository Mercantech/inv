#ifndef HARDWARETESTERE_H
#define HARDWARETESTERE_H

#include <Arduino_MKRIoTCarrier.h>

#if __has_include("Secrets.h")
#include "Secrets.h"
#endif

// Stille lokale: sæt til false — springer buzzer-test over og undgår bip ved knapper
#ifndef ENABLE_SOUND_TEST
#define ENABLE_SOUND_TEST false
#endif

// Send resultat til inventarsystemet efter tests (WiFi i Secrets.h).
#ifndef ENABLE_API_REPORT
#define ENABLE_API_REPORT true
#endif

#ifndef API_HOST
#define API_HOST "inv.mercantec.tech"
#endif
#ifndef API_PORT
#define API_PORT 443
#endif

// Globale enheds-ID'er (udfyldes af printDeviceIds)
extern char g_samdId[40];
extern char g_macStr[18];
extern char g_eccSerial[32];
extern bool g_eccOk;

// Test-opsummering (udfyldes af runAllTests)
extern int g_testsPassed;
extern int g_testsFailed;

// Detaljeret testlog til API-historik
#define MAX_TEST_LOG 12
struct TestLogEntry {
  char name[20];
  bool ok;
  bool skipped;
  char note[64]; // frie detaljer / sensorværdier
};
extern TestLogEntry g_testLog[MAX_TEST_LOG];
extern int g_testLogCount;

void resetTestLog();
void recordTest(const char *name, bool ok, const char *note = "", bool skipped = false);
void setTestNote(const char *note);
extern char g_lastNote[64];

// Enheds-ID'er
void printDeviceIds(MKRIoTCarrier &carrier);

// Bip kun hvis ENABLE_SOUND_TEST er true
void maybeBeep(MKRIoTCarrier &carrier);

// Registrér/opdatér enhed + testrapport via HTTPS
bool reportToApi(MKRIoTCarrier &carrier);

// OLED — rund 240x240 (centreret tekst, sikker zone)
#define OLED_W 240
#define OLED_H 240
#define OLED_CX 120
#define OLED_CY 120
#define OLED_SAFE_TOP 32
#define OLED_SAFE_BOTTOM 208

void clearDisplay(MKRIoTCarrier &carrier);
void drawCentered(MKRIoTCarrier &carrier, const char *text, int16_t y, uint8_t size, uint16_t color);
void drawCenteredF(MKRIoTCarrier &carrier, const __FlashStringHelper *text, int16_t y, uint8_t size, uint16_t color);
void drawCenteredBlock(MKRIoTCarrier &carrier, const char *const *lines, int count, uint8_t size, uint16_t color);

void initOLEDScreen(MKRIoTCarrier &carrier);
bool testOLEDScreen(MKRIoTCarrier &carrier);
void testFarver(MKRIoTCarrier &carrier);
void testTekstStorrelse(MKRIoTCarrier &carrier);
void drawCircleTest(MKRIoTCarrier &carrier);
void drawGraphicsTest(MKRIoTCarrier &carrier);

// Sensorer (returnerer true = bestået)
bool testHumidityTemperature(MKRIoTCarrier &carrier);
bool testPressureSensor(MKRIoTCarrier &carrier);
bool testIMU(MKRIoTCarrier &carrier);
bool testSDCard(MKRIoTCarrier &carrier);

// Periferi
bool testRelays(MKRIoTCarrier &carrier);
bool testButtons(MKRIoTCarrier &carrier);
bool testLEDs(MKRIoTCarrier &carrier);
bool testBuzzer(MKRIoTCarrier &carrier);

// Hovedtest
void runAllTests(MKRIoTCarrier &carrier);

// Hjælpere til resultatvisning
void showTestResult(MKRIoTCarrier &carrier, const char *name, bool ok);

#endif // HARDWARETESTERE_H
