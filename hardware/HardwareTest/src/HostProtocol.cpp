#include "Hardwaretestere.h"
#include <string.h>

static volatile bool g_abortRequested = false;
static bool g_testsRunning = false;

static void jsonEscapeTo(const char *value, char *out, size_t outLen) {
  size_t o = 0;
  if (!value) {
    out[0] = '\0';
    return;
  }
  for (const unsigned char *p = (const unsigned char *)value; *p && o + 2 < outLen; ++p) {
    if (*p == '"' || *p == '\\') {
      if (o + 3 >= outLen) break;
      out[o++] = '\\';
      out[o++] = (char)*p;
    } else if (*p < 0x20) {
      continue;
    } else {
      out[o++] = (char)*p;
    }
  }
  out[o] = '\0';
}

void emitReady() {
  Serial.println(F("{\"v\":1,\"type\":\"ready\"}"));
}

void emitIds() {
  char eccEsc[64];
  jsonEscapeTo(g_eccOk ? g_eccSerial : "", eccEsc, sizeof(eccEsc));
  Serial.print(F("{\"v\":1,\"type\":\"ids\",\"samd_id\":\""));
  Serial.print(g_samdId);
  Serial.print(F("\",\"mac\":\""));
  Serial.print(g_macStr);
  Serial.print(F("\",\"ecc_serial\":\""));
  Serial.print(eccEsc);
  Serial.println(F("\"}"));
}

void emitError(const char *message) {
  char esc[96];
  jsonEscapeTo(message, esc, sizeof(esc));
  Serial.print(F("{\"v\":1,\"type\":\"error\",\"message\":\""));
  Serial.print(esc);
  Serial.println(F("\"}"));
}

void emitStepBegin(const char *name) {
  Serial.print(F("{\"v\":1,\"type\":\"step\",\"name\":\""));
  Serial.print(name);
  Serial.println(F("\",\"phase\":\"begin\"}"));
}

void emitStepAwaitingConfirm(const char *name, const char *prompt) {
  char esc[128];
  jsonEscapeTo(prompt, esc, sizeof(esc));
  Serial.print(F("{\"v\":1,\"type\":\"step\",\"name\":\""));
  Serial.print(name);
  Serial.print(F("\",\"phase\":\"awaiting_confirm\",\"prompt\":\""));
  Serial.print(esc);
  Serial.println(F("\"}"));
}

void emitStepResult(const char *name, bool ok, bool skipped, const char *note) {
  char esc[96];
  jsonEscapeTo(note ? note : "", esc, sizeof(esc));
  Serial.print(F("{\"v\":1,\"type\":\"step\",\"name\":\""));
  Serial.print(name);
  Serial.print(F("\",\"phase\":\"result\",\"ok\":"));
  Serial.print(ok ? F("true") : F("false"));
  Serial.print(F(",\"skipped\":"));
  Serial.print(skipped ? F("true") : F("false"));
  Serial.print(F(",\"note\":\""));
  Serial.print(esc);
  Serial.println(F("\"}"));
}

void emitReading(const char *name, const char *text) {
  char esc[96];
  jsonEscapeTo(text ? text : "", esc, sizeof(esc));
  Serial.print(F("{\"v\":1,\"type\":\"reading\",\"name\":\""));
  Serial.print(name ? name : "");
  Serial.print(F("\",\"text\":\""));
  Serial.print(esc);
  Serial.println(F("\"}"));
}

void emitDone(int passed, int failed) {
  Serial.print(F("{\"v\":1,\"type\":\"done\",\"passed\":"));
  Serial.print(passed);
  Serial.print(F(",\"failed\":"));
  Serial.print(failed);
  Serial.print(F(",\"tests\":["));
  for (int i = 0; i < g_testLogCount; i++) {
    if (i > 0) Serial.print(',');
    char esc[96];
    jsonEscapeTo(g_testLog[i].note, esc, sizeof(esc));
    Serial.print(F("{\"name\":\""));
    Serial.print(g_testLog[i].name);
    Serial.print(F("\",\"ok\":"));
    Serial.print(g_testLog[i].ok ? F("true") : F("false"));
    Serial.print(F(",\"skipped\":"));
    Serial.print(g_testLog[i].skipped ? F("true") : F("false"));
    Serial.print(F(",\"note\":\""));
    Serial.print(esc);
    Serial.print(F("\"}"));
  }
  Serial.println(F("]}"));
}

void clearAbortRequest() {
  g_abortRequested = false;
}

bool isAbortRequested() {
  return g_abortRequested;
}

bool isTestsRunning() {
  return g_testsRunning;
}

void setTestsRunning(bool running) {
  g_testsRunning = running;
}

// Returns true if a full command line was read into buf.
bool readHostCommand(char *buf, size_t bufLen, unsigned long timeoutMs) {
  size_t len = 0;
  buf[0] = '\0';
  unsigned long start = millis();
  while (timeoutMs == 0 || millis() - start < timeoutMs) {
    while (Serial.available() > 0) {
      char c = (char)Serial.read();
      if (c == '\r') continue;
      if (c == '\n') {
        buf[len] = '\0';
        if (len == 0) continue;
        // JSON-linjer (fx wifi-config) bevarer case; korte kommandoer uppercases.
        if (buf[0] != '{') {
          for (size_t i = 0; i < len; i++) {
            if (buf[i] >= 'a' && buf[i] <= 'z') buf[i] = (char)(buf[i] - 'a' + 'A');
          }
          if (strcmp(buf, "ABORT") == 0) {
            g_abortRequested = true;
          }
        }
        return true;
      }
      if (len + 1 < bufLen) {
        buf[len++] = c;
      }
    }
    if (timeoutMs == 0) return false;
    delay(5);
  }
  return false;
}

bool confirmPhysical(MKRIoTCarrier &carrier, const __FlashStringHelper *title,
                     const __FlashStringHelper *hint, unsigned long timeoutMs) {
  char titleBuf[24];
  char hintBuf[96];
  strncpy_P(titleBuf, (PGM_P)title, sizeof(titleBuf) - 1);
  titleBuf[sizeof(titleBuf) - 1] = '\0';
  strncpy_P(hintBuf, (PGM_P)hint, sizeof(hintBuf) - 1);
  hintBuf[sizeof(hintBuf) - 1] = '\0';

  // Drain leftover input before waiting for OK/FAIL
  while (Serial.available() > 0) (void)Serial.read();

  emitStepAwaitingConfirm(titleBuf, hintBuf);

  unsigned long start = millis();
  unsigned long lastUi = 0;
  char line[24];

  while (millis() - start < timeoutMs) {
    if (g_abortRequested) {
      setTestNote("aborted");
      return false;
    }

    unsigned long elapsed = millis() - start;
    if (elapsed - lastUi >= 500 || lastUi == 0) {
      lastUi = elapsed;
      unsigned long leftSec = (timeoutMs - elapsed + 999) / 1000;
      char countdown[16];
      snprintf(countdown, sizeof(countdown), "%lus", leftSec);
      clearDisplay(carrier);
      drawCentered(carrier, titleBuf, 40, 2, ST77XX_CYAN);
      drawCenteredF(carrier, F("Ser det OK ud?"), 85, 2, ST77XX_WHITE);
      drawCenteredF(carrier, F("Svar i browser"), 120, 1, ST77XX_YELLOW);
      drawCenteredF(carrier, F("OK eller FEJL"), 140, 1, ST77XX_YELLOW);
      drawCentered(carrier, countdown, 175, 2, ST77XX_WHITE);
    }

    if (readHostCommand(line, sizeof(line), 50)) {
      if (strcmp(line, "OK") == 0 || strcmp(line, "Y") == 0 || strcmp(line, "YES") == 0
          || strcmp(line, "JA") == 0 || strcmp(line, "1") == 0) {
        return true;
      }
      if (strcmp(line, "FAIL") == 0 || strcmp(line, "FEJL") == 0 || strcmp(line, "N") == 0
          || strcmp(line, "NO") == 0 || strcmp(line, "NEJ") == 0 || strcmp(line, "0") == 0) {
        setTestNote("operator_fail");
        return false;
      }
      if (strcmp(line, "ABORT") == 0) {
        setTestNote("aborted");
        return false;
      }
      // Ignore START and unknown while waiting
    }
  }

  setTestNote("timeout_no_serial");
  return false;
}
