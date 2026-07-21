namespace BidMatrix.Infrastructure.Analyses;

public sealed class AnalysisOptions
{
    public const long DefaultMaxPdfUploadBytes = 25 * 1024 * 1024;

    public long MaxPdfUploadBytes { get; init; } = DefaultMaxPdfUploadBytes;
    public int FileRetentionDays { get; init; } = 30;
    public string ScanMode { get; init; } = "disabled";

    public void Validate(bool isDevelopment)
    {
        if (MaxPdfUploadBytes is < 1 or > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException("MAX_PDF_UPLOAD_BYTES must be between 1 byte and 100 MiB.");
        }

        if (FileRetentionDays is < 1 or > 3650)
        {
            throw new InvalidOperationException("FILE_RETENTION_DAYS must be between 1 and 3650.");
        }

        if (ScanMode is not ("development_bypass" or "disabled"))
        {
            throw new InvalidOperationException("FILE_SCAN_MODE must be development_bypass or disabled.");
        }

        if (!isDevelopment && ScanMode == "development_bypass")
        {
            throw new InvalidOperationException("Development file-scan bypass is forbidden outside Development.");
        }
    }
}

public sealed class S3StorageOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string Region { get; init; } = "us-east-1";
    public string QuarantineBucket { get; init; } = string.Empty;
    public string PrivateBucket { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;

    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("S3_ENDPOINT must be an absolute URL.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(QuarantineBucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(PrivateBucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(AccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(SecretKey);
    }
}
