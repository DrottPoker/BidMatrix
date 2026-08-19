using Npgsql;

namespace BidMatrix.Infrastructure.Persistence;

internal static class PostgresConcurrencyRetry
{
    internal const int MaxAttempts = 8;

    internal static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Action<int, int, string> onRetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onRetry);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (PostgresException exception) when (
                IsRetryable(exception) && attempt < MaxAttempts)
            {
                var delayMilliseconds = (10 * (1 << (attempt - 1))) + Random.Shared.Next(0, 10);
                onRetry(attempt + 1, delayMilliseconds, exception.SqlState);
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException("The PostgreSQL concurrency retry loop exited unexpectedly.");
    }

    private static bool IsRetryable(PostgresException exception) =>
        exception.SqlState is "40001" or "40P01";
}
