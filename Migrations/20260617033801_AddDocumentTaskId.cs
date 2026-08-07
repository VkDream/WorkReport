using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkReport.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTaskId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaskId",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TaskId",
                table: "Documents",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Tasks_TaskId",
                table: "Documents",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Tasks_TaskId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TaskId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "Documents");
        }
    }
}
