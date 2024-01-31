public static class ServiceCollectionHelpers
{
	public static IServiceCollection BindConfiguration<T>(this IServiceCollection services, string sectionName) where T : class
		=> services.AddTransient((context)
			=> context.GetRequiredService<IConfiguration>().GetSection(sectionName).Get<T>()
				?? throw new Exception($"Unable to bind section {sectionName} to {typeof(T)}"));
	public static IServiceCollection BindConfiguration<T>(this IServiceCollection services) where T : class
		=> services.AddTransient((context)
			=> context.GetRequiredService<IConfiguration>().Get<T>()
				?? throw new Exception($"Unable to bind to {typeof(T)}"));

	public static IServiceCollection AddSingletonIfConfigured<T, V>(this IServiceCollection services, Func<V, T> init) where T : class where V : IConfigurable
		=> services.AddSingleton((context) => {
				V config = context.GetRequiredService<V>();
				if (config.IsConfigured) return init(config);
				context.GetService<ILogger>()?.LogWarning("{V} for {T} not configured", typeof(V), typeof(T));
				return null!;
			});
}