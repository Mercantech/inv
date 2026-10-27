#include <Arduino_MKRIoTCarrier.h>
#include "Hardwaretestere.h"

MKRIoTCarrier carrier;

void setup() {
  Serial.begin(9600);
  unsigned long serialWait = millis();
  while (!Serial && (millis() - serialWait < 3000)) {
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

  // Send READY before OLED delay so Web Serial ikke misser handshake.
  emitReady();
  emitIds();

  showDeviceIds(carrier);

  clearDisplay(carrier);
  drawCenteredF(carrier, F("Klar"), OLED_CY - 28, 2, ST77XX_CYAN);
  drawCenteredF(carrier, F("Forbind browser"), OLED_CY + 8, 1, ST77XX_WHITE);
  drawCenteredF(carrier, F("og send START"), OLED_CY + 28, 1, ST77XX_WHITE);

  // Gentag efter display, hvis første READY kom før browseren lyttede.
  emitReady();
  emitIds();
}

void loop() {
  char cmd[200];
  if (!readHostCommand(cmd, sizeof(cmd), 0)) {
    delay(20);
    return;
  }

  if (cmd[0] == '{') {
    // {"v":1,"cmd":"wifi","ssid":"...","password":"..."}
    if (strstr(cmd, "\"cmd\":\"wifi\"") != nullptr || strstr(cmd, "\"cmd\": \"wifi\"") != nullptr) {
      applyWifiConfigJson(cmd);
      return;
    }
    emitError("unknown json command");
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

  if (strcmp(cmd, "STATUS") == 0 || strcmp(cmd, "HELLO") == 0 || strcmp(cmd, "PING") == 0) {
    emitReady();
    emitIds();
    return;
  }

  if (strcmp(cmd, "ABORT") == 0) {
    return;
  }

  if (strcmp(cmd, "OK") == 0 || strcmp(cmd, "FAIL") == 0 || strcmp(cmd, "FEJL") == 0
      || strcmp(cmd, "Y") == 0 || strcmp(cmd, "N") == 0) {
    return;
  }

  emitError("unknown command");
}
