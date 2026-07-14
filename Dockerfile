FROM node:26.5.0-bookworm-slim AS frontend

WORKDIR /src/src/BlokeBot

COPY src/BlokeBot/package.json src/BlokeBot/package-lock.json ./
RUN npm ci

COPY src/BlokeBot/ ./
RUN npm run css:build

FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build

WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/
COPY --from=frontend /src/src/BlokeBot/node_modules ./src/BlokeBot/node_modules
COPY --from=frontend /src/src/BlokeBot/wwwroot/app.css ./src/BlokeBot/wwwroot/app.css

RUN dotnet restore src/BlokeBot/BlokeBot.csproj --disable-parallel
RUN dotnet publish src/BlokeBot/BlokeBot.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9-noble AS runtime

WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    BlokeBot__DatabasePath=/data/blokebot.db \
    TwitchBot__Identity__TokenCachePath=/data/twitch.tokens.json

RUN mkdir /data && chown app:app /data

COPY --from=build --chown=app:app /app/publish ./

USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "BlokeBot.dll"]
