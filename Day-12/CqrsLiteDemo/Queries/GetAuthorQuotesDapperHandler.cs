using Dapper;
using Microsoft.Data.Sqlite;
using CqrsLiteDemo.Models.Read;

namespace CqrsLiteDemo.Queries;

public class GetAuthorQuotesDapperHandler
{
    public const string Sql = """
        SELECT a.Id AS AuthorId, a.Name AS AuthorName, q.Id AS QuoteId, q.Text AS QuoteText
        FROM Quotes AS q
        INNER JOIN Authors AS a ON q.AuthorId = a.Id
        WHERE q.AuthorId = @AuthorId
        """;

    private readonly string _connectionString;

    public GetAuthorQuotesDapperHandler(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<AuthorQuoteReadModel>> HandleAsync(GetAuthorQuotesQuery query)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var rows = await connection.QueryAsync<AuthorQuoteReadModel>(Sql, new { AuthorId = query.AuthorId });
        return rows.AsList();
    }
}
