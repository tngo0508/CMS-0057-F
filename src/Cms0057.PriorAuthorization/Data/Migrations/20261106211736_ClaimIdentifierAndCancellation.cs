using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cms0057.PriorAuthorization.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClaimIdentifierAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimIdentifier",
                table: "Requests",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Cancelled",
                table: "RequestItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ClaimIdentifier",
                table: "Requests",
                column: "ClaimIdentifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Requests_ClaimIdentifier",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ClaimIdentifier",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Cancelled",
                table: "RequestItems");
        }
    }
}
