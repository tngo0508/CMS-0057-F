using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cms0057.PriorAuthorization.Data.Migrations
{
    /// <inheritdoc />
    public partial class Expedited : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Expedited",
                table: "Requests",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Expedited",
                table: "Requests");
        }
    }
}
