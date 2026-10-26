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

void printDeviceIds(MKRIoTCarrier &carrier) {
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

  Serial.println(F("=== Enheds-ID'er ==="));
  Serial.print(F("SAMD chip ID: "));
  Serial.println(g_samdId);
  Serial.print(F("WiFi MAC:     "));
  Serial.println(g_macStr);
  Serial.print(F("ECCX08 SN:    "));
  Serial.println(g_eccOk ? g_eccSerial : "(ikke fundet)");
  Serial.println(F("===================="));

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Enheds-ID"), 40, 2, ST77XX_CYAN);
  drawCenteredF(carrier, F("SAMD"), 75, 1, ST77XX_WHITE);
  // Lang ID: to linjer á 16 tegn, centreret
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
  delay(4000);
}

void maybeBeep(MKRIoTCarrier &carrier) {
#if ENABLE_SOUND_TEST
  carrier.Buzzer.beep();
#else
  (void)carrier;
#endif
}

static void flushSerialInput() {
  while (Serial.available() > 0) {
    (void)Serial.read();
  }
}

static void toLowerAscii(char *s) {
  for (; *s; ++s) {
    if (*s >= 'A' && *s <= 'Z') {
      *s = (char)(*s - 'A' + 'a');
    }
  }
}

static bool isConfirmOk(const char *line) {
  return strcmp(line, "ok") == 0 || strcmp(line, "y") == 0 || strcmp(line, "yes") == 0
      || strcmp(line, "ja") == 0 || strcmp(line, "1") == 0;
}

static bool isConfirmFail(const char *line) {
  return strcmp(line, "fejl") == 0 || strcmp(line, "fail") == 0 || strcmp(line, "n") == 0
      || strcmp(line, "no") == 0 || strcmp(line, "nej") == 0 || strcmp(line, "0") == 0;
}

bool confirmPhysical(MKRIoTCarrier &carrier, const __FlashStringHelper *title,
                     const __FlashStringHelper *hint, unsigned long timeoutMs) {
  flushSerialInput();

  Serial.println();
  Serial.println(F(">>> FYSISK TJEK — svar i Serial Monitor <<<"));
  Serial.print(F("  "));
  Serial.println(title);
  Serial.print(F("  "));
  Serial.println(hint);
  Serial.println(F("  Skriv OK  (eller y/ja/1)  hvis det virker"));
  Serial.println(F("  Skriv FEJL (eller n/nej/0) hvis det IKKE virker"));
  Serial.println(F("  (Timeout uden svar = FEJL)"));
  Serial.println();

  char titleBuf[24];
  strncpy_P(titleBuf, (PGM_P)title, sizeof(titleBuf) - 1);
  titleBuf[sizeof(titleBuf) - 1] = '\0';

  char line[24];
  size_t len = 0;
  line[0] = '\0';
  unsigned long start = millis();
  unsigned long lastUi = 0;

  while (millis() - start < timeoutMs) {
    unsigned long elapsed = millis() - start;
    if (elapsed - lastUi >= 500 || lastUi == 0) {
      lastUi = elapsed;
      unsigned long leftSec = (timeoutMs - elapsed + 999) / 1000;
      char countdown[16];
      snprintf(countdown, sizeof(countdown), "%lus", leftSec);
      clearDisplay(carrier);
      drawCentered(carrier, titleBuf, 40, 2, ST77XX_CYAN);
      drawCenteredF(carrier, F("Ser det OK ud?"), 85, 2, ST77XX_WHITE);
      drawCenteredF(carrier, F("Skriv i Serial"), 120, 1, ST77XX_YELLOW);
      drawCenteredF(carrier, F("OK eller FEJL"), 140, 1, ST77XX_YELLOW);
      drawCentered(carrier, countdown, 175, 2, ST77XX_WHITE);
    }

    while (Serial.available() > 0) {
      char c = (char)Serial.read();
      if (c == '\r') {
        continue;
      }
      if (c == '\n') {
        line[len] = '\0';
        toLowerAscii(line);
        if (len == 0) {
          continue;
        }
        Serial.print(F("  Modtog: "));
        Serial.println(line);
        if (isConfirmOk(line)) {
          Serial.println(F("  -> Bekraeftet OK"));
          return true;
        }
        if (isConfirmFail(line)) {
          Serial.println(F("  -> Markeret som FEJL"));
          setTestNote("operator_fail");
          return false;
        }
        Serial.println(F("  Ugyldigt — brug OK eller FEJL"));
        len = 0;
        line[0] = '\0';
        continue;
      }
      if (len + 1 < sizeof(line)) {
        line[len++] = c;
      }
    }
    delay(10);
  }

  Serial.println(F("  -> Timeout uden svar = FEJL"));
  setTestNote("timeout_no_serial");
  return false;
}

void showTestResult(MKRIoTCarrier &carrier, const char *name, bool ok) {
  clearDisplay(carrier);
  drawCentered(carrier, name, OLED_CY - 28, 2, ok ? ST77XX_GREEN : ST77XX_RED);
  drawCentered(carrier, ok ? "OK!" : "FEJL!", OLED_CY + 8, 3, ok ? ST77XX_GREEN : ST77XX_RED);
  Serial.print(name);
  Serial.println(ok ? F(": OK") : F(": FEJL"));
  delay(1500);
}
