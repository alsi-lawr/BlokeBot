using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v070_CustomCommandSelectedUserAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ModeratorOnly",
                table: "custom_commands",
                newName: "AllowModerators"
            );

            migrationBuilder.AddColumn<bool>(
                name: "AllowEveryone",
                table: "custom_commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.Sql(
                """
                UPDATE custom_commands
                SET AllowEveryone = CASE WHEN AllowModerators = 1 THEN 0 ELSE 1 END
                """
            );

            migrationBuilder.CreateTable(
                name: "custom_command_allowed_users",
                columns: table => new
                {
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomCommandId = table.Column<int>(type: "INTEGER", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    Login = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_custom_command_allowed_users",
                        x => new
                        {
                            x.HostId,
                            x.CustomCommandId,
                            x.TwitchUserId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_custom_command_allowed_users_custom_commands_HostId_CustomCommandId",
                        columns: x => new { x.HostId, x.CustomCommandId },
                        principalTable: "custom_commands",
                        principalColumns: new[] { "HostId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "custom_command_allowed_users");

            migrationBuilder.DropColumn(name: "AllowEveryone", table: "custom_commands");

            migrationBuilder.RenameColumn(
                name: "AllowModerators",
                table: "custom_commands",
                newName: "ModeratorOnly"
            );
        }
    }
}
