# Build context = repository root. The Bridgit submodule must be initialized before building:
#   git submodule update --init
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# restore only needs the project graph
COPY BridgeMindPlay.Api/BridgeMindPlay.Api.csproj BridgeMindPlay.Api/
COPY TricksterBots/TricksterBots.csproj TricksterBots/
COPY Bridgit/BridgeBidder/BridgeBidder.csproj Bridgit/BridgeBidder/
RUN dotnet restore BridgeMindPlay.Api/BridgeMindPlay.Api.csproj -v q

# sources
COPY BridgeMindPlay.Api/ BridgeMindPlay.Api/
COPY TricksterBots/ TricksterBots/
COPY Bridgit/ Bridgit/

RUN dotnet publish BridgeMindPlay.Api/BridgeMindPlay.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV PORT=8080
EXPOSE 8080

# plain HTTP inside the container; TLS termination is the platform's job (Coolify)
ENTRYPOINT ["dotnet", "BridgeMindPlay.Api.dll"]
