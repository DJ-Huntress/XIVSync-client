using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XIVSync.Interop;

public static class UILoggingProviderExtensions
{
	public static ILoggingBuilder AddUILogging(this ILoggingBuilder builder, UILoggingProvider? provider = null)
	{
		if (provider != null)
		{
			builder.AddProvider(provider);
		}
		else
		{
			UILoggingProvider newProvider = new UILoggingProvider();
			builder.Services.AddSingleton(newProvider);
			builder.AddProvider(newProvider);
		}
		return builder;
	}
}
