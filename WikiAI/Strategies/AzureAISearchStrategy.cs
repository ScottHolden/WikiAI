namespace WikiAI;

public class AzureAISearchStrategy : IStrategy
{
	private readonly AzureAISearch _azureAISearch;
	public AzureAISearchStrategy(AzureAISearch azureAISearch)
	{
		_azureAISearch = azureAISearch;
	}
	public async Task<StrategyResponse> GetResponseAsync(string question)
	{
		var searchResults = await _azureAISearch.SearchAsync(question);

		int maxLength = 10000 * 4; //(10k tokens)

		Dictionary<string, SourceReference> sources = new();
		int length = 0;
		foreach (var page in searchResults)
		{
			if (length + page.content.Length > maxLength)
			{
				if (sources.Count < 1)
				{
					// Add partial match as fallback
					sources.Add(page.pageId, new SourceReference(page.content.Substring(0, maxLength - length), page.pageUrl, page.pageTitle));
				}
				break;
			}
			sources.Add(page.pageId, new SourceReference(page.content, page.pageUrl, page.pageTitle));
		}

		return new StrategyResponse(sources, "Answered using Azure AI Search", null);
	}
}
