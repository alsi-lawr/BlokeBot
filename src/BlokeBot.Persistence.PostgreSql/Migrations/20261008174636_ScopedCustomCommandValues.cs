using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlokeBot.Persistence.PostgreSql.Migrations
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
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "custom_command_computed_results",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    CommandId = table.Column<int>(type: "integer", nullable: false),
                    InvocationId = table.Column<string>(type: "text", nullable: false),
                    InvocationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ViewerId = table.Column<string>(type: "text", nullable: false),
                    Reply = table.Column<string>(type: "text", nullable: false),
                    ReplyEligible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_command_computed_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_command_computed_results_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "custom_value_definitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DefaultNumber = table.Column<long>(type: "bigint", nullable: false),
                    DefaultText = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_value_definitions", x => x.Id);
                    table.UniqueConstraint("AK_custom_value_definitions_HostId_Id", x => new { x.HostId, x.Id });
                    table.ForeignKey(
                        name: "FK_custom_value_definitions_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "custom_stored_values",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    DefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ViewerId = table.Column<string>(type: "text", nullable: false),
                    EntryKey = table.Column<string>(type: "text", nullable: false),
                    TargetHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_stored_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_custom_stored_values_custom_value_definitions_HostId_Defini~",
                        columns: x => new { x.HostId, x.DefinitionId },
                        principalTable: "custom_value_definitions",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_computed_results_HostId_InvocationHash",
                table: "custom_command_computed_results",
                columns: new[] { "HostId", "InvocationHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_stored_values_DefinitionId_TargetHash",
                table: "custom_stored_values",
                columns: new[] { "DefinitionId", "TargetHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_stored_values_HostId_DefinitionId",
                table: "custom_stored_values",
                columns: new[] { "HostId", "DefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_value_definitions_HostId_Scope_NameHash",
                table: "custom_value_definitions",
                columns: new[] { "HostId", "Scope", "NameHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_command_computed_results");

            migrationBuilder.DropTable(
                name: "custom_stored_values");

            migrationBuilder.DropTable(
                name: "custom_value_definitions");

            migrationBuilder.DropColumn(
                name: "SingleArgument",
                table: "custom_commands");
        }
    }
}
