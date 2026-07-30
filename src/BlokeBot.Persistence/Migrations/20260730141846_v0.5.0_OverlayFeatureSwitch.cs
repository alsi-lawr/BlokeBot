using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v050_OverlayFeatureSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "hosts" SET "EnabledFeatures" = "EnabledFeatures" | 16;"""
            );

            migrationBuilder.AlterColumn<long>(
                name: "EnabledFeatures",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 31L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 15L
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE "hosts" SET "EnabledFeatures" = "EnabledFeatures" & -17;"""
            );

            migrationBuilder.AlterColumn<long>(
                name: "EnabledFeatures",
                table: "hosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 31L
            );
        }
    }
}
