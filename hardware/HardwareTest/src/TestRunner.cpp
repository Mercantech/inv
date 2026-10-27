#include "Hardwaretestere.h"

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
  // recordTest clears g_lastNote — capture via record then use log entry
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

void runAllTests(MKRIoTCarrier &carrier) {
  int passed = 0;
  int failed = 0;
  char line[24];

  setTestsRunning(true);
  clearAbortRequest();
  resetTestLog();

  track("OLED", testOLEDScreen, carrier, passed, failed);
  if (!isAbortRequested()) track("Temp/Fugt", testHumidityTemperature, carrier, passed, failed);
  if (!isAbortRequested()) track("Tryk", testPressureSensor, carrier, passed, failed);
  if (!isAbortRequested()) track("IMU", testIMU, carrier, passed, failed);
  if (!isAbortRequested()) track("SD", testSDCard, carrier, passed, failed);
  if (!isAbortRequested()) track("Relay", testRelays, carrier, passed, failed);
  if (!isAbortRequested()) track("Knapper", testButtons, carrier, passed, failed);
  if (!isAbortRequested()) track("LED", testLEDs, carrier, passed, failed);
#if ENABLE_SOUND_TEST
  if (!isAbortRequested()) track("Buzzer", testBuzzer, carrier, passed, failed);
#else
  if (!isAbortRequested()) trackSkipped("Buzzer", "disabled_quiet_room", passed, failed);
#endif
  if (!isAbortRequested()) {
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
}
