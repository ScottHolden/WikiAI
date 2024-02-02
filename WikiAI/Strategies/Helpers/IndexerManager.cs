namespace WikiAI;

public class IndexerManager(
	IWikiClient _wikiClient,
	IVectorChunker _chunker,
	IIndexer[] _indexers,
	ILogger<IndexerManager> _logger
)
{
	public async Task<int> InitAsync()
	{
		if (_indexers == null || _indexers.Length < 1) return 0;

		var pagesToInit = await _wikiClient.GetAllPagesAsync();
		var chunks = await BuildChunksAsync(pagesToInit);
		var count = 0;
		foreach (var indexer in _indexers)
		{
			if (indexer == null) continue;
			try
			{
				await indexer.BuildDatabaseAsync(chunks);
				count++;
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Unable to init indexer {indexer}: {message}", nameof(indexer), e.Message);
			}
		}

		return count;
	}

	private async Task<List<VectorChunk>> BuildChunksAsync(IReadOnlyList<WikiPage> pages)
	{
		List<VectorChunk> chunks = [];
		foreach (var page in pages)
		{
			var chunk = await _chunker.ChunkAsync(page);
			chunks.Add(chunk);
			// Don't overload the chunker
			await Task.Delay(Random.Shared.Next(200, 500));
		}
		return chunks;
	}
}
