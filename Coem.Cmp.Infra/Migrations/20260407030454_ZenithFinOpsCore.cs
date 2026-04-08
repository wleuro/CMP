using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coem.Cmp.Infra.Migrations
{
    /// <inheritdoc />
    public partial class ZenithFinOpsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Roles_RoleId",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_ResourceName",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_SubscriptionId_UsageDate",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_UsageDate",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_ResourceName",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_SubscriptionId_UsageDate",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_UsageDate",
                table: "ExternalUsageRecords");

            migrationBuilder.AddColumn<int>(
                name: "RoleId1",
                table: "UserProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ResourceName",
                table: "PCUsageRecords",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargeType",
                table: "PCUsageRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FinOpsCostCenter",
                table: "PCUsageRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinOpsEnvironment",
                table: "PCUsageRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Publisher",
                table: "PCUsageRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PCUsageRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "ResourceName",
                table: "ExternalUsageRecords",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargeType",
                table: "ExternalUsageRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FinOpsCostCenter",
                table: "ExternalUsageRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinOpsEnvironment",
                table: "ExternalUsageRecords",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Publisher",
                table: "ExternalUsageRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ExternalUsageRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_RoleId1",
                table: "UserProfiles",
                column: "RoleId1");

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_FinOpsCostCenter",
                table: "PCUsageRecords",
                column: "FinOpsCostCenter");

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_FinOpsEnvironment",
                table: "PCUsageRecords",
                column: "FinOpsEnvironment");

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_TenantId_UsageDate_SubscriptionId",
                table: "PCUsageRecords",
                columns: new[] { "TenantId", "UsageDate", "SubscriptionId" })
                .Annotation("SqlServer:Include", new[] { "BilledCost", "EstimatedCost", "ResourceName", "ChargeType" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_FinOpsCostCenter",
                table: "ExternalUsageRecords",
                column: "FinOpsCostCenter");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_FinOpsEnvironment",
                table: "ExternalUsageRecords",
                column: "FinOpsEnvironment");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_TenantId_UsageDate_SubscriptionId",
                table: "ExternalUsageRecords",
                columns: new[] { "TenantId", "UsageDate", "SubscriptionId" })
                .Annotation("SqlServer:Include", new[] { "BilledCost", "EstimatedCost", "ResourceName", "ChargeType" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExternalUsageRecords_Tenants_TenantId",
                table: "ExternalUsageRecords",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PCUsageRecords_Tenants_TenantId",
                table: "PCUsageRecords",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Roles_RoleId",
                table: "UserProfiles",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Roles_RoleId1",
                table: "UserProfiles",
                column: "RoleId1",
                principalTable: "Roles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExternalUsageRecords_Tenants_TenantId",
                table: "ExternalUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_PCUsageRecords_Tenants_TenantId",
                table: "PCUsageRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Roles_RoleId",
                table: "UserProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Roles_RoleId1",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_RoleId1",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_FinOpsCostCenter",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_FinOpsEnvironment",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_PCUsageRecords_TenantId_UsageDate_SubscriptionId",
                table: "PCUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_FinOpsCostCenter",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_FinOpsEnvironment",
                table: "ExternalUsageRecords");

            migrationBuilder.DropIndex(
                name: "IX_ExternalUsageRecords_TenantId_UsageDate_SubscriptionId",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "RoleId1",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ChargeType",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "FinOpsCostCenter",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "FinOpsEnvironment",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "Publisher",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PCUsageRecords");

            migrationBuilder.DropColumn(
                name: "ChargeType",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "FinOpsCostCenter",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "FinOpsEnvironment",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "Publisher",
                table: "ExternalUsageRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ExternalUsageRecords");

            migrationBuilder.AlterColumn<string>(
                name: "ResourceName",
                table: "PCUsageRecords",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ResourceName",
                table: "ExternalUsageRecords",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_ResourceName",
                table: "PCUsageRecords",
                column: "ResourceName");

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_SubscriptionId_UsageDate",
                table: "PCUsageRecords",
                columns: new[] { "SubscriptionId", "UsageDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PCUsageRecords_UsageDate",
                table: "PCUsageRecords",
                column: "UsageDate");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_ResourceName",
                table: "ExternalUsageRecords",
                column: "ResourceName");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_SubscriptionId_UsageDate",
                table: "ExternalUsageRecords",
                columns: new[] { "SubscriptionId", "UsageDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUsageRecords_UsageDate",
                table: "ExternalUsageRecords",
                column: "UsageDate");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Roles_RoleId",
                table: "UserProfiles",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
