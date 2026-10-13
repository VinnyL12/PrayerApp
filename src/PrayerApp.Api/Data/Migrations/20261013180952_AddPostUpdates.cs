using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrayerApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PostUpdateId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostUpdates_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_PostUpdateId",
                table: "Notifications",
                column: "PostUpdateId");

            migrationBuilder.CreateIndex(
                name: "IX_PostUpdates_PostId_CreatedAt",
                table: "PostUpdates",
                columns: new[] { "PostId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_PostUpdates_PostUpdateId",
                table: "Notifications",
                column: "PostUpdateId",
                principalTable: "PostUpdates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_PostUpdates_PostUpdateId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "PostUpdates");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_PostUpdateId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "PostUpdateId",
                table: "Notifications");
        }
    }
}
