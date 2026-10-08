using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopedCustomCommandValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SingleArgument",
                table: "custom_commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.CreateTable(
                name: "custom_command_computed_results",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandId = table.Column<int>(type: "INTEGER", nullable: false),
                    InvocationId = table.Column<string>(type: "TEXT", nullable: false),
                    InvocationHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ViewerId = table.Column<string>(type: "TEXT", nullable: false),
                    Reply = table.Column<string>(type: "TEXT", nullable: false),
                    ReplyEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_computed_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_command_computed_results_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_value_definitions",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    DefaultText = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_value_definitions", x => x.Id);
                    table.UniqueConstraint(
                        "AK_custom_value_definitions_HostId_Id",
                        x => new { x.HostId, x.Id }
                    );
                    table.ForeignKey(
                        name: "FK_custom_value_definitions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "custom_stored_values",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewerId = table.Column<string>(type: "TEXT", nullable: false),
                    EntryKey = table.Column<string>(type: "TEXT", nullable: false),
                    TargetHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_stored_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_stored_values_custom_value_definitions_HostId_DefinitionId",
                        columns: x => new { x.HostId, x.DefinitionId },
                        principalTable: "custom_value_definitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_computed_results_HostId_InvocationHash",
                table: "custom_command_computed_results",
                columns: new[] { "HostId", "InvocationHash" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_stored_values_DefinitionId_TargetHash",
                table: "custom_stored_values",
                columns: new[] { "DefinitionId", "TargetHash" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_stored_values_HostId_DefinitionId",
                table: "custom_stored_values",
                columns: new[] { "HostId", "DefinitionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_value_definitions_HostId_Scope_NameHash",
                table: "custom_value_definitions",
                columns: new[] { "HostId", "Scope", "NameHash" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "custom_command_computed_results");

            migrationBuilder.DropTable(name: "custom_stored_values");

            migrationBuilder.DropTable(name: "custom_value_definitions");

            migrationBuilder.DropColumn(name: "SingleArgument", table: "custom_commands");
        }
    }
}
