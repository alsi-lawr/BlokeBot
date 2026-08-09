{
  description = "BlokeBot Twitch bot, dashboard, and public site";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs, ... }:
    let
      supportedSystems = [
        "x86_64-linux"
        "aarch64-linux"
        "aarch64-darwin"
      ];
      releaseVersion = "0.8.1";
      imageSource = "https://github.com/alsi-lawr/BlokeBot";
      imageRevision = self.rev or self.dirtyRev or "unknown";
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
      pkgsFor = system: import nixpkgs { inherit system; };
      developmentPackages = pkgs: [
        pkgs.dotnet-sdk_10
        pkgs.nodejs_22
        pkgs.nixfmt
      ];
      commonSourceFiles = lib: [
        ./Directory.Build.props
        ./Directory.Packages.props
        ./global.json
      ];
      botSource =
        pkgs:
        pkgs.lib.fileset.toSource {
          root = ./.;
          fileset = pkgs.lib.fileset.unions (
            commonSourceFiles pkgs.lib
            ++ [
              ./src/BlokeBot
              ./src/BlokeBot.Commands
              ./src/BlokeBot.Core
              ./src/BlokeBot.Eventing
              ./src/BlokeBot.Functional
              ./src/BlokeBot.Persistence
              ./src/BlokeBot.Twitch
              ./src/BlokeBot.Twitch.Auth
              ./src/BlokeBot.Twitch.Runtime
            ]
          );
        };
      siteSource =
        pkgs:
        pkgs.lib.fileset.toSource {
          root = ./.;
          fileset = pkgs.lib.fileset.unions (
            commonSourceFiles pkgs.lib
            ++ [
              ./src/BlokeBot.Site
            ]
          );
        };
      botPackageFor =
        system:
        let
          pkgs = pkgsFor system;
          src = botSource pkgs;
        in
        pkgs.buildDotnetModule {
          pname = "blokebot";
          version = releaseVersion;
          inherit src;
          enableParallelBuilding = false;

          projectFile = "src/BlokeBot/BlokeBot.csproj";
          nugetDeps = ./packaging/nix/deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
          dotnetBuildFlags = [ "-p:SourceRevisionId=${imageRevision}" ];
          executables = [ "blokebot" ];
          makeWrapperArgs = [
            "--set-default"
            "ASPNETCORE_CONTENTROOT"
            "${placeholder "out"}/lib/blokebot"
          ];

          npmRoot = "src/BlokeBot.Core";
          npmDeps = pkgs.fetchNpmDeps {
            src = src + "/src/BlokeBot.Core";
            hash = "sha256-LqmXiyTdzKlsubgaD93Zlb9aOoKSQd+7zHcpMcHpbXg=";
          };
          nativeBuildInputs = [
            pkgs.nodejs_22
            pkgs.npmHooks.npmConfigHook
          ];

          meta = {
            description = "Self-hosted Twitch bot and Blazor admin dashboard";
            license = pkgs.lib.licenses.mit;
            mainProgram = "blokebot";
          };
        };
      sitePackageFor =
        system:
        let
          pkgs = pkgsFor system;
        in
        pkgs.buildDotnetModule {
          pname = "blokebot-site";
          version = releaseVersion;
          src = siteSource pkgs;
          enableParallelBuilding = false;

          projectFile = "src/BlokeBot.Site/BlokeBot.Site.csproj";
          nugetDeps = ./packaging/nix/deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
          dotnetBuildFlags = [ "-p:SourceRevisionId=${imageRevision}" ];
          executables = [ "BlokeBot.Site" ];
          makeWrapperArgs = [
            "--set-default"
            "ASPNETCORE_CONTENTROOT"
            "${placeholder "out"}/lib/blokebot-site"
          ];

          postFixup = ''
            mv "$out/bin/BlokeBot.Site" "$out/bin/blokebot-site"
          '';

          meta = {
            description = "BlokeBot public product and user guide site";
            license = pkgs.lib.licenses.mit;
            mainProgram = "blokebot-site";
          };
        };
      imageArchitectureFor =
        system:
        {
          x86_64-linux = "amd64";
          aarch64-linux = "arm64";
        }
        .${system} or (throw "Container images are unsupported on ${system}");
      imageTagFor = system: "${releaseVersion}-${imageArchitectureFor system}";
      imageLabels = title: {
        "org.opencontainers.image.source" = imageSource;
        "org.opencontainers.image.version" = releaseVersion;
        "org.opencontainers.image.revision" = imageRevision;
        "org.opencontainers.image.title" = title;
      };
      containerImagesFor =
        system:
        let
          pkgs = pkgsFor system;
          packages = self.packages.${system};
          architecture = imageArchitectureFor system;
          tag = imageTagFor system;
        in
        {
          blokebot-image = pkgs.dockerTools.buildLayeredImage {
            name = "ghcr.io/alsi-lawr/blokebot";
            inherit architecture tag;
            contents = [
              packages.blokebot
              pkgs.dockerTools.caCertificates
            ];
            fakeRootCommands = ''
              mkdir -p ./data ./tmp
              chown 65532:65532 ./data ./tmp
              chmod 0700 ./data ./tmp
            '';
            config = {
              User = "65532:65532";
              WorkingDir = "/data";
              Entrypoint = [
                "${packages.blokebot}/bin/blokebot"
                "serve"
                "--host"
                "0.0.0.0"
                "--port"
                "8080"
                "--data-dir"
                "/data"
              ];
              Env = [
                "ASPNETCORE_ENVIRONMENT=Production"
                "BlokeBot__DatabasePath=/data/blokebot.db"
                "HOME=/data"
                "TwitchBot__Identity__TokenCachePath=/data/twitch.tokens.json"
              ];
              ExposedPorts = {
                "8080/tcp" = { };
              };
              Volumes = {
                "/data" = { };
              };
              Labels = imageLabels "BlokeBot";
            };
          };

          blokebot-site-image = pkgs.dockerTools.buildLayeredImage {
            name = "ghcr.io/alsi-lawr/blokebot-site";
            inherit architecture tag;
            contents = [ packages.blokebot-site ];
            fakeRootCommands = ''
              mkdir -p ./tmp
              chown 65532:65532 ./tmp
              chmod 0700 ./tmp
            '';
            config = {
              User = "65532:65532";
              WorkingDir = "/tmp";
              Entrypoint = [ "${packages.blokebot-site}/bin/blokebot-site" ];
              Env = [
                "ASPNETCORE_ENVIRONMENT=Production"
                "ASPNETCORE_URLS=http://0.0.0.0:8081"
                "HOME=/tmp"
              ];
              ExposedPorts = {
                "8081/tcp" = { };
              };
              Labels = imageLabels "BlokeBot public site";
            };
          };
        };
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = pkgsFor system;
          blokebot = botPackageFor system;
          blokebot-site = sitePackageFor system;
        in
        {
          default = blokebot;
          inherit blokebot blokebot-site;
        }
        // pkgs.lib.optionalAttrs pkgs.stdenv.isLinux (containerImagesFor system)
      );

      apps = forAllSystems (
        system:
        let
          packages = self.packages.${system};
        in
        {
          default = self.apps.${system}.blokebot;
          blokebot = {
            type = "app";
            program = "${packages.blokebot}/bin/blokebot";
            meta.description = "Run BlokeBot";
          };
          blokebot-site = {
            type = "app";
            program = "${packages.blokebot-site}/bin/blokebot-site";
            meta.description = "Run the BlokeBot public site";
          };
        }
      );

      devShells = forAllSystems (
        system:
        let
          pkgs = pkgsFor system;
        in
        {
          default = pkgs.mkShellNoCC {
            packages = developmentPackages pkgs;
          };
        }
      );

      formatter = forAllSystems (system: (pkgsFor system).nixfmt);

      nixosModules = {
        default = self.nixosModules.blokebot;
        blokebot = import ./packaging/nix/module.nix { inherit self; };
        blokebot-site = import ./packaging/nix/module.nix { inherit self; };
      };
    };
}
