#include "Hardwaretestere.h"

static void track(const char *name, bool ok, int &passed, int &failed, bool skipped = false) {
  recordTest(name, ok, g_lastNote, skipped);
  if (skipped) {
    return;
  }
  if (ok) {
    passed++;
  } else {
    failed++;
  }
}

void runAllTests(MKRIoTCarrier &carrier) {
  int passed = 0;
  int failed = 0;
  char line[24];

  resetTestLog();

#if ENABLE_SOUND_TEST
  Serial.println(F("Lyd: TIL (buzzer + bip)"));
#else
  Serial.println(F("Lyd: FRA (stille lokale — buzzer sprunget over)"));
#endif

  track("OLED", testOLEDScreen(carrier), passed, failed);
  track("Temp/Fugt", testHumidityTemperature(carrier), passed, failed);
  track("Tryk", testPressureSensor(carrier), passed, failed);
  track("IMU", testIMU(carrier), passed, failed);
  track("SD", testSDCard(carrier), passed, failed);
  track("Relay", testRelays(carrier), passed, failed);
  track("Knapper", testButtons(carrier), passed, failed);
  track("LED", testLEDs(carrier), passed, failed);
#if ENABLE_SOUND_TEST
  track("Buzzer", testBuzzer(carrier), passed, failed);
#else
  setTestNote("disabled_quiet_room");
  track("Buzzer", true, passed, failed, true);
  Serial.println(F("Buzzer: SPRUNGET OVER"));
#endif

  g_testsPassed = passed;
  g_testsFailed = failed;

  Serial.println(F("=== Test-opsummering ==="));
  Serial.print(F("Bestået: "));
  Serial.println(passed);
  Serial.print(F("Fejlet:  "));
  Serial.println(failed);
  for (int i = 0; i < g_testLogCount; i++) {
    Serial.print(F("  - "));
    Serial.print(g_testLog[i].name);
    Serial.print(F(": "));
    if (g_testLog[i].skipped) {
      Serial.print(F("SKIP"));
    } else {
      Serial.print(g_testLog[i].ok ? F("OK") : F("FAIL"));
    }
    if (g_testLog[i].note[0]) {
      Serial.print(F(" ("));
      Serial.print(g_testLog[i].note);
      Serial.print(')');
    }
    Serial.println();
  }

  clearDisplay(carrier);
  if (failed == 0) {
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

  reportToApi(carrier);
}
