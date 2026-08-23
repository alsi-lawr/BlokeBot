{
  lib,
  buildDotnetModule,
  dotnetCorePackages,
  fetchFromGitHub,
  fetchNpmDeps,
  nodejs_22,
  npmHooks,
}:

let
  version = "@PACKAGE_VERSION@";
  dotnetRuntime = dotnetCorePackages.aspnetcore_10_0;
  src = fetchFromGitHub {
    owner = "alsi-lawr";
    repo = "BlokeBot";
    rev = "v${version}";
    hash = "@SOURCE_HASH@";
  };
in
buildDotnetModule {
  pname = "blokebot";
  inherit version src;

  postPatch = "rm dotnet-tools.json";

  projectFile = "src/BlokeBot/BlokeBot.csproj";
  nugetDeps = ./deps.json;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetRuntime;
  executables = [ "blokebot" ];
  makeWrapperArgs = [
    "--set-default"
    "ASPNETCORE_CONTENTROOT"
    "${placeholder "out"}/lib/blokebot"
  ];

  postFixup = ''
    rm "$out/lib/blokebot/plugin-worker/BlokeBot.PluginWorker"
    makeWrapper "${dotnetRuntime}/bin/dotnet" \
      "$out/lib/blokebot/plugin-worker/BlokeBot.PluginWorker" \
      --add-flags "$out/lib/blokebot/plugin-worker/BlokeBot.PluginWorker.dll"
  '';

  npmRoot = "src/BlokeBot.Core";
  npmDeps = fetchNpmDeps {
    inherit src;
    sourceRoot = "${src.name}/src/BlokeBot.Core";
    hash = "sha256-LqmXiyTdzKlsubgaD93Zlb9aOoKSQd+7zHcpMcHpbXg=";
  };
  nativeBuildInputs = [
    nodejs_22
    npmHooks.npmConfigHook
  ];

  meta = {
    description = "Self-hosted Twitch bot and Blazor admin dashboard";
    homepage = "https://github.com/alsi-lawr/BlokeBot";
    license = lib.licenses.mit;
    mainProgram = "blokebot";
    platforms = lib.platforms.unix;
  };
}
