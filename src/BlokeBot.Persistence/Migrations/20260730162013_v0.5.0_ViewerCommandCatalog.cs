#pragma warning disable

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v050_ViewerCommandCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_command_aliases_Kind",
                table: "command_aliases"
            );

            migrationBuilder.AddColumn<bool>(
                name: "CommandsAliasesConfigured",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "CommandsDefaultConflictAlias",
                table: "hosts",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "custom_command_aliases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.Sql(
                """
                UPDATE "custom_command_aliases" AS current
                SET "SortOrder" = (
                    SELECT COUNT(*)
                    FROM "custom_command_aliases" AS preceding
                    WHERE preceding."CustomCommandId" = current."CustomCommandId"
                      AND (
                          lower(preceding."Alias") < lower(current."Alias")
                          OR (
                              lower(preceding."Alias") = lower(current."Alias")
                              AND preceding."Alias" < current."Alias"
                          )
                          OR (
                              preceding."Alias" = current."Alias"
                              AND preceding."Id" < current."Id"
                          )
                      )
                );
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE "command_aliases" AS candidate
                SET "Alias" = 'enter'
                WHERE candidate."GuessRoundProfileId" IS NULL
                  AND candidate."Kind" = 'Join'
                  AND candidate."Alias" = 'join'
                  AND (
                      SELECT COUNT(*)
                      FROM "command_aliases" AS points
                      WHERE points."HostId" = candidate."HostId"
                        AND points."GuessRoundProfileId" IS NULL
                        AND points."Kind" IN (
                            'Points', 'GivePoints', 'AddPoints', 'RemovePoints', 'Gamble',
                            'Giveaway', 'Join', 'EndGiveaway', 'CancelGiveaway'
                        )
                  ) = 9
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'Points' AND "Alias" = 'points'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'GivePoints' AND "Alias" = 'givepoints'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'AddPoints' AND "Alias" = 'addpoints'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'RemovePoints' AND "Alias" = 'removepoints'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'Gamble' AND "Alias" = 'gamble'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'Giveaway' AND "Alias" = 'giveaway'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'EndGiveaway' AND "Alias" = 'endgiveaway'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'CancelGiveaway' AND "Alias" = 'cancelgiveaway'
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId" AND "Alias" = 'enter'
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM "custom_command_aliases"
                      WHERE "HostId" = candidate."HostId" AND "Alias" = 'enter'
                  );
                """
            );

            migrationBuilder.Sql(
                """
                PRAGMA ignore_check_constraints = ON;

                INSERT INTO "command_aliases" ("HostId", "GuessRoundProfileId", "Kind", "Alias")
                SELECT host."Id", NULL, 'Commands', 'commands'
                FROM "hosts" AS host
                WHERE NOT EXISTS (
                    SELECT 1 FROM "command_aliases"
                    WHERE "HostId" = host."Id" AND "Alias" = 'commands'
                )
                  AND NOT EXISTS (
                    SELECT 1 FROM "custom_command_aliases"
                    WHERE "HostId" = host."Id" AND "Alias" = 'commands'
                );

                UPDATE "hosts"
                SET "CommandsAliasesConfigured" = 1,
                    "CommandsDefaultConflictAlias" = CASE
                        WHEN EXISTS (
                            SELECT 1 FROM "command_aliases"
                            WHERE "HostId" = "hosts"."Id" AND "Kind" = 'Commands'
                        ) THEN NULL
                        ELSE 'commands'
                    END;

                PRAGMA ignore_check_constraints = OFF;
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_aliases_CustomCommandId_SortOrder",
                table: "custom_command_aliases",
                columns: new[] { "CustomCommandId", "SortOrder" },
                unique: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_command_aliases_Kind",
                table: "command_aliases",
                sql: "Kind IN ('AddPoints', 'CancelGiveaway', 'Commands', 'EndGiveaway', 'Gamble', 'Giveaway', 'GivePoints', 'Guess', 'Guesses', 'Join', 'Points', 'RemovePoints', 'Start', 'Stop', 'Win')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "command_aliases" WHERE "Kind" = 'Commands';

                UPDATE "command_aliases" AS candidate
                SET "Alias" = 'join'
                WHERE candidate."GuessRoundProfileId" IS NULL
                  AND candidate."Kind" = 'Join'
                  AND candidate."Alias" = 'enter'
                  AND (
                      SELECT COUNT(*)
                      FROM "command_aliases" AS points
                      WHERE points."HostId" = candidate."HostId"
                        AND points."GuessRoundProfileId" IS NULL
                        AND points."Kind" IN (
                            'Points', 'GivePoints', 'AddPoints', 'RemovePoints', 'Gamble',
                            'Giveaway', 'Join', 'EndGiveaway', 'CancelGiveaway'
                        )
                  ) = 9
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'Points' AND "Alias" = 'points'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'GivePoints' AND "Alias" = 'givepoints'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'AddPoints' AND "Alias" = 'addpoints'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'RemovePoints' AND "Alias" = 'removepoints'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'Gamble' AND "Alias" = 'gamble'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'Giveaway' AND "Alias" = 'giveaway'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'EndGiveaway' AND "Alias" = 'endgiveaway'
                  )
                  AND EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId"
                        AND "GuessRoundProfileId" IS NULL
                        AND "Kind" = 'CancelGiveaway' AND "Alias" = 'cancelgiveaway'
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM "command_aliases"
                      WHERE "HostId" = candidate."HostId" AND "Alias" = 'join'
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM "custom_command_aliases"
                      WHERE "HostId" = candidate."HostId" AND "Alias" = 'join'
                  );
                """
            );

            migrationBuilder.DropIndex(
                name: "IX_custom_command_aliases_CustomCommandId_SortOrder",
                table: "custom_command_aliases"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_command_aliases_Kind",
                table: "command_aliases"
            );

            migrationBuilder.DropColumn(name: "CommandsAliasesConfigured", table: "hosts");

            migrationBuilder.DropColumn(name: "CommandsDefaultConflictAlias", table: "hosts");

            migrationBuilder.DropColumn(name: "SortOrder", table: "custom_command_aliases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_command_aliases_Kind",
                table: "command_aliases",
                sql: "Kind IN ('AddPoints', 'CancelGiveaway', 'EndGiveaway', 'Gamble', 'Giveaway', 'GivePoints', 'Guess', 'Guesses', 'Join', 'Points', 'RemovePoints', 'Start', 'Stop', 'Win')"
            );
        }
    }
}
