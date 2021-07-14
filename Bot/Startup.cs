using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Bot.Extensions;
using Bot.Jobs;
using Bot.Services;
using Bot.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quartz;
using Telegram.Bot;

namespace Bot
{
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
                    var telegramSettings = provider.GetService<IOptions<TelegramSettings>>().Value;

                    return new TelegramBotClient(telegramSettings.Token);
                })
                .AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(new EnvironmentVariablesAWSCredentials(), RegionEndpoint.EUCentral1))
                .AddSingleton<FFMpegService>()
                .AddSingleton<MessageService>();

            services.Configure<ServicesSettings>(_configuration.GetSection(ServicesSettings.SectionName))
                .Configure<TelegramSettings>(_configuration.GetSection(TelegramSettings.SectionName))
                .Configure<FFMpegSettings>(_configuration.GetSection(FFMpegSettings.SectionName));

            services.AddQuartz(q =>
            {
                // base quartz scheduler, job and trigger configuration
                q.UseMicrosoftDependencyInjectionScopedJobFactory();

                q.AddCronJob<DownloaderJob>(_configuration)
                    .AddCronJob<ConverterJob>(_configuration)
                    .AddCronJob<UploaderJob>(_configuration)
                    .AddCronJob<CleanerJob>(_configuration);
            });

            // ASP.NET Core hosting
            services.AddQuartzServer(options =>
            {
                // when shutting down we want jobs to complete gracefully
                options.WaitForJobsToComplete = true;
            });

            services.AddHealthChecks();

            services.AddApplicationInsightsTelemetry();

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

            app.UseStaticFiles("/telegram-bot-api-data");

            app.UseRouting();

            app.UseHealthChecks("/health");

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}