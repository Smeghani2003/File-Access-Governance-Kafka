using Microsoft.Data.SqlClient;

namespace FileAccessGovernance.IngestionConsumer.Sql;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct);
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("FileAccessGovernance")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:FileAccessGovernance.");

    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
