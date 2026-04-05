using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddMaresaGranularity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResourceId",
                table: "PCUsageRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceName",
                table: "PCUsageRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "PCUsageRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceId",
                table: "ExternalUsageRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceName",
                table: "ExternalUsageRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "ExternalUsageRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_ResourceName",
                table: "PCUsageRecords",
                column: "ResourceName");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ResourceName",
                table: "ExternalUsageRecords",
                column: "ResourceName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_ResourceName",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_ResourceName",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "ResourceName",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "ResourceName",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "ExternalUsageRecords");
        }
    }
}
