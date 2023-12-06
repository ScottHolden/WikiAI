using System.Net.Http.Headers;

class HttpClientHelpers
{
	public static AuthenticationHeaderValue BuildBasicAuthHeader(string username, string password)
	{
		var authenticationString = $"{username}:{password}";
		var base64EncodedAuthenticationString = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(authenticationString));
		return new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
	}
}
