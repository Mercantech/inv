#include "Hardwaretestere.h"
#include <WiFiNINA.h>

#if __has_include("Secrets.h")
#include "Secrets.h"
#endif

#ifndef WIFI_SSID
#define WIFI_SSID ""
#endif
#ifndef WIFI_PASSWORD
#define WIFI_PASSWORD ""
#endif

static bool wifiConnect() {
  const char *ssid = hasWifiCredentials() ? g_wifiSsid : WIFI_SSID;
  const char *password = hasWifiCredentials() ? g_wifiPassword : WIFI_PASSWORD;
  if (ssid[0] == '\0') {
    Serial.println(F("API: Ingen WiFi-SSID (browser eller Secrets.h) — springer over"));
    return false;
  }

  Serial.print(F("API: Forbinder til WiFi "));
  Serial.println(ssid);

  WiFi.disconnect();
  delay(200);
  WiFi.begin(ssid, password);

  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - start < 20000) {
    Serial.print('.');
    delay(500);
  }
  Serial.println();

  if (WiFi.status() != WL_CONNECTED) {
    Serial.print(F("API: WiFi fejlede, status="));
    Serial.println(WiFi.status());
    return false;
  }

  Serial.print(F("API: WiFi OK, IP="));
  Serial.println(WiFi.localIP());
  Serial.print(F("API: NINA firmware="));
  Serial.println(WiFi.firmwareVersion());
  return true;
}

static void printSslDiag(WiFiSSLClient &client) {
  Serial.println(F("API: TLS/connect fejlede"));
  Serial.println(F("  1) Serial: se NINA firmware ovenfor (helst 1.5.0+)"));
  Serial.println(F("  2) Arduino IDE: Tools > Firmware Updater (NINA)"));
  Serial.println(F("  3) Tools > Upload SSL Root Certificates"));
  Serial.print(F("     tilfoej: "));
  Serial.println(API_HOST);
  Serial.println(F("  4) Test om SSL generelt virker (google.com)"));
#if defined(WIFI_FIRMWARE_LATEST_VERSION) || 1
  // WiFiNINA 2.x: BearSSL fejl-kode (0 hvis ikke understottet)
  Serial.print(F("  BearSSL errorCode="));
  Serial.println(client.errorCodeBearSSL());
#endif
}

static bool sslProbe(const char *host, uint16_t port) {
  WiFiSSLClient probe;
  probe.setTimeout(12000);
  Serial.print(F("API: SSL-probe "));
  Serial.print(host);
  Serial.print(':');
  Serial.print(port);
  Serial.print(F(" ... "));
  bool ok = probe.connect(host, port);
  if (ok) {
    Serial.println(F("OK"));
    probe.stop();
  } else {
    Serial.println(F("FEJL"));
#if 1
    Serial.print(F("  BearSSL errorCode="));
    Serial.println(probe.errorCodeBearSSL());
#endif
  }
  return ok;
}

static String httpRequest(const char *method, const char *path, const char *jsonBody) {
  // DNS-check
  IPAddress resolved;
  Serial.print(F("API: DNS "));
  Serial.print(API_HOST);
  Serial.print(F(" -> "));
  if (WiFi.hostByName(API_HOST, resolved)) {
    Serial.println(resolved);
  } else {
    Serial.println(F("FEJL (DNS)"));
  }

  // Sammenlign: virker TLS overhovedet?
  sslProbe("www.google.com", 443);
  sslProbe(API_HOST, API_PORT);

  WiFiSSLClient client;
  client.setTimeout(20000);

  Serial.print(F("API: "));
  Serial.print(method);
  Serial.print(' ');
  Serial.println(path);

  if (!client.connect(API_HOST, API_PORT)) {
    printSslDiag(client);
    return "";
  }

  size_t bodyLen = jsonBody ? strlen(jsonBody) : 0;

  client.print(method);
  client.print(' ');
  client.print(path);
  client.println(F(" HTTP/1.1"));
  client.print(F("Host: "));
  client.println(API_HOST);
  client.println(F("User-Agent: Arduino-Opla-HardwareTest"));
  client.println(F("Accept: application/json"));
  if (bodyLen > 0) {
    client.println(F("Content-Type: application/json"));
    client.print(F("Content-Length: "));
    client.println(bodyLen);
  }
  client.println(F("Connection: close"));
  client.println();
  if (bodyLen > 0) {
    client.print(jsonBody);
  }

  String statusLine = client.readStringUntil('\n');
  int statusCode = statusLine.substring(9, 12).toInt();
  unsigned long headerStart = millis();
  while (client.connected() || client.available()) {
    if (millis() - headerStart > 20000) {
      Serial.println(F("API: timeout (headers)"));
      client.stop();
      return "";
    }
    if (!client.available()) {
      delay(10);
      continue;
    }
    String line = client.readStringUntil('\n');
    if (line == "\r" || line.length() == 0) {
      break;
    }
  }

  String response = "";
  unsigned long bodyStart = millis();
  while (millis() - bodyStart < 20000) {
    while (client.available()) {
      response += (char)client.read();
      bodyStart = millis();
    }
    if (!client.connected() && !client.available()) {
      break;
    }
    delay(10);
  }
  client.stop();

  if (statusCode < 200 || statusCode >= 300) {
    Serial.print(F("API: afvist, HTTP "));
    Serial.println(statusCode);
    return "";
  }

  Serial.print(F("API: svar "));
  Serial.print(response.length());
  Serial.println(F(" bytes"));
  return response;
}

// Escape every string value instead of interpolating notes/SSID into a fixed buffer.
// This also avoids truncating reports with long sensor notes.
static String jsonString(const char *value) {
  String result = "\"";
  for (const unsigned char *p = (const unsigned char *)value; *p; ++p) {
    if (*p == '"' || *p == '\\') { result += '\\'; result += (char)*p; }
    else if (*p < 0x20) { char escaped[7]; snprintf(escaped, sizeof(escaped), "\\u%04x", *p); result += escaped; }
    else result += (char)*p;
  }
  result += '"';
  return result;
}

bool reportToApi(MKRIoTCarrier &carrier) {
#if !ENABLE_API_REPORT
  Serial.println(F("API: ENABLE_API_REPORT=false — springer over"));
  (void)carrier;
  return false;
#else
  clearDisplay(carrier);
  drawCenteredF(carrier, F("Sender til"), 80, 2, ST77XX_WHITE);
  drawCenteredF(carrier, F("API..."), 120, 2, ST77XX_CYAN);

  if (!wifiConnect()) {
    clearDisplay(carrier);
    drawCenteredF(carrier, F("WiFi/API"), OLED_CY - 20, 2, ST77XX_YELLOW);
    drawCenteredF(carrier, F("skip"), OLED_CY + 16, 2, ST77XX_YELLOW);
    delay(2000);
    return false;
  }

  // Upsert på unikt SAMD chip-ID
  char deviceJson[256];
  snprintf(deviceJson, sizeof(deviceJson),
           "{\"samd_id\":\"%s\",\"mac\":\"%s\",\"ecc_serial\":\"%s\",\"label\":\"Arduino Opla\"}",
           g_samdId, g_macStr, g_eccOk ? g_eccSerial : "");

  String deviceResp = httpRequest("POST", "/api/devices", deviceJson);
  if (deviceResp.length() == 0 || deviceResp.indexOf(g_samdId) < 0) {
    Serial.println(F("API: device upsert fejlede"));
    if (deviceResp.length()) {
      Serial.println(deviceResp);
    }
    clearDisplay(carrier);
    drawCenteredF(carrier, F("API FEJL"), OLED_CY - 12, 2, ST77XX_RED);
    delay(2000);
    return false;
  }

  Serial.print(F("API: enhed ID (SAMD)="));
  Serial.println(g_samdId);

  delay(500); // pause mellem TLS-forbindelser

  bool allOk = (g_testsFailed == 0);
  char testPath[96];
  snprintf(testPath, sizeof(testPath), "/api/devices/%s/tests", g_samdId);

  String testJson;
  testJson.reserve(4096);
  testJson = "{\"passed\":";
  testJson += allOk ? "true" : "false";
  testJson += ",\"passed_count\":"; testJson += g_testsPassed;
  testJson += ",\"failed_count\":"; testJson += g_testsFailed;
  testJson += ",\"report\":{\"firmware\":\"HardwareTest-inv\",\"sound_enabled\":";
  testJson += ENABLE_SOUND_TEST ? "true" : "false";
  testJson += ",\"samd_id\":"; testJson += jsonString(g_samdId);
  testJson += ",\"mac\":"; testJson += jsonString(g_macStr);
  testJson += ",\"ecc_serial\":"; testJson += jsonString(g_eccOk ? g_eccSerial : "");
  testJson += ",\"tests\":[";
  for (int i = 0; i < g_testLogCount; i++) {
    if (i > 0) testJson += ',';
    testJson += "{\"name\":"; testJson += jsonString(g_testLog[i].name);
    testJson += ",\"ok\":"; testJson += g_testLog[i].ok ? "true" : "false";
    testJson += ",\"skipped\":"; testJson += g_testLog[i].skipped ? "true" : "false";
    testJson += ",\"note\":"; testJson += jsonString(g_testLog[i].note);
    testJson += '}';
  }
  testJson += "]}}";
  String testResp = httpRequest("POST", testPath, testJson.c_str());
  if (testResp.length() == 0) {
    Serial.println(F("API: testrapport fejlede"));
    clearDisplay(carrier);
    drawCenteredF(carrier, F("Device OK"), 80, 2, ST77XX_YELLOW);
    drawCenteredF(carrier, F("Test FEJL"), 120, 2, ST77XX_RED);
    delay(2500);
    return false;
  }

  Serial.println(F("API: enhed registreret/opdateret + testrapport gemt"));
  clearDisplay(carrier);
  drawCenteredF(carrier, F("API OK"), 55, 3, ST77XX_GREEN);
  drawCenteredF(carrier, F("SAMD"), 110, 1, ST77XX_WHITE);
  char samdA[20], samdB[20];
  strncpy(samdA, g_samdId, 16);
  samdA[16] = '\0';
  strncpy(samdB, g_samdId + 16, 16);
  samdB[16] = '\0';
  drawCentered(carrier, samdA, 130, 1, ST77XX_YELLOW);
  drawCentered(carrier, samdB, 146, 1, ST77XX_YELLOW);
  delay(3000);
  return true;
#endif
}
