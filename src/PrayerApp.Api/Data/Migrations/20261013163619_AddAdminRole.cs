using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrayerApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "CommunityMemberships");

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { new Guid("6c1d3b2a-9f4e-4a8b-b5d1-2e7f0a9c4d11"), "b0f7c2de-3a51-4c8e-9d62-7a1e5f4b8c30", "Admin", "ADMIN" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("6c1d3b2a-9f4e-4a8b-b5d1-2e7f0a9c4d11"));

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "CommunityMemberships",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
