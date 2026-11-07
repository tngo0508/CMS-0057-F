using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cms0057.PriorAuthorization.Data.Migrations
{
    /// <inheritdoc />
    public partial class PayerToPayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayerToPayerChangedAt",
                table: "Members",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayerToPayerOptedIn",
                table: "Members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MemberMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayerIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MemberIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsentEnds = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberMatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberMatches_PayerIdentifier_MemberIdentifier",
                table: "MemberMatches",
                columns: new[] { "PayerIdentifier", "MemberIdentifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberMatches");

            migrationBuilder.DropColumn(
                name: "PayerToPayerChangedAt",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PayerToPayerOptedIn",
                table: "Members");
        }
    }
}
