using Npgsql;

public class PostgresVectorSearch : IVectorSearch
{
    private readonly AzureOpenAIChatCompletion _azureOpenAI;
    private readonly NpgsqlDataSource _postgresDatabase;
    public PostgresVectorSearch(AzureOpenAIChatCompletion azureOpenAI, NpgsqlDataSource postgresDatabase)
    {
        _azureOpenAI = azureOpenAI;
        _postgresDatabase = postgresDatabase;
    }
    public async Task<List<string>> BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks)
    {
        List<string> results = new();
        await CreateSchemaAndIndexAsync();
        results.Add("Created index");

        foreach (var chunk in chunks)
		{
			results.Add($"Added page {chunk.PageId}");
		}

        results.Add("Done!");
		return results;
    }

    public async Task<string[]> SearchAsync(string question, int limit = 5)
    {
        throw new NotImplementedException();
    }

    private async Task CreateSchemaAndIndexAsync()
    {

    }
}