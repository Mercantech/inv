#include "Hardwaretestere.h"
#include <string.h>
#include <ctype.h>

static const char *const kTestNames[HW_TEST_COUNT] = {
    "OLED", "Temp/Fugt", "Tryk", "IMU", "SD", "Relay", "Knapper", "LED", "Buzzer", "WiFi"};

static bool g_selected[HW_TEST_COUNT];
static bool g_soundEnabled = ENABLE_SOUND_TEST;
static bool g_selectionCustom = false;

const char *const *testNames() {
  return kTestNames;
}

void resetTestSelection() {
  for (int i = 0; i < HW_TEST_COUNT; i++) g_selected[i] = true;
  g_soundEnabled = ENABLE_SOUND_TEST;
  g_selectionCustom = false;
}

bool isSoundEnabled() {
  return g_soundEnabled;
}

bool isTestSelected(const char *name) {
  if (!g_selectionCustom) return true;
  if (!name) return false;
  for (int i = 0; i < HW_TEST_COUNT; i++) {
    if (strcmp(kTestNames[i], name) == 0) return g_selected[i];
  }
  return false;
}

static bool extractJsonBool(const char *json, const char *key, bool *out) {
  if (!json || !key || !out) return false;
  char needle[40];
  snprintf(needle, sizeof(needle), "\"%s\"", key);
  const char *p = strstr(json, needle);
  if (!p) return false;
  p += strlen(needle);
  while (*p && isspace((unsigned char)*p)) p++;
  if (*p != ':') return false;
  p++;
  while (*p && isspace((unsigned char)*p)) p++;
  if (strncmp(p, "true", 4) == 0) {
    *out = true;
    return true;
  }
  if (strncmp(p, "false", 5) == 0) {
    *out = false;
    return true;
  }
  return false;
}

static bool extractJsonString(const char *json, const char *key, char *out, size_t outLen) {
  if (!json || !key || !out || outLen == 0) return false;
  out[0] = '\0';
  char needle[40];
  snprintf(needle, sizeof(needle), "\"%s\"", key);
  const char *p = strstr(json, needle);
  if (!p) return false;
  p += strlen(needle);
  while (*p && isspace((unsigned char)*p)) p++;
  if (*p != ':') return false;
  p++;
  while (*p && isspace((unsigned char)*p)) p++;
  if (*p != '"') return false;
  p++;
  size_t o = 0;
  while (*p && *p != '"' && o + 1 < outLen) {
    if (*p == '\\' && p[1]) {
      p++;
      out[o++] = *p++;
      continue;
    }
    out[o++] = *p++;
  }
  out[o] = '\0';
  return *p == '"';
}

bool applyStartConfigJson(const char *json) {
  resetTestSelection();
  if (!json) return true;

  bool sound = false;
  if (extractJsonBool(json, "sound", &sound)) {
    g_soundEnabled = sound;
  }

  char testsCsv[192];
  if (!extractJsonString(json, "tests", testsCsv, sizeof(testsCsv))) {
    // Ingen tests-liste → kør alle (lyd fra sound-feltet)
    return true;
  }

  for (int i = 0; i < HW_TEST_COUNT; i++) g_selected[i] = false;
  g_selectionCustom = true;

  char *cursor = testsCsv;
  while (*cursor) {
    while (*cursor == ' ' || *cursor == ',') cursor++;
    if (!*cursor) break;
    char *end = cursor;
    while (*end && *end != ',') end++;
    char saved = *end;
    *end = '\0';
    // trim trailing spaces
    char *trim = end;
    while (trim > cursor && trim[-1] == ' ') {
      trim--;
      *trim = '\0';
    }
    for (int i = 0; i < HW_TEST_COUNT; i++) {
      if (strcmp(kTestNames[i], cursor) == 0) {
        g_selected[i] = true;
        break;
      }
    }
    *end = saved;
    cursor = end;
  }

  // Buzzer valgt ⇒ lyd på (ellers giver buzzer-testen ingen mening)
  if (isTestSelected("Buzzer")) g_soundEnabled = true;

  return true;
}

static void track(const char *name, bool (*testFn)(MKRIoTCarrier &), MKRIoTCarrier &carrier,
                  int &passed, int &failed) {
  if (isAbortRequested()) {
    setTestNote("aborted");
    recordTest(name, false, g_lastNote, false);
    emitStepBegin(name);
    emitStepResult(name, false, false, "aborted");
    failed++;
    return;
  }

  emitStepBegin(name);
  bool ok = testFn(carrier);
  const char *note = g_lastNote;
  recordTest(name, ok, note, false);
  const char *savedNote = g_testLog[g_testLogCount - 1].note;
  emitStepResult(name, ok, false, savedNote);
  if (ok) {
    passed++;
  } else {
    failed++;
  }
}

static void trackSkipped(const char *name, const char *note, int &passed, int &failed) {
  (void)passed;
  (void)failed;
  emitStepBegin(name);
  setTestNote(note);
  recordTest(name, true, note, true);
  emitStepResult(name, true, true, note);
}

static void maybeTrack(const char *name, bool (*testFn)(MKRIoTCarrier &), MKRIoTCarrier &carrier,
                       int &passed, int &failed) {
  if (!isTestSelected(name)) return;
  if (!isAbortRequested()) track(name, testFn, carrier, passed, failed);
}

void runAllTests(MKRIoTCarrier &carrier) {
  int passed = 0;
  int failed = 0;
  char line[24];

  setTestsRunning(true);
  clearAbortRequest();
  resetTestLog();

  maybeTrack("OLED", testOLEDScreen, carrier, passed, failed);
  maybeTrack("Temp/Fugt", testHumidityTemperature, carrier, passed, failed);
  maybeTrack("Tryk", testPressureSensor, carrier, passed, failed);
  maybeTrack("IMU", testIMU, carrier, passed, failed);
  maybeTrack("SD", testSDCard, carrier, passed, failed);
  maybeTrack("Relay", testRelays, carrier, passed, failed);
  maybeTrack("Knapper", testButtons, carrier, passed, failed);
  maybeTrack("LED", testLEDs, carrier, passed, failed);

  if (isTestSelected("Buzzer") && !isAbortRequested()) {
    if (isSoundEnabled()) {
      track("Buzzer", testBuzzer, carrier, passed, failed);
    } else {
      trackSkipped("Buzzer", "disabled_quiet_room", passed, failed);
    }
  }

  if (isTestSelected("WiFi") && !isAbortRequested()) {
    if (hasWifiCredentials()) track("WiFi", testWiFi, carrier, passed, failed);
    else trackSkipped("WiFi", "no_credentials", passed, failed);
  }

  g_testsPassed = passed;
  g_testsFailed = failed;
  emitDone(passed, failed);

  clearDisplay(carrier);
  if (failed == 0 && !isAbortRequested()) {
    drawCenteredF(carrier, F("Alle tests"), 70, 2, ST77XX_GREEN);
    drawCenteredF(carrier, F("fuldfort"), 105, 2, ST77XX_GREEN);
    drawCenteredF(carrier, F("Hardware OK"), 150, 2, ST77XX_WHITE);
    for (int i = 0; i < 5; i++) {
      carrier.leds.fill(carrier.leds.Color(0, 255, 0), 0, 5);
      carrier.leds.show();
      delay(250);
      carrier.leds.clear();
      carrier.leds.show();
      delay(250);
    }
  } else {
    drawCenteredF(carrier, F("Tests FEJL"), 65, 2, ST77XX_RED);
    snprintf(line, sizeof(line), "OK: %d", passed);
    drawCentered(carrier, line, 115, 2, ST77XX_WHITE);
    snprintf(line, sizeof(line), "Fejl: %d", failed);
    drawCentered(carrier, line, 150, 2, ST77XX_WHITE);
    for (int i = 0; i < 5; i++) {
      carrier.leds.fill(carrier.leds.Color(255, 0, 0), 0, 5);
      carrier.leds.show();
      delay(250);
      carrier.leds.clear();
      carrier.leds.show();
      delay(250);
    }
  }

#if ENABLE_API_REPORT
  reportToApi(carrier);
#endif

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Klar"), OLED_CY - 20, 2, ST77XX_CYAN);
  drawCenteredF(carrier, F("Vent pa START"), OLED_CY + 16, 1, ST77XX_WHITE);
  setTestsRunning(false);
  clearAbortRequest();
  resetTestSelection();
}
