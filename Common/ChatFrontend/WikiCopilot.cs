using System.Text.RegularExpressions;

public class WikiCopilot
{
	private readonly IWikiClient _wikiClient;
	private readonly AzureOpenAIChatCompletion _chatCompletion;
	private readonly VectorSearch _vectorSearch;
	private readonly AzureAISearch _azureAISearch;

	private const string WikiQuestionPrompt = """
		You are an intelligent assistant helping developers with questions contained within a Wiki.
		Use 'you' to refer to the individual asking the questions even if they ask with 'I'.
		Answer the following question using only the data provided in the sources below. ONLY use the data below, do NOT make up answers.
		Each source has a name followed by colon and the actual information, always include the source name for each fact you use in the response.
		Use square brackets to reference the source, for example if you use the "Source1:" source, reference it as [Source1]. Don't combine sources, list each source separately, for example [Source1][Source2].
		Answer in a a single paragraph at most including references, keep all answers simple and short.
		If you cannot answer using the sources below, say you are unable to find information within the Wiki.

		Example:
		---
		SourceX: "Azure is Microsoft's cloud"
		SourceY: "Webapps can be used to host websites"
		SourceZ: "Azure storage can be used to store blobs"
		Question: "What should I host a website on?"
		Answer: "You can host websites on Webapps[SourceX]"
		---
		""";

	public WikiCopilot(IWikiClient wikiClient, AzureOpenAIChatCompletion chatCompletion, VectorSearch vectorSearch, AzureAISearch azureAISearch)
	{
		_wikiClient = wikiClient;
		_chatCompletion = chatCompletion;
		_vectorSearch = vectorSearch;
		_azureAISearch = azureAISearch;
	}

	public async Task<AnswerResponse> AskDirectToWikiAsync(string question)
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
		var sources = await BuildSourcesAsync(pages);

		return await GetFormattedResponseAsync(sources, question, "Answered using Direct Wiki Search", searchTerm);
	}

	public async Task<AnswerResponse> AskWithVectorSearchAsync(string question)
	{
		// Look up the pages we want to use
		var pages = await _vectorSearch.SearchAsync(question);
		var sources = await BuildSourcesAsync(pages);

		return await GetFormattedResponseAsync(sources, question, "Answered using Vector Search + Wiki", "");
	}

	public async Task<AnswerResponse> AskWithAzureAISearchAsync(string question)
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

		return await GetFormattedResponseAsync(sources, question, "Answered using Azure AI Search", "");
	}

	private async Task<Dictionary<string, SourceReference>> BuildSourcesAsync(string[] pages)
	{
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
		return sources;
	}

	private async Task<AnswerResponse> GetFormattedResponseAsync(Dictionary<string, SourceReference> sources, string question, string notes, string? searchQuery)
	{
		// If we don't have any references, we can't answer!
		var empty = new Dictionary<string, AnswerReference>();
		var emptyArray = Array.Empty<AnswerReference>();
		if (sources.Count < 1)
		{
			var respMessage = $"Could not find any references in the wiki related to \"{question}\".";
			if (!string.IsNullOrWhiteSpace(searchQuery))
			{
				respMessage += $" Searched for \"{searchQuery}\"";
			}
			return new AnswerResponse(respMessage, empty, notes, emptyArray, searchQuery);
		}

		// Ask Azure OpenAI for a response
		string response = await _chatCompletion.GetChatCompletionAsync(WikiQuestionPrompt, sources.ToDictionary(x => "Source" + x.Key, x => x.Value.Content), question + "\nOnly use sources provided and reference all sources.");

		// Include links to any pages references
		var refNumber = s_referenceFind.Matches(response)
										.Select(x => x.Groups[1].Value)
										.Distinct()
										.Where(sources.ContainsKey)
										.Select((x, i) => (x, i))
										.ToDictionary(x => x.x, x => x.i + 1);

		var refs = refNumber
					.Where(x => sources.ContainsKey(x.Key))
					.ToDictionary(x => x.Value.ToString(), x => new AnswerReference(x.Key, sources[x.Key].Title, sources[x.Key].Url));

		string finalResponse = s_referenceFind.Replace(response, x => refNumber.ContainsKey(x.Groups[1].Value) ? $"[{refNumber[x.Groups[1].Value]}]" : "[Unknown]");

		var allRefs = sources.Select(x => new AnswerReference(x.Key, x.Value.Title, x.Value.Url)).ToArray();

		return new AnswerResponse(finalResponse, refs, notes, allRefs, searchQuery);
	}
	private static readonly Regex s_referenceFind = new("\\[Source([^\\]]+)\\]");
	private record SourceReference(string Content, string Url, string Title);
}

public record AnswerResponse(string answer, Dictionary<string, AnswerReference> references, string notes, AnswerReference[] allReferences, string? searchQuery);
public record AnswerReference(string pageId, string title, string url);
