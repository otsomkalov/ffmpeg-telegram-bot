namespace Bot.Extensions;

public static class QuartzConfiguratorExtensions
{
    public static IServiceCollectionQuartzConfigurator AddCronJob<TJob>(this IServiceCollectionQuartzConfigurator quartzConfigurator, IConfiguration configuration) where TJob : IJob
    {
        var jobType = typeof(TJob);
        var jobKey = new JobKey(jobType.Name);

        quartzConfigurator.AddJob<TJob>(jobKey);

        quartzConfigurator
            .AddTrigger(triggerConfigurator => triggerConfigurator.ForJob(jobKey)
                .WithIdentity(jobType.Name)
                .WithCronSchedule(configuration[$"{QuartzSettings.SectionName}:{jobType.Name}"]));

        return quartzConfigurator;
    }
}