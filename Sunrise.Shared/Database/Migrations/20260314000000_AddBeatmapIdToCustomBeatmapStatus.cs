using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sunrise.Shared.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBeatmapIdToCustomBeatmapStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BeatmapId",
                table: "custom_beatmap_status",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_custom_beatmap_status_BeatmapId",
                table: "custom_beatmap_status",
                column: "BeatmapId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_custom_beatmap_status_BeatmapId",
                table: "custom_beatmap_status");

            migrationBuilder.DropColumn(
                name: "BeatmapId",
                table: "custom_beatmap_status");
        }
    }
}