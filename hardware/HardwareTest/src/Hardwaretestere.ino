#include <Arduino_MKRIoTCarrier.h>
#include "Hardwaretestere.h"

MKRIoTCarrier carrier;

void setup() {
  Serial.begin(9600);
  unsigned long serialWait = millis();
  while (!Serial && (millis() - serialWait < 3000)) {
    ; // timeout så boardet ikke hænger uden USB-monitor
  }

  Serial.println(F("Arduino Opla Hardware Tester - Fuld Test"));

  carrier.withCase(); // kabinet på — højere touch-følsomhed til knapperne

  if (!carrier.begin()) {
    Serial.println(F("FEJL: carrier.begin() fejlede"));
    while (true) {
      delay(1000);
    }
  }

  initOLEDScreen(carrier);
  printDeviceIds(carrier);
  runAllTests(carrier);
}

void loop() {
  delay(1000);
}
