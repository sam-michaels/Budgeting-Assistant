using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetAssistant.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TransactionCategoryMatchedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryMatchedOn",
                table: "Transactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryMatchedOn",
                table: "Transactions");
        }
    }
}
