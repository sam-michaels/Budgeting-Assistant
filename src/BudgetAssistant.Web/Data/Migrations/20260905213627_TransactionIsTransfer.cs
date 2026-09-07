using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetAssistant.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransactionIsTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "Transactions");
        }
    }
}
