using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RouteCustomCommandRepliesByArgumentCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_MessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropIndex(
                name: "IX_custom_command_actions_HostId_MessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.RenameColumn(
                name: "MessageLibraryEntryId",
                table: "custom_command_actions",
                newName: "ZeroArgumentMessageLibraryEntryId"
            );

            migrationBuilder.AlterColumn<int>(
                name: "ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER"
            );

            migrationBuilder.AddColumn<int>(
                name: "OneArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "TwoArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_OneArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "OneArgumentMessageLibraryEntryId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_TwoArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "TwoArgumentMessageLibraryEntryId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "ZeroArgumentMessageLibraryEntryId" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_OneArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "OneArgumentMessageLibraryEntryId" },
                principalTable: "custom_message_library_entries",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_TwoArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "TwoArgumentMessageLibraryEntryId" },
                principalTable: "custom_message_library_entries",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "ZeroArgumentMessageLibraryEntryId" },
                principalTable: "custom_message_library_entries",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_OneArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_TwoArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropIndex(
                name: "IX_custom_command_actions_HostId_OneArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropIndex(
                name: "IX_custom_command_actions_HostId_TwoArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropIndex(
                name: "IX_custom_command_actions_HostId_ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.Sql(
                """
                UPDATE custom_command_actions
                SET ZeroArgumentMessageLibraryEntryId = COALESCE(
                    ZeroArgumentMessageLibraryEntryId,
                    OneArgumentMessageLibraryEntryId,
                    TwoArgumentMessageLibraryEntryId
                )
                """
            );

            migrationBuilder.DropColumn(
                name: "OneArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.DropColumn(
                name: "TwoArgumentMessageLibraryEntryId",
                table: "custom_command_actions"
            );

            migrationBuilder.AlterColumn<int>(
                name: "ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true
            );

            migrationBuilder.RenameColumn(
                name: "ZeroArgumentMessageLibraryEntryId",
                table: "custom_command_actions",
                newName: "MessageLibraryEntryId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_custom_command_actions_HostId_MessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "MessageLibraryEntryId" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_custom_command_actions_custom_message_library_entries_HostId_MessageLibraryEntryId",
                table: "custom_command_actions",
                columns: new[] { "HostId", "MessageLibraryEntryId" },
                principalTable: "custom_message_library_entries",
                principalColumns: new[] { "HostId", "Id" },
                onDelete: ReferentialAction.Restrict
            );
        }
    }
}
