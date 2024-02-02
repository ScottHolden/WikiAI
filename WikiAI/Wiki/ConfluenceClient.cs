using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

namespace WikiAI;
public class ConfluenceClient : IWikiClient
{
	private static readonly Regex s_cleanupRegex = new("<[^>]+>", RegexOptions.Compiled);
	private readonly HttpClient _hc;

	public ConfluenceClient(ConfluenceClientConfig config)
	{
		_hc = new HttpClient()
		{
			BaseAddress = new Uri(config.CONFLUENCE_DOMAIN)
		};
		_hc.DefaultRequestHeaders.Authorization = BuildBasicAuthHeader(config.CONFLUENCE_EMAIL, config.CONFLUENCE_API_KEY);
	}
	public async Task<string[]> SearchAsync(string question, int limit = 5)
	{
		var resp = await _hc.GetFromJsonAsync<JsonNode>($"/wiki/rest/api/content/search?cql=text~\"{HttpUtility.UrlEncode(question.Replace("\"", ""))}\"&limit={limit}");
		var ids = resp?["results"]?.AsArray().Select(x => (string?)x?["id"] ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray();
		return ids ?? throw new Exception();
	}
	public async Task<WikiPage> GetContentAsync(string pageId)
	{
		var resp = await _hc.GetFromJsonAsync<JsonNode>($"/wiki/api/v2/pages/{pageId}?body-format=export_view");
		string? rawContent = (string?)resp?["body"]?["export_view"]?["value"];
		if (string.IsNullOrWhiteSpace(rawContent)) throw new Exception();

		return new WikiPage(pageId, (string?)resp?["title"] ?? "", s_cleanupRegex.Replace(rawContent, ""), _hc.BaseAddress + "wiki/" + ((string?)resp?["_links"]?["webui"] ?? "").TrimStart('/'));
	}
	public async Task<string[]> ListPagesAsync()
	{
		var resp = await _hc.GetFromJsonAsync<JsonNode>($"/wiki/api/v2/pages?limit=100"); // TODO: Add pagination
		var ids = resp?["results"]?.AsArray().Select(x => (string?)x?["id"] ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray();
		return ids ?? throw new Exception();
	}
	private static AuthenticationHeaderValue BuildBasicAuthHeader(string username, string password)
	{
		var authenticationString = $"{username}:{password}";
		var base64EncodedAuthenticationString = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(authenticationString));
		return new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
	}
}

public record WikiPage(string PageId, string Title, string Content, string Url);