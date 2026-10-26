#include "Hardwaretestere.h"
#include <math.h>

void initOLEDScreen(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
}

bool testOLEDScreen(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCentered(carrier, "OLED Test", 70, 2, ST77XX_WHITE);
  drawCentered(carrier, "Arduino Opla", 110, 2, ST77XX_CYAN);
  delay(2000);

  testFarver(carrier);
  testTekstStorrelse(carrier);
  drawCircleTest(carrier);
  drawGraphicsTest(carrier);

  // Kode kan ikke se døde pixels, forkerte farver osv. — operatør skal bekræfte.
  bool ok = confirmPhysical(carrier, F("OLED"),
                            F("Farver, tekst og figurer ser korrekte ud pa skaermen?"),
                            90000);
  if (ok) {
    setTestNote("colors+graphics;confirmed");
  }
  showTestResult(carrier, "OLED", ok);
  return ok;
}

void testFarver(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  const char *labels[] = {"Rod", "Gron", "Bla", "Gul", "Cyan", "Magenta"};
  uint16_t colors[] = {
      ST77XX_RED, ST77XX_GREEN, ST77XX_BLUE,
      ST77XX_YELLOW, ST77XX_CYAN, ST77XX_MAGENTA};

  for (int i = 0; i < 6; i++) {
    clearDisplay(carrier);
    drawCentered(carrier, labels[i], OLED_CY - 8, 3, colors[i]);
    delay(700);
  }
}

void testTekstStorrelse(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCentered(carrier, "Size 1", 50, 1, ST77XX_WHITE);
  drawCentered(carrier, "Size 2", 80, 2, ST77XX_WHITE);
  drawCentered(carrier, "Size 3", 120, 3, ST77XX_WHITE);
  delay(2500);
}

void drawCircleTest(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  // Cirkler inden for den runde skærm (ikke ud over kanten)
  carrier.display.fillCircle(OLED_CX - 45, OLED_CY - 35, 28, ST77XX_RED);
  carrier.display.fillCircle(OLED_CX + 45, OLED_CY - 35, 28, ST77XX_GREEN);
  carrier.display.fillCircle(OLED_CX - 45, OLED_CY + 40, 28, ST77XX_YELLOW);
  carrier.display.fillCircle(OLED_CX + 45, OLED_CY + 40, 28, ST77XX_MAGENTA);
  carrier.display.fillCircle(OLED_CX, OLED_CY, 18, ST77XX_CYAN);
  delay(2000);
}

void drawGraphicsTest(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  // Linjer fra centrum — synlige på rund skærm
  for (int a = 0; a < 360; a += 15) {
    float rad = a * 3.14159f / 180.0f;
    int x2 = OLED_CX + (int)(95 * cos(rad));
    int y2 = OLED_CY + (int)(95 * sin(rad));
    carrier.display.drawLine(OLED_CX, OLED_CY, x2, y2, ST77XX_WHITE);
  }
  delay(1000);

  clearDisplay(carrier);
  carrier.display.drawCircle(OLED_CX, OLED_CY, 100, ST77XX_CYAN);
  carrier.display.drawCircle(OLED_CX, OLED_CY, 70, ST77XX_GREEN);
  carrier.display.fillRoundRect(OLED_CX - 40, OLED_CY - 25, 80, 50, 10, ST77XX_YELLOW);
  delay(2000);
}
