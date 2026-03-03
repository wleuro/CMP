using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageRecordsForFinOps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostRecords_Tenants_TenantId",
                table: "CostRecords");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CostRecords",
                table: "CostRecords");

            migrationBuilder.DropIndex(
                name: "IX_CostRecords_TenantId_UsageDate",
                table: "CostRecords");

            migrationBuilder.RenameTable(
                name: "CostRecords",
                newName: "CostRecord");

           migrationBuilder.AddPrimaryKey(
                name: "PK_CostRecord",
                table: "CostRecord",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "UsageRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceSubCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsageDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageRecords_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Id",
                table: "Subscriptions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_CostRecord_TenantId",
                table: "CostRecord",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageRecords_TenantId_UsageDate",
                table: "UsageRecords",
                columns: new[] { "TenantId", "UsageDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageRecords_UsageDate",
                table: "UsageRecords",
                column: "UsageDate");

            migrationBuilder.AddForeignKey(
                name: "FK_CostRecord_Tenants_TenantId",
                table: "CostRecord",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostRecord_Tenants_TenantId",
                table: "CostRecord");

            migrationBuilder.DropTable(
                name: "PartnerCenterCredentials");

            migrationBuilder.DropTable(
                name: "UsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_Id",
                table: "Subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CostRecord",
                table: "CostRecord");

            migrationBuilder.DropIndex(
                name: "IX_CostRecord_TenantId",
                table: "CostRecord");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Tenants");

            migrationBuilder.RenameTable(
                name: "CostRecord",
                newName: "CostRecords");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CostRecords",
                table: "CostRecords",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_CostRecords_TenantId_UsageDate",
                table: "CostRecords",
                columns: new[] { "TenantId", "UsageDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_CostRecords_Tenants_TenantId",
                table: "CostRecords",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
