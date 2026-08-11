using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlokeBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class v0100_Collectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CollectivesAcceptWorkAfterUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "CollectivesPausedAtUtc",
                table: "hosts",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "collectives",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collectives", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_audits",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    Action = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    ActingHostId = table.Column<int>(type: "INTEGER", nullable: false),
                    AffectedHostId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ActorLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: false
                    ),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_audits_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_goals",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    UnitName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Target = table.Column<long>(type: "INTEGER", nullable: false),
                    Current = table.Column<long>(type: "INTEGER", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_goals_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_local_settings",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Notification = table.Column<string>(
                        type: "TEXT",
                        maxLength: 48,
                        nullable: false
                    ),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_local_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_local_settings_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_memberships",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    AcceptWorkAfterUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InvitedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_memberships_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_collective_memberships_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_raid_relays",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CurrentHostId = table.Column<int>(type: "INTEGER", nullable: false),
                    NextHostId = table.Column<int>(type: "INTEGER", nullable: true),
                    AggregateViewerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSourceEventAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_raid_relays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_raid_relays_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_tournament_references",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveId = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerHostId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitionPublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    EntrantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmedResultCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSourceEventAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_tournament_references", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_tournament_references_collectives_CollectiveId",
                        column: x => x.CollectiveId,
                        principalTable: "collectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_goal_host_totals",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveGoalId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceBountyPublicId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Total = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSourceEventAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_goal_host_totals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_goal_host_totals_collective_goals_CollectiveGoalId",
                        column: x => x.CollectiveGoalId,
                        principalTable: "collective_goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "collective_raid_handoffs",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectiveRaidRelayId = table.Column<long>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 160,
                        nullable: false
                    ),
                    FromHostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToHostId = table.Column<int>(type: "INTEGER", nullable: false),
                    AggregateViewerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_raid_handoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collective_raid_handoffs_collective_raid_relays_CollectiveRaidRelayId",
                        column: x => x.CollectiveRaidRelayId,
                        principalTable: "collective_raid_relays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_audits_CollectiveId_OperationId",
                table: "collective_audits",
                columns: new[] { "CollectiveId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_goal_host_totals_CollectiveGoalId_HostId",
                table: "collective_goal_host_totals",
                columns: new[] { "CollectiveGoalId", "HostId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_goal_host_totals_HostId_SourceBountyPublicId",
                table: "collective_goal_host_totals",
                columns: new[] { "HostId", "SourceBountyPublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_goals_CollectiveId",
                table: "collective_goals",
                column: "CollectiveId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_local_settings_CollectiveId_HostId",
                table: "collective_local_settings",
                columns: new[] { "CollectiveId", "HostId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_memberships_CollectiveId_HostId",
                table: "collective_memberships",
                columns: new[] { "CollectiveId", "HostId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_memberships_HostId",
                table: "collective_memberships",
                column: "HostId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_raid_handoffs_CollectiveRaidRelayId_OperationId",
                table: "collective_raid_handoffs",
                columns: new[] { "CollectiveRaidRelayId", "OperationId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_raid_relays_CollectiveId",
                table: "collective_raid_relays",
                column: "CollectiveId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_tournament_references_CollectiveId",
                table: "collective_tournament_references",
                column: "CollectiveId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collective_tournament_references_OwnerHostId_CompetitionPublicId",
                table: "collective_tournament_references",
                columns: new[] { "OwnerHostId", "CompetitionPublicId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_collectives_CreationOperationId",
                table: "collectives",
                column: "CreationOperationId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_collectives_PublicId",
                table: "collectives",
                column: "PublicId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "collective_audits");

            migrationBuilder.DropTable(name: "collective_goal_host_totals");

            migrationBuilder.DropTable(name: "collective_local_settings");

            migrationBuilder.DropTable(name: "collective_memberships");

            migrationBuilder.DropTable(name: "collective_raid_handoffs");

            migrationBuilder.DropTable(name: "collective_tournament_references");

            migrationBuilder.DropTable(name: "collective_goals");

            migrationBuilder.DropTable(name: "collective_raid_relays");

            migrationBuilder.DropTable(name: "collectives");

            migrationBuilder.DropColumn(name: "CollectivesAcceptWorkAfterUtc", table: "hosts");

            migrationBuilder.DropColumn(name: "CollectivesPausedAtUtc", table: "hosts");
        }
    }
}
