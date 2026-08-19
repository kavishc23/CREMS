using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalStageDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [ApprovalRequests] SET [CurrentStage] = 1 WHERE [CurrentStage] < 1; UPDATE [ApprovalRequests] SET [TotalStages] = 1 WHERE [TotalStages] < 1;");
            migrationBuilder.AlterColumn<int>(
                name: "TotalStages",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CurrentStage",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TotalStages",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CurrentStage",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);
        }
    }
}
