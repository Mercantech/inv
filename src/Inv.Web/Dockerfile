FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Inv.Web/Inv.Web.csproj src/Inv.Web/
RUN dotnet restore src/Inv.Web/Inv.Web.csproj
COPY src/Inv.Web/ src/Inv.Web/
RUN dotnet publish src/Inv.Web/Inv.Web.csproj --configuration Release --no-restore --output /app/publish

FROM nginxinc/nginx-unprivileged:stable-alpine AS runtime
COPY --from=build --chown=101:101 /app/publish/wwwroot/ /usr/share/nginx/html/
COPY src/Inv.Web/Hosting/default.conf.template /etc/nginx/templates/default.conf.template
COPY src/Inv.Web/Hosting/client-config.json.template /opt/inv/client-config.json.template
COPY --chmod=755 src/Inv.Web/Hosting/40-client-config.sh /docker-entrypoint.d/40-client-config.sh
ENV API_BASE_URL=/api/ \
    API_UPSTREAM=http://inv-api:8080 \
    NGINX_ENVSUBST_FILTER=API_UPSTREAM
EXPOSE 3000
