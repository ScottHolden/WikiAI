namespace WikiAI.Strategies;

public class WikiSourceReferenceBuilder(
	IWikiClient _wikiClient
)
{
	public async Task<Dictionary<string, SourceReference>> BuildSourcesFromPageIdsAsync(string[] pageIds, int maxLength = 10000 * 4)
	{
		Dictionary<string, SourceReference> sources = new();
		foreach (var pageId in pageIds)
		{
			var pageContent = await _wikiClient.GetContentAsync(pageId);
			var currentLength = sources.Values.Sum(x => x.Content.Length);

			// Allow us to add partial pages if we have too much info
			var trimmedContent = LazySubstring(pageContent.Content, maxLength - currentLength);

			sources.Add(pageId, new SourceReference(trimmedContent, pageContent.Url, pageContent.Title));

			if (currentLength + trimmedContent.Length >= maxLength) break;
		}
		return sources;
	}

	private static string LazySubstring(string input, int length)
	{
		return input.Length <= length ? input : input.Substring(0, length);
	}
}