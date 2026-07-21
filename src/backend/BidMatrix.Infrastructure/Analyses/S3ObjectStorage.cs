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
}
