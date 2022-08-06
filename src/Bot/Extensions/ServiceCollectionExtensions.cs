using Bot.BackgroundServices;
using Bot.Constants;
using Polly;
using Polly.Extensions.Http;

namespace Bot.Extensions;

public static class ServiceCollectionExtensions
{
    public static void RegisterHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(Downloader), client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpClientConstants.ChromeUserAgent);
            })
            .AddPolicyHandler(GetRetryPolicy());
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}