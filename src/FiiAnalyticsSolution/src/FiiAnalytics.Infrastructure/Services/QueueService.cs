using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FiiAnalytics.Application.Interfaces;

namespace FiiAnalytics.Infrastructure.Services;

public class QueueService : IQueueService
{
    private readonly IAmazonSQS _sqsClient;

    public QueueService(IAmazonSQS sqsClient)
    {
        _sqsClient = sqsClient;
    }

    public async Task PublishEventAsync<T>(string queueUrl, T messageBody) where T : class
    {
        var jsonMessage = JsonSerializer.Serialize(messageBody);

        var sendRequest = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = jsonMessage
        };

        await _sqsClient.SendMessageAsync(sendRequest);
    }
}