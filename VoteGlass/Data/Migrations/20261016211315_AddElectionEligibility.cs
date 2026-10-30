using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoteGlass.Migrations
{
    /// <inheritdoc />
    public partial class AddElectionEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OpenToEveryone",
                table: "Elections",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenToEveryone",
                table: "Elections");
        }
    }
}
