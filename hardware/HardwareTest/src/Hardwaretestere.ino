#include <Arduino_MKRIoTCarrier.h>
#include "Hardwaretestere.h"

MKRIoTCarrier carrier;

void setup() {
  Serial.begin(9600);
  unsigned long serialWait = millis();
  while (!Serial && (millis() - serialWait < 5000)) {
    ;
  }

  carrier.withCase();

  if (!carrier.begin()) {
    emitError("carrier.begin failed");
    while (true) {
      delay(1000);
    }
  }

  initOLEDScreen(carrier);
  loadDeviceIds();
  showDeviceIds(carrier);

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Klar"), OLED_CY - 28, 2, ST77XX_CYAN);
  drawCenteredF(carrier, F("Forbind browser"), OLED_CY + 8, 1, ST77XX_WHITE);
  drawCenteredF(carrier, F("og send START"), OLED_CY + 28, 1, ST77XX_WHITE);

  emitReady();
  emitIds();
}

void loop() {
  char cmd[24];
  if (!readHostCommand(cmd, sizeof(cmd), 0)) {
    delay(20);
    return;
  }

  if (strcmp(cmd, "START") == 0) {
    if (isTestsRunning()) {
      emitError("tests already running");
      return;
    }
    runAllTests(carrier);
    emitReady();
    emitIds();
    return;
  }

  if (strcmp(cmd, "ABORT") == 0) {
    // Already flagged inside readHostCommand; ignore when idle
    return;
  }

  if (strcmp(cmd, "OK") == 0 || strcmp(cmd, "FAIL") == 0 || strcmp(cmd, "FEJL") == 0
      || strcmp(cmd, "Y") == 0 || strcmp(cmd, "N") == 0) {
    // Confirmations are only consumed inside confirmPhysical
    return;
  }

  emitError("unknown command");
}
