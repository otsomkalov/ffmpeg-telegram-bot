using Microsoft.Extensions.Options;
using File = System.IO.File;

namespace Bot.Jobs;

[DisallowConcurrentExecution]
public class CleanerJob : IJob
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ServicesSettings _servicesSettings;

    public CleanerJob(IOptions<ServicesSettings> servicesSettings, IAmazonSQS sqsClient)
    {
        _sqsClient = sqsClient;
        _servicesSettings = servicesSettings.Value;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var response = await _sqsClient.ReceiveMessageAsync(_servicesSettings.CleanerQueueUrl);
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

        await _sqsClient.DeleteMessageAsync(_servicesSettings.CleanerQueueUrl, queueMessage.ReceiptHandle);
    }
}