using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cms0057.PriorAuthorization.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProviderAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderAccessChangedAt",
                table: "Members",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProviderAccessOptedOut",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Attributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MemberIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AttributedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attributions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attributions_ProviderIdentifier_MemberIdentifier",
                table: "Attributions",
                columns: new[] { "ProviderIdentifier", "MemberIdentifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attributions");

            migrationBuilder.DropColumn(
                name: "ProviderAccessChangedAt",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ProviderAccessOptedOut",
                table: "Members");
        }
    }
}
