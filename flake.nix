{
  description = "Self-hosted Twitch bot and Blazor admin dashboard";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { nixpkgs, ... }:
    let
      system = "x86_64-linux";
      pkgs = import nixpkgs { inherit system; };
      package = pkgs.buildDotnetModule {
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

        npmRoot = "src/BlokeBot";
        npmDeps = pkgs.fetchNpmDeps {
          src = ./.;
          npmRoot = "src/BlokeBot";
          hash = "sha256-LqmXiyTdzKlsubgaD93Zlb9aOoKSQd+7zHcpMcHpbXg=";
        };
        nativeBuildInputs = [ pkgs.nodejs_22 pkgs.npmHooks.npmConfigHook ];

        preBuild = ''
          pushd src/BlokeBot
          npm run css:build
          popd
        '';
      };
    in
    {
      packages.${system}.default = package;
      apps.${system}.default = {
        type = "app";
        program = "${package}/bin/BlokeBot";
      };
    };
}
