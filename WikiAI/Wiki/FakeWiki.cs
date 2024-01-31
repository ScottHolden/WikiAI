
using System.Text.RegularExpressions;

public class FakeWiki : IWikiClient
{
    private const string s_fakeWikiFolder = "fakewiki";
    private static readonly Regex s_cleanupRegex = new("<[^>]+>", RegexOptions.Compiled);
    private readonly Dictionary<string, WikiPage> _wikiPages = new();
    public FakeWiki(IWebHostEnvironment hostEnvironment)
    {
        var folder = Path.Combine(hostEnvironment.WebRootPath, s_fakeWikiFolder);
        foreach(var file in Directory.GetFiles(folder, "*.html"))
        {
            string rawContent = File.ReadAllText(file);
            string id = Path.GetFileNameWithoutExtension(file);
            string url = $"/{s_fakeWikiFolder}/{Path.GetFileName(file)}";
            var page = new WikiPage(id, id, s_cleanupRegex.Replace(rawContent, ""),  url);
            _wikiPages.Add(id, page);
        }
    }
    public Task<WikiPage> GetContentAsync(string pageId)
        => Task.FromResult(_wikiPages[pageId]);

    public Task<string[]> ListPagesAsync()
        => Task.FromResult(_wikiPages.Keys.ToArray());

    public Task<string[]> SearchAsync(string question, int limit = 5)
        => Task.FromResult(Array.Empty<string>());
}
