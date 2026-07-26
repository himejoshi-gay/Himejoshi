using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sunrise.Shared.Database;

#nullable disable

namespace Sunrise.Shared.Database.Migrations;

[DbContext(typeof(SunriseDbContext))]
[Migration("20260726000000_AddScoreClockRate")]
public partial class AddScoreClockRate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "ClockRate",
            table: "score",
            type: "double",
            nullable: false,
            defaultValue: 1.0);

        migrationBuilder.Sql("UPDATE score SET ClockRate = 1.5 WHERE (Mods & 64) <> 0 OR (Mods & 512) <> 0");
        migrationBuilder.Sql("UPDATE score SET ClockRate = 0.75 WHERE (Mods & 256) <> 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClockRate",
            table: "score");
    }
}
