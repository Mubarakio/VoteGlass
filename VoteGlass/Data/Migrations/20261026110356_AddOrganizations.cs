using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoteGlass.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_VoterProfiles_ClaimedByVoterProfileId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_ClaimedByVoterProfileId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_RegistrationNumber",
                table: "RosterEntries");

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "VoterGroups",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "RosterEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Elections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentOrganizationId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSuspended = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoterGroups_OrganizationId_Name",
                table: "VoterGroups",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_OrganizationId_ClaimedByVoterProfileId",
                table: "RosterEntries",
                columns: new[] { "OrganizationId", "ClaimedByVoterProfileId" },
                unique: true,
                filter: "[ClaimedByVoterProfileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_OrganizationId_RegistrationNumber",
                table: "RosterEntries",
                columns: new[] { "OrganizationId", "RegistrationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Elections_OrganizationId",
                table: "Elections",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId_UserId",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_UserId",
                table: "OrganizationMembers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Elections_Organizations_OrganizationId",
                table: "Elections",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_Organizations_OrganizationId",
                table: "RosterEntries",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VoterGroups_Organizations_OrganizationId",
                table: "VoterGroups",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Elections_Organizations_OrganizationId",
                table: "Elections");

            migrationBuilder.DropForeignKey(
                name: "FK_RosterEntries_Organizations_OrganizationId",
                table: "RosterEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_VoterGroups_Organizations_OrganizationId",
                table: "VoterGroups");

            migrationBuilder.DropTable(
                name: "OrganizationMembers");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_VoterGroups_OrganizationId_Name",
                table: "VoterGroups");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_OrganizationId_ClaimedByVoterProfileId",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_RosterEntries_OrganizationId_RegistrationNumber",
                table: "RosterEntries");

            migrationBuilder.DropIndex(
                name: "IX_Elections_OrganizationId",
                table: "Elections");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "VoterGroups");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "RosterEntries");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Elections");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CurrentOrganizationId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_ClaimedByVoterProfileId",
                table: "RosterEntries",
                column: "ClaimedByVoterProfileId",
                unique: true,
                filter: "[ClaimedByVoterProfileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_RegistrationNumber",
                table: "RosterEntries",
                column: "RegistrationNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RosterEntries_VoterProfiles_ClaimedByVoterProfileId",
                table: "RosterEntries",
                column: "ClaimedByVoterProfileId",
                principalTable: "VoterProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
