FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:306301580fcaa5b445180e759db59309979002d1000669cb4cf58a567d0014bc AS build
WORKDIR /src
COPY BankingReconciliation.sln ./
COPY BankingReconciliation.Api/BankingReconciliation.Api.csproj BankingReconciliation.Api/
COPY BankingReconciliation.Tests/BankingReconciliation.Tests.csproj BankingReconciliation.Tests/
RUN dotnet restore BankingReconciliation.sln
COPY . .
RUN dotnet publish BankingReconciliation.Api/BankingReconciliation.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine-extra@sha256:bfb8d74a4b0130c7e4abf88a4dede4f51929b91e26d76ae8ccf3f571a21db3b9 AS runtime
WORKDIR /app
COPY --from=build --chown=1654:1654 /app/publish .
USER 1654
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "BankingReconciliation.Api.dll"]
