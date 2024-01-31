using MongoDB.Bson;
using MongoDB.Driver;

public class VectorSearch : IVectorSearch
{
	private readonly IMongoCollection<BsonDocument> _vectors;
	private readonly AzureOpenAIChatCompletion _azureOpenAI;
	public VectorSearch(AzureOpenAIChatCompletion azureOpenAI, IMongoDatabase mongoDatabase)
	{
		_azureOpenAI = azureOpenAI;
		_vectors = mongoDatabase.GetCollection<BsonDocument>("vectors"); ;
	}

	public async Task<List<string>> BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks)
	{
		List<string> results = new();
		await CreateIndexAsync(_vectors);
		results.Add("Created index");
		int failureCount = 0;

		foreach (var chunk in chunks)
		{
			try
			{
				await UpsertVectorAsync(new PageToInsert(chunk.PageId, chunk.PageId, chunk.Vector));
				results.Add($"Added page {chunk.PageId}");
			}
			catch (Exception e)
			{
				results.Add($"Error adding page {chunk.PageId}: {e.Message}");
				failureCount++;
				if (failureCount > 5) return results;
			}
		}
		results.Add("Done!");
		return results;
	}
	public async Task<string[]> SearchAsync(string question, int limit = 5)
	{
		// Convert from a search string into vectors
		var vector = await _azureOpenAI.GetEmbeddingAsync(question);

		// Query our CosmosDB running Mongo vCore
		var pipeline = BuildEmbeddingsSearchPipeline(vector, limit);
		var result = await _vectors.Aggregate<BsonDocument>(pipeline).ToListAsync();

		return result.Select(x => x["pageId"].AsString).ToArray();
	}

	private static BsonDocument[] BuildEmbeddingsSearchPipeline(IEnumerable<float> vector, int maxRecords)
	{
		return new BsonDocument[]
		{
			BsonDocument.Parse($"{{$search: {{cosmosSearch: {{ vector: [{string.Join(',', vector)}], path: 'vector', k: {maxRecords}}}, returnStoredSource:true}}}}"),
			BsonDocument.Parse($"{{$project: {{\"similarityScore\":{{\"$meta\":\"searchScore\"}}, pageId: 1}}}}"),
		};
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

	private static async Task CreateIndexAsync(IMongoCollection<BsonDocument> collection)
	{
		string vectorIndexName = "vectorSearchIndex";
		using IAsyncCursor<BsonDocument> indexCursor = collection.Indexes.List();
		bool vectorIndexExists = indexCursor.ToList().Any(x => x["name"] == vectorIndexName);
		if (!vectorIndexExists)
		{
			BsonDocumentCommand<BsonDocument> command = new BsonDocumentCommand<BsonDocument>(
			BsonDocument.Parse(@"
								{ createIndexes: 'vectors', 
									indexes: [{ 
									name: 'vectorSearchIndex', 
									key: { vector: 'cosmosSearch' }, 
									cosmosSearchOptions: { kind: 'vector-ivf', numLists: 5, similarity: 'COS', dimensions: 1536 } 
									}] 
								}"
			));

			BsonDocument result = await collection.Database.RunCommandAsync(command);
			if (result["ok"] != 1) throw new Exception("CreateIndex failed with response: " + result.ToJson());
		}
	}
}