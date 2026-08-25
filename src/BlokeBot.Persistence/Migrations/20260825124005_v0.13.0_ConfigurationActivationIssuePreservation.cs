using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations;

[DbContext(typeof(BlokeBotDbContext))]
[Migration("20260825124005_v0.13.0_ConfigurationActivationIssuePreservation")]
public sealed class v0130_ConfigurationActivationIssuePreservation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IssuesJson",
            table: "configuration_activations",
            type: "TEXT",
            maxLength: 4096,
            nullable: true
        );

        migrationBuilder.Sql(
            """
            UPDATE configuration_activations
            SET IssuesJson = json_array(
                json_object(
                    'Code', FailureCode,
                    'Reason', 'A previous automatic activation failed. Retry automatic activation.'
                )
            )
            WHERE FailureCode IS NOT NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IssuesJson", table: "configuration_activations");
}
