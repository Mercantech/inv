#!/bin/sh
set -eu

# Public configuration only. Reject characters that would break JSON or a URL.
case "${API_BASE_URL}" in
    ''|*[!a-zA-Z0-9:/._~-]*) echo "API_BASE_URL must be a plain HTTP(S) or relative URL" >&2; exit 1 ;;
esac

envsubst '${API_BASE_URL}' < /opt/inv/client-config.json.template > /usr/share/nginx/html/client-config.json
