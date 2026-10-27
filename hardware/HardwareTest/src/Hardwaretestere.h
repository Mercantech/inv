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

// WiFi-rapport er legacy; browseren gemmer resultatet via Web Serial.
#ifndef ENABLE_API_REPORT
#define ENABLE_API_REPORT false
#endif

#ifndef API_HOST
#define API_HOST "inv.mercantec.tech"
#endif
#ifndef API_PORT
#define API_PORT 443
#endif

// Globale enheds-ID'er (udfyldes af loadDeviceIds)
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
  char note[64];
};
extern TestLogEntry g_testLog[MAX_TEST_LOG];
extern int g_testLogCount;

void resetTestLog();
void recordTest(const char *name, bool ok, const char *note = "", bool skipped = false);
void setTestNote(const char *note);
extern char g_lastNote[64];

// Enheds-ID'er
void loadDeviceIds();
void showDeviceIds(MKRIoTCarrier &carrier);
void printDeviceIds(MKRIoTCarrier &carrier); // load + show (legacy)

// WiFi-credentials fra browser (runtime — ikke Secrets.h)
extern char g_wifiSsid[33];
extern char g_wifiPassword[65];
bool hasWifiCredentials();
void clearWifiCredentials();
bool applyWifiConfigJson(const char *json);
bool testWiFi(MKRIoTCarrier &carrier);

// Host-protokol (JSON-linjer + START/OK/FAIL/ABORT)
void emitReady();
void emitIds();
void emitError(const char *message);
void emitStepBegin(const char *name);
void emitStepAwaitingConfirm(const char *name, const char *prompt);
void emitStepResult(const char *name, bool ok, bool skipped, const char *note);
void emitReading(const char *name, const char *text);
void emitDone(int passed, int failed);
bool readHostCommand(char *buf, size_t bufLen, unsigned long timeoutMs);
void clearAbortRequest();
bool isAbortRequested();
bool isTestsRunning();
void setTestsRunning(bool running);

void maybeBeep(MKRIoTCarrier &carrier);

// Vent på OK/FAIL fra host (browser). Timeout = fejl.
bool confirmPhysical(MKRIoTCarrier &carrier, const __FlashStringHelper *title,
                     const __FlashStringHelper *hint, unsigned long timeoutMs = 90000);

bool reportToApi(MKRIoTCarrier &carrier);

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

bool testHumidityTemperature(MKRIoTCarrier &carrier);
bool testPressureSensor(MKRIoTCarrier &carrier);
bool testIMU(MKRIoTCarrier &carrier);
bool testSDCard(MKRIoTCarrier &carrier);

bool testRelays(MKRIoTCarrier &carrier);
bool testButtons(MKRIoTCarrier &carrier);
bool testLEDs(MKRIoTCarrier &carrier);
bool testBuzzer(MKRIoTCarrier &carrier);

void runAllTests(MKRIoTCarrier &carrier);
void showTestResult(MKRIoTCarrier &carrier, const char *name, bool ok);

#endif // HARDWARETESTERE_H
