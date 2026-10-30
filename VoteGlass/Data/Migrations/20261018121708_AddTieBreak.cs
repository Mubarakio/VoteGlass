using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoteGlass.Migrations
{
    /// <inheritdoc />
    public partial class AddTieBreak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TieBreakWinners",
                table: "Positions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TieBreakWinners",
                table: "Positions");
        }
    }
}
