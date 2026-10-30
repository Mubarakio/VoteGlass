using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoteGlass.Migrations
{
    /// <inheritdoc />
    public partial class AddVoterRegistrationNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "VoterProfiles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_VoterProfiles_PhoneNumber",
                table: "VoterProfiles",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoterProfiles_RegistrationNumber",
                table: "VoterProfiles",
                column: "RegistrationNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoterProfiles_PhoneNumber",
                table: "VoterProfiles");

            migrationBuilder.DropIndex(
                name: "IX_VoterProfiles_RegistrationNumber",
                table: "VoterProfiles");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "VoterProfiles");
        }
    }
}
