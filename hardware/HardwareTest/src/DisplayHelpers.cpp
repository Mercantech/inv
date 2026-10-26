#include "Hardwaretestere.h"

void clearDisplay(MKRIoTCarrier &carrier) {
  carrier.display.fillScreen(ST77XX_BLACK);
}

static int16_t clampY(int16_t y, uint16_t textH) {
  if (y < OLED_SAFE_TOP) {
    y = OLED_SAFE_TOP;
  }
  if (y + (int16_t)textH > OLED_SAFE_BOTTOM) {
    y = OLED_SAFE_BOTTOM - (int16_t)textH;
  }
  return y;
}

void drawCentered(MKRIoTCarrier &carrier, const char *text, int16_t y, uint8_t size, uint16_t color) {
  if (!text) {
    return;
  }
  carrier.display.setTextSize(size);
  carrier.display.setTextColor(color);
  carrier.display.setTextWrap(false);

  int16_t x1, y1;
  uint16_t w, h;
  carrier.display.getTextBounds(text, 0, 0, &x1, &y1, &w, &h);

  int16_t x = (OLED_W - (int16_t)w) / 2 - x1;
  y = clampY(y, h);
  carrier.display.setCursor(x, y);
  carrier.display.print(text);
}

void drawCenteredF(MKRIoTCarrier &carrier, const __FlashStringHelper *text, int16_t y, uint8_t size, uint16_t color) {
  char buf[48];
  strncpy_P(buf, (PGM_P)text, sizeof(buf) - 1);
  buf[sizeof(buf) - 1] = '\0';
  drawCentered(carrier, buf, y, size, color);
}

void drawCenteredBlock(MKRIoTCarrier &carrier, const char *const *lines, int count, uint8_t size, uint16_t color) {
  if (count <= 0) {
    return;
  }
  carrier.display.setTextSize(size);
  const int lineH = 8 * size + 4;
  const int blockH = count * lineH;
  int16_t startY = OLED_CY - blockH / 2;
  startY = clampY(startY, blockH);

  for (int i = 0; i < count; i++) {
    drawCentered(carrier, lines[i], startY + i * lineH, size, color);
  }
}
