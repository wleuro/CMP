using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Subscriptions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Subscriptions");
        }
    }
}
