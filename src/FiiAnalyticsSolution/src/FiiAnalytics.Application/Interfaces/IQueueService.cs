namespace FiiAnalytics.Application.Interfaces;

public interface IQueueService
{
    Task PublishEventAsync<T>(string queueUrl, T messageBody) where T : class;
}