using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementTypeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgreementType",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillingAccountId",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgreementType",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BillingAccountId",
                table: "Tenants");
        }
    }
}
