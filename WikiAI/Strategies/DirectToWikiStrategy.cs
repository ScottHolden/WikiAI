using WikiAI.Strategies;

namespace WikiAI;

public class DirectToWikiStrategy(
    IWikiClient _wikiClient,
    WikiSourceReferenceBuilder _referenceBuilder,
    AzureOpenAIChatCompletion _chatCompletion
) : IStrategy
{
    private const int MaxSearchResults = 5;
    public string Name => "wiki";
    public string DisplayName => "Wiki Search";
    public async Task<StrategyResponse> GetResponseAsync(string question)
    {
        // Format our question to search on, only need to do this for Direct to Confluence
        var searchTerm = await FormatQuestionAsSearchTermAsync(question);

        // Search and format responses
        var pageIds = await _wikiClient.SearchAsync(searchTerm, MaxSearchResults);
        var sources = await _referenceBuilder.BuildSourcesFromPageIdsAsync(pageIds);

        return new StrategyResponse(sources, "Answered using Direct Wiki Search", searchTerm);
    }

    private async Task<string> FormatQuestionAsSearchTermAsync(string question)
    {
        return await _chatCompletion.GetChatCompletionAsync("""
			Convert the following question into a search query that could be used to find relevant documents. 
			Return the search terms and nothing else.
			Do not include any special characters like '+'.
			If you cannot generate a search query, return just the number 0.
			""", question);
    }
}
