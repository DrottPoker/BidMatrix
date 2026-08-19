using Npgsql;

namespace BidMatrix.Database.Schema;

public sealed class BidMatrixDataSourceOptions
{
    public string Host { get; init; } = "localhost";
    public string MigrationHost { get; init; } = string.Empty;
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "bidmatrix";
    public string User { get; init; } = "bidmatrix_app";
    public string Password { get; init; } = string.Empty;
    public string AuditUser { get; init; } = "bidmatrix_audit";
    public string AuditPassword { get; init; } = string.Empty;
    public string AuthUser { get; init; } = "bidmatrix_auth";
    public string AuthPassword { get; init; } = string.Empty;
    public string MigrationUser { get; init; } = "bidmatrix_admin";
    public string MigrationPassword { get; init; } = string.Empty;
    public SslMode SslMode { get; init; } = SslMode.Prefer;
    public ChannelBinding ChannelBinding { get; init; } = ChannelBinding.Prefer;

    public string BuildApplicationConnectionString() => BuildConnectionString(Host, User, Password);

    public string BuildAuditConnectionString() => BuildConnectionString(Host, AuditUser, AuditPassword);

    public string BuildAuthConnectionString() => BuildConnectionString(Host, AuthUser, AuthPassword);

    public string BuildMigrationConnectionString() => BuildConnectionString(
        string.IsNullOrWhiteSpace(MigrationHost) ? Host : MigrationHost,
        MigrationUser,
        MigrationPassword);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(Database);
        ArgumentException.ThrowIfNullOrWhiteSpace(User);
        ArgumentException.ThrowIfNullOrWhiteSpace(Password);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuditUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuditPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuthUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuthPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(MigrationUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(MigrationPassword);

        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("PostgreSQL port must be between 1 and 65535.");
        }

        if (SslMode == SslMode.Disable && ChannelBinding == ChannelBinding.Require)
        {
            throw new InvalidOperationException("Required PostgreSQL channel binding requires TLS.");
        }

        var roles = new[] { User, AuditUser, AuthUser, MigrationUser };
        if (roles.Distinct(StringComparer.Ordinal).Count() != roles.Length)
        {
            throw new InvalidOperationException(
                "PostgreSQL application, audit, auth, and migration roles must be distinct.");
        }
    }

    private string BuildConnectionString(string host, string user, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = Port,
            Database = Database,
            Username = user,
            Password = password,
            SslMode = SslMode,
            ChannelBinding = ChannelBinding,
            IncludeErrorDetail = false,
            Pooling = true,
        };

        return builder.ConnectionString;
    }
}
