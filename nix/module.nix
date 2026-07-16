{ self }:
{
  config,
  lib,
  pkgs,
  ...
}:
let
  botCfg = config.services.blokebot;
  siteCfg = config.services.blokebot-site;
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

  options.services.blokebot-site = {
    enable = lib.mkEnableOption "the BlokeBot public site";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.stdenv.hostPlatform.system}.blokebot-site;
      defaultText = lib.literalExpression "inputs.blokebot.packages.${pkgs.stdenv.hostPlatform.system}.blokebot-site";
      description = "BlokeBot public site package to run.";
    };

    listenAddress = lib.mkOption {
      type = lib.types.str;
      default = "127.0.0.1";
      description = "Address on which the BlokeBot public site listens.";
    };

    port = lib.mkOption {
      type = lib.types.port;
      default = 8081;
      description = "TCP port on which the BlokeBot public site listens.";
    };
  };

  config = lib.mkMerge [
    (lib.mkIf botCfg.enable {
      users.groups.blokebot = { };
      users.users.blokebot = {
        isSystemUser = true;
        group = "blokebot";
        home = stateDir;
      };

      networking.firewall.allowedTCPPorts = lib.optional botCfg.openFirewall botCfg.port;

      systemd.tmpfiles.rules = [ "d ${stateDir} 0700 blokebot blokebot -" ];

      systemd.services.blokebot = {
        description = "BlokeBot Twitch bot and admin dashboard";
        after = [ "network-online.target" ];
        wants = [ "network-online.target" ];
        wantedBy = [ "multi-user.target" ];

        environment =
          lib.mapAttrs (
            _: value: if lib.isBool value then lib.boolToString value else toString value
          ) botCfg.environment
          // {
            ASPNETCORE_ENVIRONMENT = "Production";
            ASPNETCORE_URLS = "http://${botCfg.listenAddress}:${toString botCfg.port}";
            BlokeBot__DatabasePath = "${stateDir}/blokebot.db";
            TwitchBot__Identity__TokenCachePath = "${stateDir}/twitch.tokens.json";
          };

        serviceConfig = {
          ExecStart = "${lib.getExe botCfg.package} serve";
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
        // lib.optionalAttrs (botCfg.environmentFile != null) {
          EnvironmentFile = botCfg.environmentFile;
        };
      };
    })
    (lib.mkIf siteCfg.enable {
      systemd.services.blokebot-site = {
        description = "BlokeBot public site";
        after = [ "network.target" ];
        wantedBy = [ "multi-user.target" ];

        environment = {
          ASPNETCORE_ENVIRONMENT = "Production";
          ASPNETCORE_URLS = "http://${siteCfg.listenAddress}:${toString siteCfg.port}";
        };

        serviceConfig = {
          ExecStart = lib.getExe siteCfg.package;
          DynamicUser = true;
          Restart = "on-failure";
          UMask = "0077";

          AmbientCapabilities = [ ];
          CapabilityBoundingSet = [ ];
          LockPersonality = true;
          NoNewPrivileges = true;
          PrivateDevices = true;
          PrivateTmp = true;
          ProtectClock = true;
          ProtectControlGroups = true;
          ProtectHome = true;
          ProtectHostname = true;
          ProtectKernelLogs = true;
          ProtectKernelModules = true;
          ProtectKernelTunables = true;
          ProtectSystem = "strict";
          RestrictAddressFamilies = [
            "AF_INET"
            "AF_INET6"
            "AF_UNIX"
          ];
          RestrictNamespaces = true;
          RestrictRealtime = true;
          RestrictSUIDSGID = true;
          SystemCallArchitectures = "native";
        };
      };
    })
  ];
}
