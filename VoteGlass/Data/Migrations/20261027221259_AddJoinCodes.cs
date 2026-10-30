using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoteGlass.Migrations
{
    /// <inheritdoc />
    public partial class AddJoinCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JoinCode",
                table: "Organizations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_JoinCode",
                table: "Organizations",
                column: "JoinCode",
                unique: true,
                filter: "[JoinCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_JoinCode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "JoinCode",
                table: "Organizations");
        }
    }
}
