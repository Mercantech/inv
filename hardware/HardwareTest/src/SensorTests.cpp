#include "Hardwaretestere.h"
#include <SD.h>
#include <math.h>

bool testHumidityTemperature(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Temp & Fugt"), OLED_CY - 12, 2, ST77XX_WHITE);
  delay(1000);

  bool ok = false;
  float temperature = 0;
  float humidity = 0;
  char line[32];

  for (int i = 0; i < 5; i++) {
    temperature = carrier.Env.readTemperature();
    humidity = carrier.Env.readHumidity();

    clearDisplay(carrier);
    drawCenteredF(carrier, F("Temp & Fugt"), 45, 2, ST77XX_CYAN);
    snprintf(line, sizeof(line), "%.1f C", temperature);
    drawCentered(carrier, line, 100, 3, ST77XX_WHITE);
    snprintf(line, sizeof(line), "%.0f %%", humidity);
    drawCentered(carrier, line, 145, 2, ST77XX_WHITE);

    char reading[40];
    snprintf(reading, sizeof(reading), "%.1f C · %.0f %%", temperature, humidity);
    emitReading("Temp/Fugt", reading);

    if (temperature > -20 && temperature < 60 && humidity >= 0 && humidity <= 100) {
      ok = true;
    }
    delay(1000);
  }

  snprintf(line, sizeof(line), "temp_c=%.2f humidity=%.1f", temperature, humidity);
  setTestNote(line);
  showTestResult(carrier, "Temp/Fugt", ok);
  return ok;
}

bool testPressureSensor(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Tryk"), OLED_CY - 12, 2, ST77XX_WHITE);
  delay(1000);

  bool ok = false;
  float pressure = 0;
  char line[32];

  for (int i = 0; i < 5; i++) {
    pressure = carrier.Pressure.readPressure();

    clearDisplay(carrier);
    drawCenteredF(carrier, F("Tryk"), 50, 2, ST77XX_CYAN);
    snprintf(line, sizeof(line), "%.2f kPa", pressure);
    drawCentered(carrier, line, 105, 2, ST77XX_WHITE);
    snprintf(line, sizeof(line), "%.0f hPa", pressure * 10);
    drawCentered(carrier, line, 145, 2, ST77XX_WHITE);

    char reading[40];
    snprintf(reading, sizeof(reading), "%.2f kPa · %.0f hPa", pressure, pressure * 10);
    emitReading("Tryk", reading);

    if (pressure > 20 && pressure < 2000) {
      ok = true;
    }
    delay(1000);
  }

  snprintf(line, sizeof(line), "pressure_kpa=%.2f", pressure);
  setTestNote(line);
  showTestResult(carrier, "Tryk", ok);
  return ok;
}

bool testIMU(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("IMU"), 55, 3, ST77XX_WHITE);
  drawCenteredF(carrier, F("Bevoeg kortet"), 120, 2, ST77XX_CYAN);
  delay(2000);

  float ax = 0, ay = 0, az = 0, gx = 0, gy = 0, gz = 0;
  bool accelOk = false;
  bool gyroOk = false;
  unsigned long startTime = millis();
  unsigned long lastEmit = 0;
  char line[40];

  while (millis() - startTime < 10000) {
    clearDisplay(carrier);
    drawCenteredF(carrier, F("IMU"), 40, 2, ST77XX_CYAN);

    if (carrier.IMUmodule.accelerationAvailable()) {
      carrier.IMUmodule.readAcceleration(ax, ay, az);
      snprintf(line, sizeof(line), "A %.1f %.1f %.1f", ax, ay, az);
      drawCentered(carrier, line, 90, 1, ST77XX_WHITE);
      if (fabs(ax) < 5 && fabs(ay) < 5 && fabs(az) < 5) {
        accelOk = true;
      }
    }

    if (carrier.IMUmodule.gyroscopeAvailable()) {
      carrier.IMUmodule.readGyroscope(gx, gy, gz);
      snprintf(line, sizeof(line), "G %.0f %.0f %.0f", gx, gy, gz);
      drawCentered(carrier, line, 120, 1, ST77XX_WHITE);
      gyroOk = true;
    }

    if (millis() - lastEmit >= 400) {
      lastEmit = millis();
      char reading[48];
      snprintf(reading, sizeof(reading), "A %.1f %.1f %.1f · G %.0f %.0f %.0f", ax, ay, az, gx, gy, gz);
      emitReading("IMU", reading);
    }

    delay(100);
  }

  bool ok = accelOk && gyroOk;
  snprintf(line, sizeof(line), "accel=%.2f,%.2f,%.2f gyro=%.1f,%.1f,%.1f", ax, ay, az, gx, gy, gz);
  setTestNote(line);
  showTestResult(carrier, "IMU", ok);
  return ok;
}

bool testSDCard(MKRIoTCarrier &carrier) {
  clearDisplay(carrier);
  drawCenteredF(carrier, F("SD Kort"), OLED_CY - 12, 2, ST77XX_WHITE);
  delay(800);

  bool ok = false;
  const char *path = "oplatest.txt";
  const char *payload = "opla-ok";

  File f = SD.open(path, FILE_WRITE);
  if (f) {
    f.print(payload);
    f.close();

    f = SD.open(path, FILE_READ);
    if (f) {
      char buf[16] = {0};
      int n = f.read(buf, sizeof(buf) - 1);
      f.close();
      SD.remove(path);
      if (n > 0 && strstr(buf, "opla") != NULL) {
        ok = true;
      }
    }
  }

  if (!ok) {
    Serial.println(F("SD: ingen kort eller I/O-fejl"));
    setTestNote("rw=fail");
  } else {
    Serial.println(F("SD: skriv/laes OK"));
    setTestNote("rw=ok");
  }

  showTestResult(carrier, "SD Kort", ok);
  return ok;
}
