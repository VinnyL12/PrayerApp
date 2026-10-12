using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrayerApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FeedIndexTieBreaker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_CommunityId_Status_CreatedAt",
                table: "Posts");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CommunityId_Status_CreatedAt_Id",
                table: "Posts",
                columns: new[] { "CommunityId", "Status", "CreatedAt", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_CommunityId_Status_CreatedAt_Id",
                table: "Posts");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CommunityId_Status_CreatedAt",
                table: "Posts",
                columns: new[] { "CommunityId", "Status", "CreatedAt" },
                descending: new[] { false, false, true });
        }
    }
}
