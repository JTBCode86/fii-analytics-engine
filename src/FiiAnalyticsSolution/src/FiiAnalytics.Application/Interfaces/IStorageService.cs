namespace FiiAnalytics.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadFileAsync(string bucketName, string fileName, Stream fileStream);
}