namespace WikiAI;

public static class WikiClientExtensions
{
	public static async Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(this IWikiClient wikiClient)
	{
		var pages = await wikiClient.ListPagesAsync();
		var pageContent = new List<WikiPage>();
		foreach (var pageId in pages)
		{
			try
			{
				pageContent.Add(await wikiClient.GetContentAsync(pageId));
			}
			catch { }
		}
		return pageContent;
	}
}
