FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json ./
COPY src/Inv.Domain/ src/Inv.Domain/
COPY src/Inv.Infrastructure/ src/Inv.Infrastructure/
COPY src/Inv.Api/ src/Inv.Api/
COPY tests/Inv.Database.Tests/ tests/Inv.Database.Tests/
RUN dotnet tool restore \
    && dotnet build src/Inv.Api/Inv.Api.csproj --configuration Release \
    && dotnet build tests/Inv.Database.Tests/Inv.Database.Tests.csproj --configuration Release
ENV CONNECTION_STRING="Host=localhost;Database=inv_design_time;Username=inv"
CMD ["sh", "-c", "dotnet ef migrations has-pending-model-changes --project src/Inv.Infrastructure --startup-project src/Inv.Api --configuration Release --no-build && dotnet test tests/Inv.Database.Tests --configuration Release --no-build --logger 'console;verbosity=minimal'"]
