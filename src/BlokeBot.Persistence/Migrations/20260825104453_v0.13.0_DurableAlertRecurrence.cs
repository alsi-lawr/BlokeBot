using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0130_DurableAlertRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastOccurredAtUtc",
                table: "durable_alerts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "durable_alerts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.Sql(
                """
                UPDATE "durable_alerts"
                SET "LastOccurredAtUtc" = "CreatedAtUtc";
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastOccurredAtUtc", table: "durable_alerts");

            migrationBuilder.DropColumn(name: "OccurrenceCount", table: "durable_alerts");
        }
    }
}
