using Npgsql;

namespace BidMatrix.Database.Schema;

public sealed class BidMatrixDataSourceOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "bidmatrix";
    public string User { get; init; } = "bidmatrix_app";
    public string Password { get; init; } = string.Empty;
    public string AuditUser { get; init; } = "bidmatrix_audit";
    public string AuditPassword { get; init; } = string.Empty;
    public string MigrationUser { get; init; } = "bidmatrix_admin";
    public string MigrationPassword { get; init; } = string.Empty;

    public string BuildApplicationConnectionString() => BuildConnectionString(User, Password);

    public string BuildAuditConnectionString() => BuildConnectionString(AuditUser, AuditPassword);

    public string BuildMigrationConnectionString() => BuildConnectionString(MigrationUser, MigrationPassword);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(Database);
        ArgumentException.ThrowIfNullOrWhiteSpace(User);
        ArgumentException.ThrowIfNullOrWhiteSpace(Password);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuditUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuditPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(MigrationUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(MigrationPassword);

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("PostgreSQL port must be between 1 and 65535.");
        }

        if (string.Equals(User, MigrationUser, StringComparison.Ordinal) ||
            string.Equals(AuditUser, MigrationUser, StringComparison.Ordinal) ||
            string.Equals(User, AuditUser, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PostgreSQL application, audit, and migration roles must be distinct.");
        }
    }

    private string BuildConnectionString(string user, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = user,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
        };

        return builder.ConnectionString;
    }
}
