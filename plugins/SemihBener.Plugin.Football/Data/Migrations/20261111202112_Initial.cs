using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemihBener.Plugin.Football.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    League = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Season = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HomeTeam = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AwayTeam = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: true),
                    AwayScore = table.Column<int>(type: "int", nullable: true),
                    KickoffUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_AwayTeam",
                table: "Matches",
                column: "AwayTeam");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HomeTeam",
                table: "Matches",
                column: "HomeTeam");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_League_Season_KickoffUtc",
                table: "Matches",
                columns: new[] { "League", "Season", "KickoffUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Matches");
        }
    }
}
