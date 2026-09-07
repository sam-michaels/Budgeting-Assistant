using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetAssistant.Web.Data.Migrations
{
    /// <summary>
    /// HNSW indexes for approximate nearest-neighbour search.
    ///
    /// Written as raw SQL because EF has no model concept for a vector index. The
    /// vector_cosine_ops operator class must match the distance operator the queries use
    /// (&lt;=&gt;, cosine) — an index built for a different operator class is silently
    /// ignored by the planner rather than reported as an error.
    /// </summary>
    public partial class VectorIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Transactions_Embedding_hnsw"
                ON "Transactions" USING hnsw ("Embedding" vector_cosine_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_MerchantExamples_Embedding_hnsw"
                ON "MerchantExamples" USING hnsw ("Embedding" vector_cosine_ops);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Transactions_Embedding_hnsw"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_MerchantExamples_Embedding_hnsw"";");
        }
    }
}
