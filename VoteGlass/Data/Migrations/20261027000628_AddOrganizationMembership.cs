using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoteGlass.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoterProfiles_RegistrationNumber",
                table: "VoterProfiles");

            migrationBuilder.DropColumn(
                name: "ManualReviewOnly",
                table: "VoterProfiles");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "VoterProfiles");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "VoterProfiles");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "VoterProfiles");

            migrationBuilder.AddColumn<bool>(
                name: "Blocked",
                table: "RosterEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "RosterEntries",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Blocked",
                table: "RosterEntries");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "RosterEntries");

            migrationBuilder.AddColumn<bool>(
                name: "ManualReviewOnly",
                table: "VoterProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "VoterProfiles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "VoterProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "VoterProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoterProfiles_RegistrationNumber",
                table: "VoterProfiles",
                column: "RegistrationNumber",
                unique: true);
        }
    }
}
