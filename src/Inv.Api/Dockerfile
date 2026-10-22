FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Inv.Domain/Inv.Domain.csproj src/Inv.Domain/
COPY src/Inv.Infrastructure/Inv.Infrastructure.csproj src/Inv.Infrastructure/
COPY src/Inv.Api/Inv.Api.csproj src/Inv.Api/
RUN dotnet restore src/Inv.Api/Inv.Api.csproj

COPY src/ src/
RUN dotnet publish src/Inv.Api/Inv.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Inv.Api.dll"]
