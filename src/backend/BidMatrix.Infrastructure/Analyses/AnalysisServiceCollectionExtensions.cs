using Amazon.Runtime;
using Amazon.S3;
using BidMatrix.Application.Analyses;
using BidMatrix.Application.Audit;
using BidMatrix.Application.Workflows;
using BidMatrix.Infrastructure.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BidMatrix.Infrastructure.Analyses;

public static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddBidMatrixAnalysis(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var analysisOptions = new AnalysisOptions
        {
            MaxPdfUploadBytes = long.TryParse(configuration["MAX_PDF_UPLOAD_BYTES"], out var maxBytes)
                ? maxBytes
                : AnalysisOptions.DefaultMaxPdfUploadBytes,
            FileRetentionDays = int.TryParse(configuration["FILE_RETENTION_DAYS"], out var retentionDays)
                ? retentionDays
                : 30,
            ScanMode = configuration["FILE_SCAN_MODE"] ?? (environment.IsDevelopment() ? "development_bypass" : "disabled"),
        };
        analysisOptions.Validate(environment.IsDevelopment());

        var storageOptions = new S3StorageOptions
        {
            Endpoint = configuration["S3_ENDPOINT"] ?? string.Empty,
            Region = configuration["S3_REGION"] ?? "us-east-1",
            QuarantineBucket = configuration["S3_BUCKET_QUARANTINE"] ?? string.Empty,
            PrivateBucket = configuration["S3_BUCKET_PRIVATE"] ?? string.Empty,
            AccessKey = configuration["S3_ACCESS_KEY"] ?? string.Empty,
            SecretKey = configuration["S3_SECRET_KEY"] ?? string.Empty,
        };
        storageOptions.Validate();

        services.AddSingleton(analysisOptions);
        services.AddSingleton(storageOptions);
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(storageOptions.AccessKey, storageOptions.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = storageOptions.Endpoint,
                AuthenticationRegion = storageOptions.Region,
                ForcePathStyle = true,
            }));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddSingleton<IFileScanner, ConfiguredFileScanner>();
        services.AddScoped<IAnalysisService, PostgresAnalysisService>();
        services.AddScoped<IWorkflowBridgeService, PostgresWorkflowBridgeService>();
        services.AddScoped<IAuditWriter, PostgresAuditWriter>();

        return services;
    }
}
