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
  databaseLaunchScript = pkgs.writeShellScript "blokebot-database-launch" ''
    set -eu

    unset BlokeBot__DatabaseProvider
    unset BlokeBot__DatabasePath
    unset BlokeBot__StateDirectory
    unset BlokeBot__PostgreSqlConnectionStringFile

    export BlokeBot__DatabaseProvider=${lib.escapeShellArg botCfg.databaseProvider}
    export BlokeBot__StateDirectory=${lib.escapeShellArg stateDir}
    ${
      if botCfg.databaseProvider == "Sqlite" then
        ''
          export BlokeBot__DatabasePath=${lib.escapeShellArg "${stateDir}/blokebot.db"}
        ''
      else
        ''
          if [ "$#" -lt 1 ]; then
            echo "The PostgreSQL credential path is missing." >&2
            exit 1
          fi
          export BlokeBot__PostgreSqlConnectionStringFile="$1"
          shift
        ''
    }

    exec "$@"
  '';
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

    databaseProvider = lib.mkOption {
      type = lib.types.enum [
        "Sqlite"
        "PostgreSql"
      ];
      default = "Sqlite";
      description = ''
        Main database provider. Sqlite is the default. PostgreSql requires one
        protected connection-string file. The operator must configure one
        active BlokeBot service.
      '';
    };

    postgresqlConnectionStringFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/run/secrets/blokebot-postgresql.connection";
      description = ''
        Path to the PostgreSQL connection-string file. The module transfers
        this file with systemd credentials. Do not use a Nix store path.
      '';
    };

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
        BlokeBotPrivacy__ControllerName = "Example Streaming Collective";
        BlokeBotPrivacy__PrivacyContact = "privacy@example.com";
        BlokeBotPrivacy__NoticeUrl = "https://www.example.com/privacy";
      };
      description = ''
        Non-secret ASP.NET Core environment settings for BlokeBot. These values
        are stored in the world-readable Nix store; use environmentFile for
        credentials and other secrets. Online deployments must supply the
        BlokeBotPrivacy controller name, monitored privacy contact, and
        privacy-notice URL; there are no defaults.
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

    pathBase = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/blokebot";
      description = "Optional URL path prefix under which the public site is served.";
    };

    liveAppUrl = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "https://bot.example.com";
      description = "Optional BlokeBot dashboard URL shown by the public site.";
    };

    controllerName = lib.mkOption {
      type = lib.types.str;
      example = "Example Streaming Collective";
      description = ''
        Who operates this deployment, as named in its privacy notice. Required;
        the site refuses to start without it and there is no default.
      '';
    };

    privacyContact = lib.mkOption {
      type = lib.types.str;
      example = "privacy@example.com";
      description = ''
        Monitored email address for privacy requests, shown on the privacy
        notice. Required; there is no default.
      '';
    };

    privacyNoticeUrl = lib.mkOption {
      type = lib.types.str;
      example = "https://www.example.com/privacy";
      description = ''
        Absolute HTTPS URL of this deployment's privacy notice page. Required;
        there is no default.
      '';
    };
  };

  config = lib.mkMerge [
    (lib.mkIf botCfg.enable {
      assertions = [
        {
          assertion =
            botCfg.databaseProvider == "PostgreSql" -> botCfg.postgresqlConnectionStringFile != null;
          message = "services.blokebot.postgresqlConnectionStringFile is required for PostgreSql.";
        }
        {
          assertion = botCfg.databaseProvider == "Sqlite" -> botCfg.postgresqlConnectionStringFile == null;
          message = "services.blokebot.postgresqlConnectionStringFile requires PostgreSql.";
        }
        {
          assertion =
            botCfg.postgresqlConnectionStringFile == null
            || (
              lib.hasPrefix "/" botCfg.postgresqlConnectionStringFile
              && !lib.hasPrefix "/nix/store/" botCfg.postgresqlConnectionStringFile
            );
          message = "services.blokebot.postgresqlConnectionStringFile must be an absolute path outside the Nix store.";
        }
        {
          assertion = lib.all (name: !(builtins.hasAttr name botCfg.environment)) [
            "BlokeBot__DatabaseProvider"
            "BlokeBot__DatabasePath"
            "BlokeBot__StateDirectory"
            "BlokeBot__PostgreSqlConnectionStringFile"
          ];
          message = "Use the typed services.blokebot database options instead of services.blokebot.environment.";
        }
      ];

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
            TwitchBot__Identity__TokenCachePath = "${stateDir}/twitch.tokens.json";
          };

        serviceConfig = {
          ExecStart = lib.escapeShellArgs (
            [ databaseLaunchScript ]
            ++ lib.optional (botCfg.databaseProvider == "PostgreSql") "%d/blokebot-postgresql"
            ++ [
              (lib.getExe botCfg.package)
              "serve"
              "--host"
              botCfg.listenAddress
              "--port"
              (toString botCfg.port)
              "--data-dir"
              stateDir
            ]
          );
          User = "blokebot";
          Group = "blokebot";
          WorkingDirectory = stateDir;
          Restart = "always";
          RestartSec = 5;
          UMask = "0077";

          NoNewPrivileges = true;
          PrivateTmp = true;
          ProtectHome = true;
          ProtectSystem = "strict";
          ReadWritePaths = [ stateDir ];
        }
        // lib.optionalAttrs (botCfg.databaseProvider == "PostgreSql") {
          LoadCredential = [
            "blokebot-postgresql:${botCfg.postgresqlConnectionStringFile}"
          ];
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
          BlokeBotSite__ControllerName = siteCfg.controllerName;
          BlokeBotSite__PrivacyContact = siteCfg.privacyContact;
          BlokeBotSite__PrivacyNoticeUrl = siteCfg.privacyNoticeUrl;
        }
        // lib.optionalAttrs (siteCfg.pathBase != null) {
          BlokeBotSite__PathBase = siteCfg.pathBase;
        }
        // lib.optionalAttrs (siteCfg.liveAppUrl != null) {
          BlokeBotSite__LiveAppUrl = siteCfg.liveAppUrl;
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
