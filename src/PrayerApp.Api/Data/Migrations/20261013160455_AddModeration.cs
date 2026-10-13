using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrayerApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Edited by hand: existing posts must start Visible (1). The generated 0 isn't a
            // valid ModerationStatus, so every existing post would have dropped out of view.
            migrationBuilder.AddColumn<int>(
                name: "ModerationStatus",
                table: "Posts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Resolution = table.Column<int>(type: "int", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_AspNetUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_PostId_Open",
                table: "Reports",
                column: "PostId",
                filter: "[ResolvedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_PostId_ReporterUserId",
                table: "Reports",
                columns: new[] { "PostId", "ReporterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterUserId",
                table: "Reports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ResolvedByUserId",
                table: "Reports",
                column: "ResolvedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropColumn(
                name: "ModerationStatus",
                table: "Posts");
        }
    }
}
