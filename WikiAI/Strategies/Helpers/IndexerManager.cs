namespace WikiAI;

public class IndexerManager(
	IWikiClient _wikiClient,
	IVectorChunker _chunker,
	IEnumerable<IIndexer> _indexers,
	ILogger<IndexerManager> _logger
)
{
	public async Task<int> InitAsync()
	{
		if (_indexers == null || !_indexers.Any()) return 0;

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


public static class IsConfiguredEnumerableHelper
{
	// TODO: Think if this should be singleton!
	public static IServiceCollection AddConfiguredEnumerableItems<T>(this IServiceCollection service, params IEnumerableItemResolver<T>[] items)
		=> service.AddSingleton(new IsConfiguredEnumerable<T>(items).Resolve);
}
public class IsConfiguredEnumerable<T>(IEnumerable<IEnumerableItemResolver<T>> _items)
{
	public IEnumerable<T> Resolve(IServiceProvider services)
		=> _items.Where(x => x.IsConfigured(services))
					.Select(x => x.Resolve(services));
}
public class IsConfiguredEnumerableItem<T, V, I> :
	IEnumerableItemResolver<I>
	where T : IConfigurable where V : class, I
{
	public bool IsConfigured(IServiceProvider services)
		=> services.GetRequiredService<T>().IsConfigured;
	public I Resolve(IServiceProvider services)
		=> services.GetRequiredService<V>();
}
public class AlwaysEnumerableItem<T, V> :
	IEnumerableItemResolver<V>
	where T : class, V
{
	public bool IsConfigured(IServiceProvider services) => true;
	public V Resolve(IServiceProvider services)
		=> services.GetRequiredService<T>();
}
public interface IEnumerableItemResolver<T>
{
	bool IsConfigured(IServiceProvider services);
	T Resolve(IServiceProvider services);
}