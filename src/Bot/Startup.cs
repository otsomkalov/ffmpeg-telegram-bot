using Amazon;
using Amazon.Runtime;
using Bot.BackgroundServices;
using Bot.Constants;
using Microsoft.Extensions.Options;

namespace Bot;

public class Startup
{
    private readonly IConfiguration _configuration;

    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITelegramBotClient>(provider =>
            {
                var settings = provider.GetRequiredService<IOptions<TelegramSettings>>().Value;

                return new TelegramBotClient(settings.Token, baseUrl: settings.ApiUrl);
            })
            .AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(new EnvironmentVariablesAWSCredentials(), RegionEndpoint.EUCentral1))
            .AddSingleton<FFMpegService>()
            .AddSingleton<MessageService>();

        services.AddHostedService<Cleaner>()
            .AddHostedService<Converter>()
            .AddHostedService<Downloader>()
            .AddHostedService<Uploader>();

        services.Configure<ServicesSettings>(_configuration.GetSection(ServicesSettings.SectionName))
            .Configure<TelegramSettings>(_configuration.GetSection(TelegramSettings.SectionName))
            .Configure<FFMpegSettings>(_configuration.GetSection(FFMpegSettings.SectionName));

        services.AddHttpClient<Downloader>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpClientConstants.ChromeUserAgent);
        });

        services.AddApplicationInsightsTelemetry()
            .AddApplicationInsightsTelemetryWorkerService();

        services.AddControllers()
            .AddNewtonsoftJson();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }
}