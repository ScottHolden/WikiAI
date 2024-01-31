using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

namespace WikiAI;
public class AzureAISearch
{
	private readonly SearchIndexClient _searchIndexClient;
	private readonly SearchClient _searchClient;
	private readonly AzureOpenAIChatCompletion _azureOpenAI;

	private const string VectorAlgorithmName = "vector-hnsw-400";
	private const string VectorProfileName = "vector-default";
	private const string SemanticSearchName = "default";

	public AzureAISearch(
		SearchIndexClient searchIndexClient,
		SearchClient searchClient,
		AzureOpenAIChatCompletion azureOpenAI)
	{
		_searchIndexClient = searchIndexClient;
		_searchClient = searchClient;
		_azureOpenAI = azureOpenAI;
	}

	public async Task<List<string>> BuildIndexAsync(IReadOnlyList<WikiPage> pages)
	{
		await EnsureIndexExistsAsync();

		List<string> results = new();
		List<AzureAISearchInsert> pagesToInsert = new();

		foreach (var page in pages)
		{
			try
			{
				var textToVector = $"{page.Title}\n{page.Content}".Trim();
				// TODO: Break it up

				float[] vector = await _azureOpenAI.GetEmbeddingAsync(textToVector);
				pagesToInsert.Add(new AzureAISearchInsert(page.PageId, page.PageId, page.Content, page.Title, page.Url, vector));
			}
			catch { results.Add($"Skipping {page.PageId}, error building vector"); }
		}

		var indexBatch = IndexDocumentsBatch.Upload(pagesToInsert);

		var result = await _searchClient.IndexDocumentsAsync(indexBatch);

		results.AddRange(result.Value.Results.Select(x => $"{x.Key}: {x.Status} {x.ErrorMessage}"));
		results.Add("Done!");
		return results;
	}

	private async Task EnsureIndexExistsAsync()
	{
		string idFieldName = nameof(AzureAISearchInsert.id);
		string pageIdFieldName = nameof(AzureAISearchInsert.pageId);
		string pageTitleFieldName = nameof(AzureAISearchInsert.pageTitle);
		string pageUrlFieldName = nameof(AzureAISearchInsert.pageUrl);
		string contentFieldName = nameof(AzureAISearchInsert.content);
		string embeddingFieldName = nameof(AzureAISearchInsert.embedding);

		var searchIndex = new SearchIndex(_searchClient.IndexName, new SearchField[] {
			new SearchField(idFieldName, SearchFieldDataType.String) {
				IsKey = true,
				IsHidden = false,
			},
			new SearchField(pageIdFieldName, SearchFieldDataType.String) {
				IsHidden = false,
			},
			new SearchField(pageTitleFieldName, SearchFieldDataType.String) {
				IsHidden = false,
				IsSearchable = true,
				AnalyzerName = LexicalAnalyzerName.EnMicrosoft
			},
			new SearchField(pageUrlFieldName, SearchFieldDataType.String) {
				IsHidden = false,
			},
			new SearchField(contentFieldName, SearchFieldDataType.String) {
				IsHidden = false,
				IsSearchable = true,
				AnalyzerName = LexicalAnalyzerName.EnMicrosoft
			},
			new VectorSearchField(embeddingFieldName, 1536, VectorProfileName)
		})
		{
			VectorSearch = new()
			{
				Algorithms = {
					new HnswAlgorithmConfiguration(VectorAlgorithmName){
						Parameters = new()
						{
							Metric = VectorSearchAlgorithmMetric.Cosine,
							M = 4,
							EfConstruction = 400,
							EfSearch = 500
						}
					}
				},
				Profiles = {
					new VectorSearchProfile(VectorProfileName, VectorAlgorithmName)
				}
			},
			SemanticSearch = new()
			{
				DefaultConfigurationName = SemanticSearchName,
				Configurations = {
					new SemanticConfiguration(SemanticSearchName, new SemanticPrioritizedFields{
						TitleField = new SemanticField(pageTitleFieldName),
						ContentFields = {
							new SemanticField(contentFieldName)
						},
						KeywordsFields = {
							new SemanticField(pageUrlFieldName)
						}
					})
				}
			}
		};

		await _searchIndexClient.CreateOrUpdateIndexAsync(searchIndex, allowIndexDowntime: false);
	}
	public async Task<AzureAISearchResult[]> SearchAsync(string question, int limit = 5)
	{
		string embeddingFieldName = nameof(AzureAISearchInsert.embedding);

		var vector = await _azureOpenAI.GetEmbeddingAsync(question);

		// TODO: Add highlight based responses
		var searchOptions = new SearchOptions()
		{
			QueryType = SearchQueryType.Semantic,
			VectorSearch = new VectorSearchOptions
			{
				Queries = {
					new VectorizedQuery(vector)
					{
						KNearestNeighborsCount = limit,
						Fields = {
							embeddingFieldName
						},
					}
				}
			},
			SemanticSearch = new SemanticSearchOptions
			{
				SemanticConfigurationName = SemanticSearchName,
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
			results.Add(item.Document);
			if (results.Count >= limit) break;
		}

		return results.ToArray();
	}
}
