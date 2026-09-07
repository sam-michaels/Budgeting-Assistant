using BudgetAssistant.Core.Analysis;
using BudgetAssistant.Core.Entities;
using Npgsql;
using Pgvector;

namespace BudgetAssistant.Web.Services;

/// <summary>
/// k-NN retrieval over pgvector. Issued as raw SQL rather than LINQ so the distance
/// operator and the ORDER BY ... LIMIT shape that lets Postgres use the HNSW index are
/// both explicit.
/// </summary>
public sealed class VectorSearch(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Nearest labelled neighbours for a merchant embedding: the seeded example corpus
    /// plus anything this user has categorized by hand. Manual corrections therefore
    /// improve later predictions, which is the whole point of storing them.
    /// </summary>
    // ponytail: UNION ALL over two tables means the planner may not use the HNSW index
    // on both arms. Irrelevant at demo scale (~1k rows); split into two indexed queries
    // and merge in memory if the corpus ever grows past ~100k.
    public async Task<List<Neighbor>> FindLabeledNeighborsAsync(
        float[] embedding, string userId, int limit = 10, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "CategoryId", label, sim FROM (
                SELECT m."CategoryId", m."Text" AS label,
                       1 - (m."Embedding" <=> @q) AS sim
                FROM "MerchantExamples" m
                WHERE m."Embedding" IS NOT NULL
                UNION ALL
                SELECT t."CategoryId", t."NormalizedMerchant" AS label,
                       1 - (t."Embedding" <=> @q) AS sim
                FROM "Transactions" t
                JOIN "Accounts" a ON a."Id" = t."AccountId"
                WHERE a."UserId" = @userId
                  AND t."CategoryId" IS NOT NULL
                  AND t."CategorySource" = @manual
                  AND t."Embedding" IS NOT NULL
            ) candidates
            ORDER BY sim DESC
            LIMIT @limit;
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("q", new Vector(embedding));
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("manual", (int)CategorySource.Manual);
        cmd.Parameters.AddWithValue("limit", limit);

        var results = new List<Neighbor>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new Neighbor(reader.GetInt32(0), reader.GetString(1), (float)reader.GetDouble(2)));
        return results;
    }
}
