FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build

ARG BLOKEBOT_VERSION=0.0.0-dev
ARG SOURCE_REVISION_ID=unknown

WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BlokeBot.Site/ src/BlokeBot.Site/

RUN dotnet restore src/BlokeBot.Site/BlokeBot.Site.csproj --disable-parallel
RUN dotnet publish src/BlokeBot.Site/BlokeBot.Site.csproj \
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
      org.opencontainers.image.title="BlokeBot public site"

WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8081 \
    HOME=/tmp

COPY --from=build --chown=app:app /app/publish ./

USER app

EXPOSE 8081

ENTRYPOINT ["dotnet", "BlokeBot.Site.dll"]
