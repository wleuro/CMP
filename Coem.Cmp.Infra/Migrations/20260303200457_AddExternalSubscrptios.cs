using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalSubscrptios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AzureDirectCredentialId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuditResult = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSync = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalSubscriptions_AzureDirectCredentials_AzureDirectCredentialId",
                        column: x => x.AzureDirectCredentialId,
                        principalTable: "AzureDirectCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalSubscriptions_AzureDirectCredentialId",
                table: "ExternalSubscriptions",
                column: "AzureDirectCredentialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalSubscriptions");
        }
    }
}
