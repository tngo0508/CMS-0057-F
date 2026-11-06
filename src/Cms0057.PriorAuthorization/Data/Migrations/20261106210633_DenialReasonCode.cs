using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cms0057.PriorAuthorization.Data.Migrations
{
    /// <inheritdoc />
    public partial class DenialReasonCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DenialReasonCode",
                table: "Requests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DenialReasonDisplay",
                table: "Requests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DenialReasonCode",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "DenialReasonDisplay",
                table: "Requests");
        }
    }
}
