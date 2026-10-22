#!/bin/sh
set -eu

# Public configuration only. Reject characters that would break JSON or a URL.
case "${API_BASE_URL}" in
    ''|*[!a-zA-Z0-9:/._~-]*) echo "API_BASE_URL must be a plain HTTP(S) or relative URL" >&2; exit 1 ;;
esac

for value in "$AUTH_AUTHORITY" "$AUTH_CLIENT_ID" "$AUTH_REDIRECT_URI"; do
    case "$value" in
        *[!a-zA-Z0-9:/._~%-]*) echo "Auth configuration contains unsupported characters" >&2; exit 1 ;;
    esac
done

envsubst '${API_BASE_URL} ${AUTH_AUTHORITY} ${AUTH_CLIENT_ID} ${AUTH_REDIRECT_URI}' < /opt/inv/client-config.json.template > /usr/share/nginx/html/client-config.json
