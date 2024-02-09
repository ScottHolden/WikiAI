using Npgsql;

namespace WikiAI.Strategies;

public class PostgresVectorStrategy(
    PostgresConfig _config,
    AzureOpenAIChatCompletion _azureOpenAI,
    WikiSourceReferenceBuilder _referenceBuilder,
    ILogger<PostgresVectorStrategy> _logger
) : IStrategy, IIndexer
{
    private const int MaxSearchResults = 5;

    private readonly NpgsqlDataSource _dataSource = ConnectToPostgres(_config);
    public string Name => "postgresVector";
    public string DisplayName => "Postgres Vector Search + Wiki";
    public async Task BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks)
    {
        _logger.LogInformation("Setting up database...");
        await EnsureTableAndIndexExist();
        _logger.LogInformation("Upserting chunks...");
        await UpsertChunks(chunks);
        _logger.LogInformation("Done!");
    }

    public async Task<StrategyResponse> GetResponseAsync(string question)
    {
        var pageIds = await SearchAsync(question, MaxSearchResults);
        var sources = await _referenceBuilder.BuildSourcesFromPageIdsAsync(pageIds);

        return new StrategyResponse(sources, "Answered using Postgres Vector Search + Wiki", null);
    }

    private async Task<string[]> SearchAsync(string input, int limit)
        => await SearchAsync(await _azureOpenAI.GetEmbeddingAsync(input), limit);
    private async Task<string[]> SearchAsync(float[] vector, int limit)
    {
        await using var command = _dataSource.CreateCommand("""
			SELECT id FROM confluence 
			ORDER BY embedding <-> @vec 
			LIMIT @limit;
			""");
        command.Parameters.Add(new NpgsqlParameter("vec", vector));
        command.Parameters.Add(new NpgsqlParameter("limit", limit));

        await using var reader = command.ExecuteReader();

        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }
        return [.. results]; //Todo: cleanup
    }

    private async Task UpsertChunks(IReadOnlyList<VectorChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            try
            {
                await UpsertChunk(chunk);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to upsert chunk: {message}", e.Message);
            }
        }
    }

    private async Task UpsertChunk(VectorChunk chunk)
    {
        await using var command = _dataSource.CreateCommand("""
			INSERT INTO confluence (id, embedding)
			VALUES (@id, @vec)
			ON CONFLICT (id)
			DO UPDATE SET
			  embedding = EXCLUDED.embedding;
			""");
        command.Parameters.Add(new NpgsqlParameter("id", chunk.PageId));
        command.Parameters.Add(new NpgsqlParameter("vec", chunk.Vector));
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureTableAndIndexExist()
    {
        await ExecuteNonQueryAsync("CREATE EXTENSION IF NOT EXISTS vector;");
        await ExecuteNonQueryAsync("""
			CREATE TABLE IF NOT EXISTS confluence (
				id varchar(256) UNIQUE,
				embedding vector(1536)
			);
			""");
        // TODO: Add index.
    }

    private async Task ExecuteNonQueryAsync(string query)
    {
        await using var command = _dataSource.CreateCommand(query);
        await command.ExecuteNonQueryAsync();
    }

    private static NpgsqlDataSource ConnectToPostgres(PostgresConfig config)
        => NpgsqlDataSource.Create(config.POSTGRES_CONNECTION_STRING!);
}
