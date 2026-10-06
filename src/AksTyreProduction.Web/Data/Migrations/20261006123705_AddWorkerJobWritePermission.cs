using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AksTyreProduction.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerJobWritePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanWriteJobs",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanWriteJobs",
                table: "Users");
        }
    }
}
