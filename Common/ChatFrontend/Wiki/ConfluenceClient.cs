using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

public class ConfluenceClient : IWikiClient
{
	private static readonly Regex s_cleanupRegex = new("<[^>]+>", RegexOptions.Compiled);
	private readonly HttpClient _hc;

	public ConfluenceClient(string confluenceDomain, string confluenceEmail, string confluenceKey)
	{
		_hc = new HttpClient()
		{
			BaseAddress = new Uri(confluenceDomain)
		};
		_hc.DefaultRequestHeaders.Authorization = HttpClientHelpers.BuildBasicAuthHeader(confluenceEmail, confluenceKey);
	}
	public async Task<string[]> SearchAsync(string question, int limit = 5)
	{
		var resp = await _hc.GetFromJsonAsync<JsonNode>($"/wiki/rest/api/content/search?cql=text~\"{HttpUtility.UrlEncode(question.Replace("\"", ""))}\"&limit={limit}");
		var ids = resp?["results"]?.AsArray().Select(x => (string?)x?["id"] ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray();
		if (ids == null) throw new Exception();
		return ids;
	}
	public async Task<WikiPage> GetContentAsync(string pageId)
	{
		var resp = await _hc.GetFromJsonAsync<JsonNode>($"/wiki/api/v2/pages/{pageId}?body-format=export_view");
		string? rawContent = (string?)resp?["body"]?["export_view"]?["value"];
		if (string.IsNullOrWhiteSpace(rawContent)) throw new Exception();

		return new WikiPage((string?)resp?["title"] ?? "", s_cleanupRegex.Replace(rawContent, ""), _hc.BaseAddress + "wiki/" + ((string?)resp?["_links"]?["webui"] ?? "").TrimStart('/'));
	}
	public async Task<string[]> ListPagesAsync()
	{
		var resp = await _hc.GetFromJsonAsync<JsonNode>($"/wiki/api/v2/pages?limit=100"); // TODO: Add pagination
		var ids = resp?["results"]?.AsArray().Select(x => (string?)x?["id"] ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray();
		if (ids == null) throw new Exception();
		return ids;
	}
}

public record WikiPage(string Title, string Content, string Url);