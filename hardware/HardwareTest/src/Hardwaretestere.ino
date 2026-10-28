#include <Arduino_MKRIoTCarrier.h>
#include "Hardwaretestere.h"
#include <string.h>
#include <ctype.h>

MKRIoTCarrier carrier;

static void trimInPlace(char *s) {
  if (!s) return;
  char *start = s;
  while (*start && isspace((unsigned char)*start)) start++;
  if (start != s) memmove(s, start, strlen(start) + 1);
  size_t n = strlen(s);
  while (n > 0 && isspace((unsigned char)s[n - 1])) {
    s[--n] = '\0';
  }
}

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
  resetTestSelection();

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
  // wifi + start-config kan være lange; 512 undgår truncated JSON → unknown command
  char cmd[512];
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
    // {"v":1,"cmd":"start","sound":true,"tests":"OLED,Buzzer,..."}
    if (strstr(cmd, "\"cmd\":\"start\"") != nullptr || strstr(cmd, "\"cmd\": \"start\"") != nullptr) {
      if (isTestsRunning()) {
        emitError("tests already running");
        return;
      }
      applyStartConfigJson(cmd);
      runAllTests(carrier);
      emitReady();
      emitIds();
      return;
    }
    emitError("unknown json command");
    return;
  }

  trimInPlace(cmd);

  if (strcmp(cmd, "START") == 0) {
    if (isTestsRunning()) {
      emitError("tests already running");
      return;
    }
    resetTestSelection();
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
