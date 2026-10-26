#include "Hardwaretestere.h"
#include <WiFiNINA.h>
#include <ArduinoECCX08.h>
#include <string.h>

char g_samdId[40] = {0};
char g_macStr[18] = {0};
char g_eccSerial[32] = {0};
bool g_eccOk = false;

int g_testsPassed = 0;
int g_testsFailed = 0;

TestLogEntry g_testLog[MAX_TEST_LOG];
int g_testLogCount = 0;
char g_lastNote[64] = {0};

void resetTestLog() {
  g_testLogCount = 0;
  g_lastNote[0] = '\0';
  memset(g_testLog, 0, sizeof(g_testLog));
}

void setTestNote(const char *note) {
  strncpy(g_lastNote, note ? note : "", sizeof(g_lastNote) - 1);
  g_lastNote[sizeof(g_lastNote) - 1] = '\0';
}

void recordTest(const char *name, bool ok, const char *note, bool skipped) {
  if (g_testLogCount >= MAX_TEST_LOG) {
    return;
  }
  TestLogEntry &e = g_testLog[g_testLogCount++];
  strncpy(e.name, name ? name : "?", sizeof(e.name) - 1);
  e.name[sizeof(e.name) - 1] = '\0';
  e.ok = ok;
  e.skipped = skipped;
  const char *n = (note && note[0]) ? note : g_lastNote;
  strncpy(e.note, n ? n : "", sizeof(e.note) - 1);
  e.note[sizeof(e.note) - 1] = '\0';
  g_lastNote[0] = '\0';
}

static void printSamdIdHex(char *out, size_t outLen) {
  volatile uint32_t *id = (volatile uint32_t *)0x0080A00C;
  snprintf(out, outLen, "%08lX%08lX%08lX%08lX",
           (unsigned long)id[0], (unsigned long)id[1],
           (unsigned long)id[2], (unsigned long)id[3]);
}

void loadDeviceIds() {
  printSamdIdHex(g_samdId, sizeof(g_samdId));

  byte mac[6];
  WiFi.macAddress(mac);
  snprintf(g_macStr, sizeof(g_macStr), "%02X:%02X:%02X:%02X:%02X:%02X",
           mac[5], mac[4], mac[3], mac[2], mac[1], mac[0]);

  g_eccOk = false;
  strncpy(g_eccSerial, "-", sizeof(g_eccSerial) - 1);
  if (ECCX08.begin()) {
    String sn = ECCX08.serialNumber();
    if (sn.length() > 0) {
      sn.toCharArray(g_eccSerial, sizeof(g_eccSerial));
      g_eccOk = true;
    }
  }
}

void showDeviceIds(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Enheds-ID"), 40, 2, ST77XX_CYAN);
  drawCenteredF(carrier, F("SAMD"), 75, 1, ST77XX_WHITE);
  char samdA[20], samdB[20];
  strncpy(samdA, g_samdId, 16);
  samdA[16] = '\0';
  strncpy(samdB, g_samdId + 16, 16);
  samdB[16] = '\0';
  drawCentered(carrier, samdA, 92, 1, ST77XX_YELLOW);
  drawCentered(carrier, samdB, 106, 1, ST77XX_YELLOW);
  drawCenteredF(carrier, F("MAC"), 128, 1, ST77XX_WHITE);
  drawCentered(carrier, g_macStr, 144, 1, ST77XX_YELLOW);
  drawCenteredF(carrier, F("ECCX08"), 166, 1, ST77XX_WHITE);
  drawCentered(carrier, g_eccOk ? g_eccSerial : "-", 182, 1, ST77XX_YELLOW);
  delay(2000);
}

void printDeviceIds(MKRIoTCarrier &carrier) {
  loadDeviceIds();
  showDeviceIds(carrier);
}

void maybeBeep(MKRIoTCarrier &carrier) {
#if ENABLE_SOUND_TEST
  carrier.Buzzer.beep();
#else
  (void)carrier;
#endif
}

void showTestResult(MKRIoTCarrier &carrier, const char *name, bool ok) {
  clearDisplay(carrier);
  drawCentered(carrier, name, OLED_CY - 28, 2, ok ? ST77XX_GREEN : ST77XX_RED);
  drawCentered(carrier, ok ? "OK!" : "FEJL!", OLED_CY + 8, 3, ok ? ST77XX_GREEN : ST77XX_RED);
  delay(800);
}
