using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_ConfigurationActivationOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations"
            );

            migrationBuilder.DropColumn(name: "FailureCode", table: "configuration_activations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_configuration_activations_Status",
                table: "configuration_activations",
                sql: "Status IN ('Complete', 'Failed', 'ManualFollowUp', 'Pending', 'Processing')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "configuration_activations",
                type: "TEXT",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.Sql(
                """
                UPDATE configuration_activations
                SET FailureCode = CASE
                    WHEN IssuesJson IS NOT NULL AND json_valid(IssuesJson)
                    THEN json_extract(IssuesJson, '$[0].Code')
                    ELSE NULL
                END;
                """
            );

            migrationBuilder.Sql(
                """
                UPDATE configuration_activations
                SET Status = 'Failed'
                WHERE Status = 'ManualFollowUp';
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE __temp_configuration_activations (
                    Id TEXT NOT NULL CONSTRAINT PK_configuration_activations PRIMARY KEY,
                    HostId INTEGER NOT NULL,
                    EnabledChanges INTEGER NOT NULL,
                    DisabledChanges INTEGER NOT NULL,
                    Status TEXT NOT NULL,
                    AttemptCount INTEGER NOT NULL,
                    Revision INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT NULL,
                    IssuesJson TEXT NULL,
                    FailureCode TEXT NULL,
                    CONSTRAINT CK_configuration_activations_Status
                        CHECK (Status IN ('Complete', 'Failed', 'Pending', 'Processing')),
                    CONSTRAINT FK_configuration_activations_hosts_HostId
                        FOREIGN KEY (HostId) REFERENCES hosts (Id) ON DELETE CASCADE
                );

                INSERT INTO __temp_configuration_activations (
                    Id,
                    HostId,
                    EnabledChanges,
                    DisabledChanges,
                    Status,
                    AttemptCount,
                    Revision,
                    CreatedAtUtc,
                    UpdatedAtUtc,
                    CompletedAtUtc,
                    IssuesJson,
                    FailureCode
                )
                SELECT
                    Id,
                    HostId,
                    EnabledChanges,
                    DisabledChanges,
                    Status,
                    AttemptCount,
                    Revision,
                    CreatedAtUtc,
                    UpdatedAtUtc,
                    CompletedAtUtc,
                    IssuesJson,
                    FailureCode
                FROM configuration_activations;

                DROP TABLE configuration_activations;
                ALTER TABLE __temp_configuration_activations RENAME TO configuration_activations;
                CREATE INDEX IX_configuration_activations_HostId_Status
                    ON configuration_activations (HostId, Status);
                """
            );
        }
    }
}
