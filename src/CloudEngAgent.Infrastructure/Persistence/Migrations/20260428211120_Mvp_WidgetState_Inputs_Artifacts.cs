using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudEngAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Mvp_WidgetState_Inputs_Artifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Filename = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentSha256 = table.Column<string>(type: "char(64)", fixedLength: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artifacts_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InputRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SchemaRef = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InputRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputRequests_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunWidgetStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WidgetKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Placement_Surface = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Placement_StepId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Placement_AgentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Placement_ParentMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PropsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArtifactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunWidgetStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunWidgetStates_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_ContentSha256",
                table: "Artifacts",
                column: "ContentSha256");

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_RunId_CreatedAt",
                table: "Artifacts",
                columns: new[] { "RunId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_InputRequests_RunId_Status",
                table: "InputRequests",
                columns: new[] { "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RunWidgetStates_RunId_UpdatedAt",
                table: "RunWidgetStates",
                columns: new[] { "RunId", "UpdatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_RunWidgetStates_RunId_WidgetKey",
                table: "RunWidgetStates",
                columns: new[] { "RunId", "WidgetKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "InputRequests");

            migrationBuilder.DropTable(
                name: "RunWidgetStates");
        }
    }
}
