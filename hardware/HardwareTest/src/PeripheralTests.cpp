#include "Hardwaretestere.h"

bool testRelays(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Relay"), OLED_CY - 12, 2, ST77XX_WHITE);
  Serial.println(F("Relay: lyt efter klik (relay 1, 2, begge)"));
  delay(800);

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Relay 1 ON"), OLED_CY - 12, 2, ST77XX_YELLOW);
  carrier.Relay1.open();
  delay(800);
  carrier.Relay1.close();

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Relay 2 ON"), OLED_CY - 12, 2, ST77XX_YELLOW);
  carrier.Relay2.open();
  delay(800);
  carrier.Relay2.close();

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Begge ON"), OLED_CY - 12, 2, ST77XX_YELLOW);
  carrier.Relay1.open();
  carrier.Relay2.open();
  delay(500);
  carrier.Relay1.close();
  carrier.Relay2.close();

  bool ok = confirmPhysical(carrier, F("Relay"),
                            F("Horte/folte du klik for relay 1, 2 og begge?"),
                            60000);
  if (ok) {
    setTestNote("relay1+2_cycled;confirmed");
  }
  showTestResult(carrier, "Relay", ok);
  return ok;
}

bool testButtons(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Knapper"), 55, 2, ST77XX_WHITE);
  drawCenteredF(carrier, F("Tryk alle 5"), 110, 2, ST77XX_CYAN);
  Serial.println();
  Serial.println(F(">>> FYSISK TJEK — knapper <<<"));
  Serial.println(F("  Tryk alle 5 touch-knapper rundt om skaermen (30s)"));
  Serial.println();

  unsigned long startTime = millis();
  bool buttonPressed[5] = {false, false, false, false, false};
  bool allButtonsPressed = false;
  touchButtons buttons[5] = {TOUCH0, TOUCH1, TOUCH2, TOUCH3, TOUCH4};
  char line[24];

  while (!allButtonsPressed && (millis() - startTime < 30000)) {
    carrier.Buttons.update();
    for (int i = 0; i < 5; i++) {
      if (carrier.Buttons.onTouchDown(buttons[i])) {
        buttonPressed[i] = true;
        clearDisplay(carrier);
        snprintf(line, sizeof(line), "Knap %d", i);
        drawCentered(carrier, line, OLED_CY - 28, 3, ST77XX_GREEN);
        drawCenteredF(carrier, F("OK"), OLED_CY + 20, 2, ST77XX_WHITE);
        Serial.print(F("Knap "));
        Serial.print(i);
        Serial.println(F(" trykket"));
        maybeBeep(carrier);
      }
    }
    allButtonsPressed = true;
    for (int i = 0; i < 5; i++) {
      if (!buttonPressed[i]) {
        allButtonsPressed = false;
        break;
      }
    }
    delay(10);
  }

  int n = 0;
  for (int i = 0; i < 5; i++) {
    if (buttonPressed[i]) n++;
  }
  char note[24];
  snprintf(note, sizeof(note), "touched=%d/5", n);
  setTestNote(note);
  showTestResult(carrier, "Knapper", allButtonsPressed);
  return allButtonsPressed;
}

bool testLEDs(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("LED"), OLED_CY - 12, 3, ST77XX_WHITE);
  Serial.println(F("LED: se alle 5 pixels skifte rod/gron/bla + farvecyklus"));
  delay(500);

  carrier.leds.setBrightness(255);
  char line[16];

  for (int led = 0; led < 5; led++) {
    snprintf(line, sizeof(line), "LED %d", led);
    clearDisplay(carrier);
    drawCentered(carrier, line, OLED_CY - 12, 3, ST77XX_WHITE);
    carrier.leds.clear();
    carrier.leds.setPixelColor(led, 255, 0, 0);
    carrier.leds.show();
    delay(200);
    carrier.leds.setPixelColor(led, 0, 255, 0);
    carrier.leds.show();
    delay(200);
    carrier.leds.setPixelColor(led, 0, 0, 255);
    carrier.leds.show();
    delay(200);
  }

  uint32_t colors[] = {
      carrier.leds.Color(255, 0, 0),
      carrier.leds.Color(0, 255, 0),
      carrier.leds.Color(0, 0, 255),
      carrier.leds.Color(255, 255, 0),
      carrier.leds.Color(0, 255, 255),
      carrier.leds.Color(255, 0, 255)};

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Alle LED"), OLED_CY - 12, 2, ST77XX_WHITE);
  for (int c = 0; c < 6; c++) {
    carrier.leds.fill(colors[c], 0, 5);
    carrier.leds.show();
    delay(300);
  }

  carrier.leds.clear();
  carrier.leds.show();

  bool ok = confirmPhysical(carrier, F("LED"),
                            F("Alle 5 LED lyser korrekt i rod/gron/bla?"),
                            60000);
  if (ok) {
    setTestNote("pixels=5 rgb_cycle;confirmed");
  }
  showTestResult(carrier, "LED", ok);
  return ok;
}

bool testBuzzer(MKRIoTCarrier &carrier) {
#if !ENABLE_SOUND_TEST
  Serial.println(F("Buzzer: sprunget over (ENABLE_SOUND_TEST=false)"));
  return true;
#else
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Buzzer"), OLED_CY - 12, 3, ST77XX_WHITE);
  delay(500);

  int tones[] = {262, 294, 330, 349, 392, 440, 494, 523};
  char line[20];
  for (int i = 0; i < 8; i++) {
    clearDisplay(carrier);
    snprintf(line, sizeof(line), "%d Hz", tones[i]);
    drawCentered(carrier, line, OLED_CY - 12, 3, ST77XX_YELLOW);
    carrier.Buzzer.sound(tones[i]);
    delay(150);
    carrier.Buzzer.noSound();
    delay(50);
  }

  for (int i = 0; i < 3; i++) {
    clearDisplay(carrier);
    drawCenteredF(carrier, F("Beep"), OLED_CY - 12, 3, ST77XX_WHITE);
    carrier.Buzzer.beep();
    delay(300);
  }

  bool ok = confirmPhysical(carrier, F("Buzzer"),
                            F("Horte du skalaen og beep-lydene?"),
                            60000);
  if (ok) {
    setTestNote("scale+beep;confirmed");
  }
  showTestResult(carrier, "Buzzer", ok);
  return ok;
#endif
}
