using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateStoredValueNamespaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_custom_value_definitions_HostId_Scope_NameHash",
                table: "custom_value_definitions"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_value_definitions_DictionaryName",
                table: "custom_value_definitions",
                columns: new[] { "HostId", "Scope", "NameHash" },
                unique: true,
                filter: "\"Kind\" = 2"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_value_definitions_ScalarName",
                table: "custom_value_definitions",
                columns: new[] { "HostId", "Scope", "NameHash" },
                unique: true,
                filter: "\"Kind\" <> 2"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_custom_value_definitions_DictionaryName",
                table: "custom_value_definitions"
            );

            migrationBuilder.DropIndex(
                name: "IX_custom_value_definitions_ScalarName",
                table: "custom_value_definitions"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_value_definitions_HostId_Scope_NameHash",
                table: "custom_value_definitions",
                columns: new[] { "HostId", "Scope", "NameHash" },
                unique: true
            );
        }
    }
}
