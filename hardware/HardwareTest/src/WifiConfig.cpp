#include "Hardwaretestere.h"
#include <WiFiNINA.h>
#include <string.h>
#include <ctype.h>

char g_wifiSsid[33] = {0};
char g_wifiPassword[65] = {0};

static bool extractJsonString(const char *json, const char *key, char *out, size_t outLen) {
  if (!json || !key || !out || outLen == 0) return false;
  out[0] = '\0';

  char needle[40];
  snprintf(needle, sizeof(needle), "\"%s\"", key);
  const char *p = strstr(json, needle);
  if (!p) return false;
  p += strlen(needle);
  while (*p && isspace((unsigned char)*p)) p++;
  if (*p != ':') return false;
  p++;
  while (*p && isspace((unsigned char)*p)) p++;
  if (*p != '"') return false;
  p++;

  size_t o = 0;
  while (*p && *p != '"' && o + 1 < outLen) {
    if (*p == '\\' && p[1]) {
      p++;
      if (*p == '"' || *p == '\\' || *p == '/') out[o++] = *p;
      else if (*p == 'n') out[o++] = '\n';
      else if (*p == 't') out[o++] = '\t';
      else out[o++] = *p;
      p++;
      continue;
    }
    out[o++] = *p++;
  }
  out[o] = '\0';
  return *p == '"';
}

bool hasWifiCredentials() {
  return g_wifiSsid[0] != '\0';
}

void clearWifiCredentials() {
  memset(g_wifiSsid, 0, sizeof(g_wifiSsid));
  memset(g_wifiPassword, 0, sizeof(g_wifiPassword));
}

bool applyWifiConfigJson(const char *json) {
  char ssid[33];
  char password[65];
  if (!extractJsonString(json, "ssid", ssid, sizeof(ssid))) {
    emitError("wifi missing ssid");
    return false;
  }
  if (ssid[0] == '\0') {
    clearWifiCredentials();
    Serial.println(F("{\"v\":1,\"type\":\"wifi\",\"ok\":true,\"cleared\":true}"));
    return true;
  }
  if (!extractJsonString(json, "password", password, sizeof(password))) {
    password[0] = '\0';
  }
  strncpy(g_wifiSsid, ssid, sizeof(g_wifiSsid) - 1);
  g_wifiSsid[sizeof(g_wifiSsid) - 1] = '\0';
  strncpy(g_wifiPassword, password, sizeof(g_wifiPassword) - 1);
  g_wifiPassword[sizeof(g_wifiPassword) - 1] = '\0';

  Serial.print(F("{\"v\":1,\"type\":\"wifi\",\"ok\":true,\"ssid\":\""));
  for (const char *c = g_wifiSsid; *c; ++c) {
    if (*c == '"' || *c == '\\') Serial.print('\\');
    Serial.print(*c);
  }
  Serial.println(F("\"}"));
  return true;
}

bool testWiFi(MKRIoTCarrier &carrier) {
  g_lastNote[0] = '\0';
  if (!hasWifiCredentials()) {
    setTestNote("no_credentials");
    return false;
  }

  clearDisplay(carrier);
  drawCenteredF(carrier, F("WiFi"), 50, 2, ST77XX_CYAN);
  drawCentered(carrier, g_wifiSsid, 95, 1, ST77XX_WHITE);
  drawCenteredF(carrier, F("Forbinder..."), 130, 1, ST77XX_YELLOW);

  WiFi.disconnect();
  delay(200);
  WiFi.begin(g_wifiSsid, g_wifiPassword);

  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - start < 20000) {
    if (isAbortRequested()) {
      setTestNote("aborted");
      WiFi.disconnect();
      return false;
    }
    delay(400);
  }

  if (WiFi.status() != WL_CONNECTED) {
    char note[40];
    snprintf(note, sizeof(note), "status=%d", (int)WiFi.status());
    setTestNote(note);
    clearDisplay(carrier);
    drawCenteredF(carrier, F("WiFi FEJL"), OLED_CY - 10, 2, ST77XX_RED);
    delay(800);
    WiFi.disconnect();
    return false;
  }

  IPAddress ip = WiFi.localIP();
  char note[48];
  snprintf(note, sizeof(note), "%d.%d.%d.%d", ip[0], ip[1], ip[2], ip[3]);
  setTestNote(note);

  clearDisplay(carrier);
  drawCenteredF(carrier, F("WiFi OK"), 55, 2, ST77XX_GREEN);
  drawCentered(carrier, note, 105, 1, ST77XX_WHITE);
  delay(900);
  WiFi.disconnect();
  return true;
}
