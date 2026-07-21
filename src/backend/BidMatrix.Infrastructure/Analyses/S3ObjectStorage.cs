using Amazon.S3;
using Amazon.S3.Model;
using BidMatrix.Application.Analyses;

namespace BidMatrix.Infrastructure.Analyses;

public sealed class S3ObjectStorage(IAmazonS3 client) : IObjectStorage
{
    public async Task PutAsync(ObjectWriteRequest request, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(request.Content.ToArray(), writable: false);
        var putRequest = new PutObjectRequest
        {
            BucketName = request.Bucket,
            Key = request.Key,
            InputStream = stream,
            ContentType = request.ContentType,
            AutoCloseStream = false,
        };
        putRequest.Metadata["sha256"] = request.Sha256;

        await client.PutObjectAsync(putRequest, cancellationToken);
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(
        string bucket,
        string key,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucket,
            Key = key,
        }, cancellationToken);
        await using var content = new MemoryStream();
        await response.ResponseStream.CopyToAsync(content, cancellationToken);
        return content.ToArray();
    }
}
