using System.Text.RegularExpressions;

public class WikiCopilot
{
	private readonly IWikiClient _wikiClient;
	private readonly AzureOpenAIChatCompletion _chatCompletion;
	private readonly VectorSearch _vectorSearch;

	private const string WikiQuestionPrompt = """
		You are an intelligent assistant helping developers with questions about their knowledgebase.
		Use 'you' to refer to the individual asking the questions even if they ask with 'I'.
		Answer the following question using only the data provided in the sources below.
		Each source has a name followed by colon and the actual information, always include the source name for each fact you use in the response.
		Use square brackets to reference the source, for example [1234]. Don't combine sources, list each source separately, for example [1234][73456].
		If you cannot answer using the sources below, say you don't know.
		""";

	public WikiCopilot(IWikiClient wikiClient, AzureOpenAIChatCompletion chatCompletion, VectorSearch vectorSearch)
	{
		_wikiClient = wikiClient;
		_chatCompletion = chatCompletion;
		_vectorSearch = vectorSearch;
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

		return await GetFormattedResponseAsync(sources, question, "Answered using Direct Wiki Search");
	}

	public async Task<AnswerResponse> AskWithVectorSearchAsync(string question)
	{
		// Look up the pages we want to use
		var pages = await _vectorSearch.SearchAsync(question);
		var sources = await BuildSourcesAsync(pages);

		return await GetFormattedResponseAsync(sources, question, "Answered using Vector Search + Wiki");
	}

	public async Task<AnswerResponse> AskWithAzureAISearchAsync(string question)
	{
		throw new NotImplementedException();
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

	private async Task<AnswerResponse> GetFormattedResponseAsync(Dictionary<string, SourceReference> sources, string question, string notes)
	{
		// Ask Azure OpenAI for a response
		string response = await _chatCompletion.GetChatCompletionAsync(WikiQuestionPrompt, sources.ToDictionary(x => x.Key, x => x.Value.Content), question);

		// Include links to any pages references
		var refNumber = s_referenceFind.Matches(response).Select(x => x.Groups[1].Value).Distinct().Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i + 1);

		string finalResponse = s_referenceFind.Replace(response, x => $"[{refNumber[x.Groups[1].Value]}]");

		return new AnswerResponse(finalResponse, refNumber.ToDictionary(x => x.Value.ToString(), x => new AnswerReference(sources[x.Key].Title, sources[x.Key].Url)), notes);
	}
	private static readonly Regex s_referenceFind = new("\\[([^\\]]+)\\]");
	private record SourceReference(string Content, string Url, string Title);
}

public record AnswerResponse(string answer, Dictionary<string, AnswerReference> references, string notes);
public record AnswerReference(string title, string url);