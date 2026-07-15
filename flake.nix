{
  description = "Self-hosted Twitch bot and Blazor admin dashboard";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs, ... }:
    let
      supportedSystems = [
        "x86_64-linux"
        "aarch64-linux"
      ];
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
      pkgsFor = system: import nixpkgs { inherit system; };
      packageFor =
        system:
        let
          pkgs = pkgsFor system;
        in
        pkgs.buildDotnetModule {
          pname = "BlokeBot";
          version = "0.0.0";
          src = pkgs.lib.cleanSourceWith {
            src = ./.;
            filter = path: type: baseNameOf path != "dotnet-tools.json";
          };

          projectFile = "src/BlokeBot/BlokeBot.csproj";
          nugetDeps = ./deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
          executables = [ "BlokeBot" ];
          makeWrapperArgs = [
            "--set-default"
            "ASPNETCORE_CONTENTROOT"
            "${placeholder "out"}/lib/BlokeBot"
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
            mainProgram = "BlokeBot";
          };
        };
    in
    {
      packages = forAllSystems (
        system:
        let
          package = packageFor system;
        in
        {
          default = package;
          blokebot = package;
        }
      );

      apps = forAllSystems (
        system:
        let
          package = self.packages.${system}.blokebot;
        in
        {
          default = self.apps.${system}.blokebot;
          blokebot = {
            type = "app";
            program = "${package}/bin/BlokeBot";
            meta.description = "Run BlokeBot";
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
            packages = [
              pkgs.dotnet-sdk_10
              pkgs.nodejs_22
              pkgs.nixfmt
            ];
          };
        }
      );

      formatter = forAllSystems (system: (pkgsFor system).nixfmt);

      nixosModules = {
        default = self.nixosModules.blokebot;
        blokebot = import ./nix/module.nix { inherit self; };
      };
    };
}
