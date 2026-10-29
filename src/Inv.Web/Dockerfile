FROM node:24-alpine AS labels
WORKDIR /labels
COPY src/Inv.Web/printing/package*.json ./
RUN npm ci
COPY src/Inv.Web/printing/ ./
RUN node build.mjs

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP_VERSION=dev
ARG APP_BUILT_AT=unknown
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Inv.Web/Inv.Web.csproj src/Inv.Web/
RUN dotnet restore src/Inv.Web/Inv.Web.csproj
COPY src/Inv.Web/ src/Inv.Web/
COPY --from=labels /wwwroot/letratag.js src/Inv.Web/wwwroot/letratag.js
# Firmware.bin ligger i wwwroot/firmware (bygges separat via hardware/Dockerfile / CI).
RUN test -f src/Inv.Web/wwwroot/firmware/opla-hardwaretest.bin
RUN printf '%s\n' "{\"version\":\"${APP_VERSION}\",\"builtAt\":\"${APP_BUILT_AT}\"}" \
    > src/Inv.Web/wwwroot/version.json \
 && cat src/Inv.Web/wwwroot/version.json
RUN dotnet publish src/Inv.Web/Inv.Web.csproj --configuration Release --no-restore --output /app/publish

FROM nginxinc/nginx-unprivileged:stable-alpine AS runtime
COPY --from=build --chown=101:101 /app/publish/wwwroot/ /usr/share/nginx/html/
COPY src/Inv.Web/Hosting/default.conf.template /etc/nginx/templates/default.conf.template
COPY src/Inv.Web/Hosting/client-config.json.template /opt/inv/client-config.json.template
COPY --chmod=755 src/Inv.Web/Hosting/40-client-config.sh /docker-entrypoint.d/40-client-config.sh
ENV API_BASE_URL=/api/ \
    AUTH_AUTHORITY=https://auth.mercantec.tech \
    AUTH_CLIENT_ID=inv \
    AUTH_REDIRECT_URI="" \
    LABEL_PUBLIC_BASE_URL="" \
    API_UPSTREAM=http://inv-api:8080 \
    NGINX_ENVSUBST_FILTER=API_UPSTREAM
EXPOSE 3000
