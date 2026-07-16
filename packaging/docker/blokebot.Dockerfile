FROM node:26.5.0-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build

ARG BLOKEBOT_VERSION=0.0.0-dev
ARG SOURCE_REVISION_ID=unknown

COPY --from=node /usr/local/ /usr/local/

WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BlokeBot.Commands/ src/BlokeBot.Commands/
COPY src/BlokeBot.Core/ src/BlokeBot.Core/
COPY src/BlokeBot.Eventing/ src/BlokeBot.Eventing/
COPY src/BlokeBot.Functional/ src/BlokeBot.Functional/
COPY src/BlokeBot.Persistence/ src/BlokeBot.Persistence/
COPY src/BlokeBot.Twitch.Auth/ src/BlokeBot.Twitch.Auth/
COPY src/BlokeBot.Twitch.Runtime/ src/BlokeBot.Twitch.Runtime/
COPY src/BlokeBot.Twitch/ src/BlokeBot.Twitch/
COPY src/BlokeBot/ src/BlokeBot/

RUN dotnet restore src/BlokeBot/BlokeBot.csproj --disable-parallel
RUN dotnet publish src/BlokeBot/BlokeBot.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:Version="$BLOKEBOT_VERSION" \
    -p:SourceRevisionId="$SOURCE_REVISION_ID"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9-noble AS runtime

ARG BLOKEBOT_VERSION=0.0.0-dev
ARG SOURCE_REVISION_ID=unknown

LABEL org.opencontainers.image.source="https://github.com/alsi-lawr/BlokeBot" \
      org.opencontainers.image.version="$BLOKEBOT_VERSION" \
      org.opencontainers.image.revision="$SOURCE_REVISION_ID" \
      org.opencontainers.image.title="BlokeBot"

WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    BlokeBot__DatabasePath=/data/blokebot.db \
    TwitchBot__Identity__TokenCachePath=/data/twitch.tokens.json \
    HOME=/data

RUN mkdir /data && chown app:app /data

COPY --from=build --chown=app:app /app/publish ./

USER app

EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "blokebot.dll"]
CMD ["serve", "--host", "0.0.0.0", "--port", "8080", "--data-dir", "/data"]
