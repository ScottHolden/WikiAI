namespace WikiAI;

public class VectorSearchWikiStrategy : IStrategy
{
	private readonly VectorSearch _vectorSearch;
	private readonly IWikiClient _wikiClient;
	public VectorSearchWikiStrategy(IWikiClient wikiClient, VectorSearch vectorSearch)
	{
		_wikiClient = wikiClient;
		_vectorSearch = vectorSearch;
	}
	public async Task<StrategyResponse> GetResponseAsync(string question)
	{
		// Look up the pages we want to use
		var pages = await _vectorSearch.SearchAsync(question);

		// Format response
		int maxLength = 10000 * 4; //(10k tokens)

		Dictionary<string, SourceReference> sources = new();
		int length = 0;
		foreach (var page in pages)
		{
			var pageContent = await _wikiClient.GetContentAsync(page);
			if (length + pageContent.Content.Length > maxLength)
			{
				if (sources.Count < 1)
				{
					// Add partial match as fallback
					sources.Add(page, new SourceReference(pageContent.Content.Substring(0, maxLength - length), pageContent.Url, pageContent.Title));
				}
				break;
			}
			sources.Add(page, new SourceReference(pageContent.Content, pageContent.Url, pageContent.Title));
		}

		return new StrategyResponse(sources, "Answered using Vector Search + Wiki", null);
	}
}
