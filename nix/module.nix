{ self }:
{
  config,
  lib,
  pkgs,
  ...
}:
let
  cfg = config.services.blokebot;
  stateDir = "/var/lib/blokebot";
in
{
  options.services.blokebot = {
    enable = lib.mkEnableOption "BlokeBot";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
      defaultText = lib.literalExpression "inputs.blokebot.packages.${pkgs.stdenv.hostPlatform.system}.default";
      description = "BlokeBot package to run.";
    };

    listenAddress = lib.mkOption {
      type = lib.types.str;
      default = "127.0.0.1";
      description = "Address on which the BlokeBot dashboard listens.";
    };

    port = lib.mkOption {
      type = lib.types.port;
      default = 8080;
      description = "TCP port on which the BlokeBot dashboard listens.";
    };

    openFirewall = lib.mkEnableOption "the BlokeBot dashboard port in the firewall";

    environment = lib.mkOption {
      type = lib.types.attrsOf (
        lib.types.oneOf [
          lib.types.str
          lib.types.int
          lib.types.bool
        ]
      );
      default = { };
      example = {
        TwitchBot__Identity__BotUsername = "my-bot";
        TwitchBot__Identity__ClientId = "public-client-id";
        TwitchBot__Identity__RedirectUri = "https://bot.example.com/oauth/callback";
      };
      description = ''
        Non-secret ASP.NET Core environment settings for BlokeBot. These values
        are stored in the world-readable Nix store; use environmentFile for
        credentials and other secrets.
      '';
    };

    environmentFile = lib.mkOption {
      type = lib.types.nullOr lib.types.path;
      default = null;
      example = "/run/secrets/blokebot.env";
      description = ''
        Environment file containing secrets, as described by
        {manpage}`systemd.exec(5)`. The file must be readable by the blokebot
        service user and should not be created in the Nix store.
      '';
    };
  };

  config = lib.mkIf cfg.enable {
    users.groups.blokebot = { };
    users.users.blokebot = {
      isSystemUser = true;
      group = "blokebot";
      home = stateDir;
    };

    networking.firewall.allowedTCPPorts = lib.optional cfg.openFirewall cfg.port;

    systemd.tmpfiles.rules = [ "d ${stateDir} 0700 blokebot blokebot -" ];

    systemd.services.blokebot = {
      description = "BlokeBot Twitch bot and admin dashboard";
      after = [ "network-online.target" ];
      wants = [ "network-online.target" ];
      wantedBy = [ "multi-user.target" ];

      environment =
        lib.mapAttrs (
          _: value: if lib.isBool value then lib.boolToString value else toString value
        ) cfg.environment
        // {
          ASPNETCORE_ENVIRONMENT = "Production";
          ASPNETCORE_URLS = "http://${cfg.listenAddress}:${toString cfg.port}";
          BlokeBot__DatabasePath = "${stateDir}/blokebot.db";
          TwitchBot__Identity__TokenCachePath = "${stateDir}/twitch.tokens.json";
        };

      serviceConfig = {
        ExecStart = "${lib.getExe cfg.package} serve";
        User = "blokebot";
        Group = "blokebot";
        WorkingDirectory = stateDir;
        Restart = "on-failure";
        UMask = "0077";

        NoNewPrivileges = true;
        PrivateTmp = true;
        ProtectHome = true;
        ProtectSystem = "strict";
        ReadWritePaths = [ stateDir ];
      }
      // lib.optionalAttrs (cfg.environmentFile != null) {
        EnvironmentFile = cfg.environmentFile;
      };
    };
  };
}
