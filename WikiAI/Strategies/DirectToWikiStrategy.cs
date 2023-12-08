namespace WikiAI;

public class DirectToWikiStrategy : IStrategy
{
	private readonly AzureOpenAIChatCompletion _chatCompletion;
	private readonly IWikiClient _wikiClient;
	public DirectToWikiStrategy(IWikiClient wikiClient, AzureOpenAIChatCompletion chatCompletion)
	{
		_wikiClient = wikiClient;
		_chatCompletion = chatCompletion;
	}
	public async Task<StrategyResponse> GetResponseAsync(string question)
	{
		// Format our question to search on, only need to do this for Direct to Confluence
		var searchTerm = await _chatCompletion.GetChatCompletionAsync("""
			Convert the following question into a search query that could be used to find relevant documents. 
			Return the search terms and nothing else.
			Do not include any special characters like '+'.
			If you cannot generate a search query, return just the number 0.
			""", question);

		// Search and format responses
		var pages = await _wikiClient.SearchAsync(searchTerm);

		// Format Response
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

		return new StrategyResponse(sources, "Answered using Direct Wiki Search", searchTerm);
	}
}
