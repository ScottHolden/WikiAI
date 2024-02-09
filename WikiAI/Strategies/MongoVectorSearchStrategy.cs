using MongoDB.Bson;
using MongoDB.Driver;
using WikiAI.Strategies;

namespace WikiAI;

public class MongoVectorSearchStrategy(
    MongoConfig _config,
    WikiSourceReferenceBuilder _referenceBuilder,
    AzureOpenAIChatCompletion _azureOpenAI,
    ILogger<MongoVectorSearchStrategy> _logger
) : IStrategy, IIndexer
{
    private readonly IMongoCollection<BsonDocument> _vectors = GetVectorMongoCollection(_config);

    public string Name => "mongoVector";
    public string DisplayName => "MongoDB Vector Search + Wiki";

    public async Task BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks)
    {
        _logger.LogInformation("Creating search index...");
        await EnsureIndexExistsAsync();

        _logger.LogInformation("Upserting chunks...");
        await UpsertChunksAsync(chunks);

        _logger.LogInformation("Done!");
    }

    public async Task<StrategyResponse> GetResponseAsync(string question)
    {
        var pageIds = await SearchAsync(question);
        var sources = await _referenceBuilder.BuildSourcesFromPageIdsAsync(pageIds);

        return new StrategyResponse(sources, "Answered using MongoDB Vector Search + Wiki", null);
    }

    private static IMongoCollection<BsonDocument> GetVectorMongoCollection(MongoConfig config)
    {
        var client = new MongoClient(config.MONGO_CONNECTION_STRING);
        var databaseName = string.IsNullOrWhiteSpace(config.MONGO_DATABASE_NAME) ? "wiki" : config.MONGO_DATABASE_NAME;
        var database = client.GetDatabase(databaseName);
        return database.GetCollection<BsonDocument>("vectors");
    }

    private async Task<string[]> SearchAsync(string question, int limit = 5)
    {
        // Convert from a search string into vectors
        var vector = await _azureOpenAI.GetEmbeddingAsync(question);

        // Query our CosmosDB running Mongo vCore
        var pipeline = BuildEmbeddingsSearchPipeline(vector, limit);
        var result = await _vectors.Aggregate<BsonDocument>(pipeline).ToListAsync();

        return result.Select(x => x["pageId"].AsString).ToArray();
    }

    private static BsonDocument[] BuildEmbeddingsSearchPipeline(IEnumerable<float> vector, int maxRecords)
        => [
            BsonDocument.Parse($"{{$search: {{cosmosSearch: {{ vector: [{string.Join(',', vector)}], path: 'vector', k: {maxRecords}}}, returnStoredSource:true}}}}"),
            BsonDocument.Parse($"{{$project: {{\"similarityScore\":{{\"$meta\":\"searchScore\"}}, pageId: 1}}}}"),
        ];

    public async Task UpsertChunksAsync(IReadOnlyList<VectorChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            try
            {
                await UpsertVectorAsync(new PageToInsert(chunk.PageId, chunk.PageId, chunk.Vector));
                _logger.LogInformation("Added page {pageId}", chunk.PageId);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error adding page {pageId}: {message}", chunk.PageId, e.Message);
            }
        }
    }


    private record PageToInsert(string _id, string pageId, float[]? vector);

    private async Task UpsertVectorAsync(PageToInsert session)
    {
        var document = session.ToBsonDocument();
        string? _idValue = document["_id"].ToString();
        var filter = Builders<BsonDocument>.Filter.Eq("_id", _idValue);
        var options = new ReplaceOptions { IsUpsert = true };
        await _vectors.ReplaceOneAsync(filter, document, options);
    }

    private async Task EnsureIndexExistsAsync()
    {
        string vectorIndexName = "vectorSearchIndex";
        using IAsyncCursor<BsonDocument> indexCursor = _vectors.Indexes.List();
        bool vectorIndexExists = indexCursor.ToList().Any(x => x["name"] == vectorIndexName);
        if (vectorIndexExists) return;
        BsonDocumentCommand<BsonDocument> command = new(
        BsonDocument.Parse(@"
							{ createIndexes: 'vectors', 
								indexes: [{ 
								name: 'vectorSearchIndex', 
								key: { vector: 'cosmosSearch' }, 
								cosmosSearchOptions: { kind: 'vector-ivf', numLists: 5, similarity: 'COS', dimensions: 1536 } 
								}] 
							}"
        ));

        BsonDocument result = await _vectors.Database.RunCommandAsync(command);
        if (result["ok"] != 1) throw new Exception("CreateIndex failed with response: " + result.ToJson());
    }
}
