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

  projectFile = "src/BlokeBot/BlokeBot.csproj";
  nugetDeps = ./deps.json;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.aspnetcore_10_0;
  executables = [ "blokebot" ];
  makeWrapperArgs = [
    "--set-default"
    "ASPNETCORE_CONTENTROOT"
    "${placeholder "out"}/lib/blokebot"
  ];

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
