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
      releaseVersion = "0.1.0";
      imageSource = "https://github.com/alsi-lawr/BlokeBot";
      imageRevision = self.rev or self.dirtyRev or "unknown";
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
      pkgsFor = system: import nixpkgs { inherit system; };
      developmentPackages = pkgs: [
        pkgs.dotnet-sdk_10
        pkgs.nodejs_22
        pkgs.nixfmt
      ];
      source =
        pkgs:
        pkgs.lib.cleanSourceWith {
          src = ./.;
          filter = path: type: baseNameOf path != "dotnet-tools.json";
        };
      botPackageFor =
        system:
        let
          pkgs = pkgsFor system;
        in
        pkgs.buildDotnetModule {
          pname = "blokebot";
          version = "0.1.0";
          src = source pkgs;

          projectFile = "src/BlokeBot/BlokeBot.csproj";
          nugetDeps = ./deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
          executables = [ "blokebot" ];
          makeWrapperArgs = [
            "--set-default"
            "ASPNETCORE_CONTENTROOT"
            "${placeholder "out"}/lib/blokebot"
          ];

          npmRoot = "src/BlokeBot";
          npmDeps = pkgs.fetchNpmDeps {
            src = ./.;
            npmRoot = "src/BlokeBot";
            hash = "sha256-LqmXiyTdzKlsubgaD93Zlb9aOoKSQd+7zHcpMcHpbXg=";
          };
          nativeBuildInputs = [
            pkgs.nodejs_22
            pkgs.npmHooks.npmConfigHook
          ];

          preBuild = ''
            pushd src/BlokeBot
            npm run css:build
            popd
          '';

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
          version = "0.1.0";
          src = pkgs.lib.fileset.toSource {
            root = ./.;
            fileset = pkgs.lib.fileset.unions [
              ./Directory.Build.props
              ./Directory.Packages.props
              ./global.json
              ./src/BlokeBot.Site
            ];
          };

          projectFile = "src/BlokeBot.Site/BlokeBot.Site.csproj";
          nugetDeps = [ ];
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
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
      imageTagFor = system: "v${releaseVersion}-${imageArchitectureFor system}";
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
              ];
              Env = [
                "ASPNETCORE_ENVIRONMENT=Production"
                "ASPNETCORE_URLS=http://0.0.0.0:8080"
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
        // pkgs.lib.optionalAttrs pkgs.stdenv.isLinux {
          simulation = pkgs.mkShellNoCC {
            packages = developmentPackages pkgs ++ [
              pkgs.chromium
              pkgs.curl
              pkgs.imagemagick
              pkgs.libwebp
            ];
          };
        }
      );

      formatter = forAllSystems (system: (pkgsFor system).nixfmt);

      nixosModules = {
        default = self.nixosModules.blokebot;
        blokebot = import ./nix/module.nix { inherit self; };
        blokebot-site = self.nixosModules.blokebot;
      };
    };
}
