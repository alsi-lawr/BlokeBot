#pragma warning disable

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v070_AutomationEventReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_event_receipts",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceDefinitionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 96,
                        nullable: false
                    ),
                    ProviderMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_automation_event_receipts",
                        x => new
                        {
                            x.HostId,
                            x.SourceDefinitionId,
                            x.ProviderMessageId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_automation_event_receipts_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_automation_event_receipts_ExpiresAtUtc",
                table: "automation_event_receipts",
                column: "ExpiresAtUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "automation_event_receipts");
        }
    }
}
