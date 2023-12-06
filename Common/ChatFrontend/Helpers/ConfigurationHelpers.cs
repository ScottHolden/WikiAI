static class ConfigurationHelpers
{
	public static string RequiredConfigValue(this IConfiguration config, string name)
	{
		string? value = config[name];
		if (string.IsNullOrWhiteSpace(value)) throw new Exception($"Missing configuration '{name}'");
		return value;
	}
	public static string ConfigValueOrDefault(this IConfiguration config, string name, string defaultValue)
	{
		string? value = config[name];
		if (string.IsNullOrWhiteSpace(value)) return defaultValue;
		return value;
	}
}
