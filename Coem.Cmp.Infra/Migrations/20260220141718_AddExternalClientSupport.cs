using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalClientSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExternalDirectoryId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBilledByCoem",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalDirectoryId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsBilledByCoem",
                table: "Tenants");
        }
    }
}
