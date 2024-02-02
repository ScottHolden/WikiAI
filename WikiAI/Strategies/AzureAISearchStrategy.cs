using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

namespace WikiAI;

public class AzureAISearchStrategy(
	AISearchConfig _config,
	AzureOpenAIChatCompletion _azureOpenAI,
	ILogger<AzureAISearchStrategy> _logger
) : IStrategy, IIndexer
{
	private const string VectorAlgorithmName = "vector-hnsw-400";
	private const string VectorProfileName = "vector-default";
	private const string SemanticSearchName = "default";
	private const int MaxSourcesLength = 10000 * 4;

	private readonly SearchIndexClient _searchIndexClient = new(
		new Uri(_config.AISEARCH_ENDPOINT),
		new AzureKeyCredential(_config.AISEARCH_KEY)
	);
	private readonly SearchClient _searchClient = new(
		new Uri(_config.AISEARCH_ENDPOINT),
		_config.AISEARCH_INDEX,
		new AzureKeyCredential(_config.AISEARCH_KEY)
	);
	public string Name => "aiSearch";
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
		var searchResults = await SearchAsync(question);

		Dictionary<string, SourceReference> sources = [];
		int length = 0;
		foreach (var page in searchResults)
		{
			if (length + page.content.Length > MaxSourcesLength)
			{
				if (sources.Count < 1)
				{
					// Add partial match as fallback
					sources.Add(page.pageId, new SourceReference(page.content[..(MaxSourcesLength - length)], page.pageUrl, page.pageTitle));
				}
				break;
			}
			sources.Add(page.pageId, new SourceReference(page.content, page.pageUrl, page.pageTitle));
		}

		return new StrategyResponse(sources, "Answered using Azure AI Search", null);
	}

	private async Task<IEnumerable<AzureAISearchResult>> SearchAsync(string question, int limit = 5)
		=> await SearchAsync(question, await _azureOpenAI.GetEmbeddingAsync(question), limit);

	private async Task<IEnumerable<AzureAISearchResult>> SearchAsync(string question, float[] vector, int limit = 5)
	{
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
							nameof(AzureAISearchInsert.embedding)
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
		return await GetTopResultDocuments(result, limit);
	}

	private async Task<IEnumerable<AzureAISearchResult>> GetTopResultDocuments(SearchResults<AzureAISearchResult> results, int limit)
	{
		List<AzureAISearchResult> output = [];
		await foreach (var item in results.GetResultsAsync())
		{
			output.Add(item.Document);
			if (output.Count >= limit) break;
		}
		return output;
	}

	private async Task UpsertChunksAsync(IReadOnlyList<VectorChunk> chunks)
	{
		var indexBatch = IndexDocumentsBatch.Upload(
			chunks.Select(x => new AzureAISearchInsert(x.PageId, x.PageId, x.ChunkContent, x.Title, x.Url, x.Vector))
		);

		var result = await _searchClient.IndexDocumentsAsync(indexBatch);
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
}

public record AzureAISearchResult(string pageId, string content, string pageTitle, string pageUrl);
public record AzureAISearchInsert(string id, string pageId, string content, string pageTitle, string pageUrl, float[] embedding);