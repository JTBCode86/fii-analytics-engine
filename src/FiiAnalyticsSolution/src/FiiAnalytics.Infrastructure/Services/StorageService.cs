using Amazon.S3;
using Amazon.S3.Model;
using FiiAnalytics.Application.Interfaces;

namespace FiiAnalytics.Infrastructure.Services;

public class StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;

    public StorageService(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task<string> UploadFileAsync(string bucketName, string fileName, Stream fileStream)
    {
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = fileName,
            InputStream = fileStream,
            AutoCloseStream = true
        };

        await _s3Client.PutObjectAsync(putRequest);
        return fileName;
    }
}