using BidMatrix.Database.Schema;
using Npgsql;

namespace BidMatrix.Api.IntegrationTests;

public sealed class DatabaseConfigurationTests
{
    [Fact]
    public void ConnectionStringsSeparateRuntimeAndMigrationHosts()
    {
        var options = CreateOptions(
            host: "ep-example-pooler.eu-central-1.aws.neon.tech",
            migrationHost: "ep-example.eu-central-1.aws.neon.tech",
            sslMode: SslMode.VerifyFull,
            channelBinding: ChannelBinding.Require);

        var application = new NpgsqlConnectionStringBuilder(options.BuildApplicationConnectionString());
        var migration = new NpgsqlConnectionStringBuilder(options.BuildMigrationConnectionString());

        Assert.Equal("ep-example-pooler.eu-central-1.aws.neon.tech", application.Host);
        Assert.Equal("ep-example.eu-central-1.aws.neon.tech", migration.Host);
        Assert.Equal(SslMode.VerifyFull, application.SslMode);
        Assert.Equal(ChannelBinding.Require, application.ChannelBinding);
        Assert.Equal(SslMode.VerifyFull, migration.SslMode);
        Assert.Equal(ChannelBinding.Require, migration.ChannelBinding);
    }

    [Fact]
    public void RequiredChannelBindingRejectsDisabledTls()
    {
        var options = CreateOptions(
            sslMode: SslMode.Disable,
            channelBinding: ChannelBinding.Require);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Equal("Required PostgreSQL channel binding requires TLS.", exception.Message);
    }

    private static BidMatrixDataSourceOptions CreateOptions(
        string host = "localhost",
        string migrationHost = "",
        SslMode sslMode = SslMode.Prefer,
        ChannelBinding channelBinding = ChannelBinding.Prefer) => new()
        {
            Host = host,
            MigrationHost = migrationHost,
            Database = "bidmatrix",
            User = "bidmatrix_app",
            Password = "application-password",
            AuditUser = "bidmatrix_audit",
            AuditPassword = "audit-password",
            AuthUser = "bidmatrix_auth",
            AuthPassword = "auth-password",
            MigrationUser = "bidmatrix_admin",
            MigrationPassword = "migration-password",
            SslMode = sslMode,
            ChannelBinding = channelBinding,
        };
}
