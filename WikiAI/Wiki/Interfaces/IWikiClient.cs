namespace WikiAI;
public interface IWikiClient
{
	Task<string[]> SearchAsync(string question, int limit = 5);
	Task<WikiPage> GetContentAsync(string pageId);
	Task<string[]> ListPagesAsync();
}
