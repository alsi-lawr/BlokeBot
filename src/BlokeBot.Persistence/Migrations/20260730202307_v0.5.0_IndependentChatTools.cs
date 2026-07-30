using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v050_IndependentChatTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "hosts"
                SET "EnabledFeatures" =
                    "EnabledFeatures"
                    | 224
                    | CASE
                        WHEN ("EnabledFeatures" & 8) = 8 THEN 3840
                        ELSE 0
                      END;
                """
            );

            migrationBuilder.AlterColumn<long>(
                name: "EnabledFeatures",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 31L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "hosts"
                SET "EnabledFeatures" =
                    ("EnabledFeatures" & -4073)
                    | CASE
                        WHEN ("EnabledFeatures" & 3848) = 3848 THEN 8
                        ELSE 0
                      END;
                """
            );

            migrationBuilder.AlterColumn<long>(
                name: "EnabledFeatures",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 31L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L
            );
        }
    }
}
