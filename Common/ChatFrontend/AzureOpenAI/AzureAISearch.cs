using Azure.Search.Documents;
using Azure.Search.Documents.Models;

public class AzureAISearch
{
	private readonly SearchClient _searchClient;
	private readonly IWikiClient _wikiClient;
	private readonly AzureOpenAIChatCompletion _azureOpenAI;
	public AzureAISearch(SearchClient searchClient, IWikiClient wikiClient, AzureOpenAIChatCompletion azureOpenAI)
	{
		_searchClient = searchClient;
		_wikiClient = wikiClient;
		_azureOpenAI = azureOpenAI;
	}

	public async Task<List<string>> BuildIndexAsync()
	{
		List<string> results = new List<string>();
		string[] pagesToIndex = await _wikiClient.ListPagesAsync();
		List<AzureAISearchResult> pages = new();
		foreach (string pageId in pagesToIndex)
		{
			try
			{
				var page = await _wikiClient.GetContentAsync(pageId);
				pages.Add(new AzureAISearchResult(pageId, pageId, page.Content, page.Title, page.Url));
			}
			catch { results.Add($"Skipping {pageId}, couldn't get content"); }
		}
		var indexBatch = IndexDocumentsBatch.Upload(pages);
		var result = await _searchClient.IndexDocumentsAsync(indexBatch);
		results.AddRange(result.Value.Results.Select(x => $"{x.Key}: {x.Status} {x.ErrorMessage}"));
		results.Add("Done!");
		return results;
	}
	public async Task<AzureAISearchResult[]> SearchAsync(string question, int limit = 5)
	{
		bool highlights = true;
		var vector = await _azureOpenAI.GetEmbeddingAsync(question);
		var vectorSearchOptions = new VectorSearchOptions();
		var vectorQuery = new VectorizedQuery(vector)
		{
			KNearestNeighborsCount = limit,
		};
		vectorQuery.Fields.Add("embedding");
		vectorSearchOptions.Queries.Add(vectorQuery);
		var searchOptions = new SearchOptions()
		{
			QueryType = SearchQueryType.Semantic,
			VectorSearch = vectorSearchOptions,
			SemanticSearch = new SemanticSearchOptions
			{
				SemanticConfigurationName = "default",
				QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
				{
					HighlightEnabled = false
				},
			},
		};
		var result = await _searchClient.SearchAsync<AzureAISearchResult>(question, searchOptions);
		List<AzureAISearchResult> results = new();
		await foreach (var item in result.Value.GetResultsAsync())
		{
			/*if (highlights && item.Highlights)
			{
				results.AddRange(item.Highlights)
				var r = item.Document with
				{
					content = item
				}
			}*/

			results.Add(item.Document);
			if (results.Count >= limit) break;
		}
		return results.ToArray();
	}
}
public record AzureAISearchResult(string id, string pageId, string content, string pageTitle, string pageUrl);