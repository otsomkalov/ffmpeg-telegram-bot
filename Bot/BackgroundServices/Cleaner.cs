using Microsoft.Extensions.Options;
using File = System.IO.File;

namespace Bot.BackgroundServices;

public class Cleaner : BackgroundService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ServicesSettings _servicesSettings;
    private readonly ILogger<Cleaner> _logger;

    public Cleaner(IAmazonSQS sqsClient, IOptions<ServicesSettings> servicesSettings, ILogger<Cleaner> logger)
    {
        _sqsClient = sqsClient;
        _logger = logger;
        _servicesSettings = servicesSettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during Cleaner execution:");
            }
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var response = await _sqsClient.ReceiveMessageAsync(_servicesSettings.CleanerQueueUrl, stoppingToken);
        var queueMessage = response.Messages.FirstOrDefault();

        if (queueMessage is null)
        {
            return;
        }

        var (inputFilePath, outputFilePath, thumbnailFilePath) = JsonSerializer.Deserialize<CleanerMessage>(queueMessage.Body)!;

        if (File.Exists(inputFilePath))
        {
            File.Delete(inputFilePath);
        }

        if (File.Exists(outputFilePath))
        {
            File.Delete(outputFilePath);
        }

        if (File.Exists(thumbnailFilePath))
        {
            File.Delete(thumbnailFilePath);
        }

        await _sqsClient.DeleteMessageAsync(_servicesSettings.CleanerQueueUrl, queueMessage.ReceiptHandle, stoppingToken);
    }
}