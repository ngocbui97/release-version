using Npgsql;
using ReleasePrepTool.Models;
using System.Diagnostics;

namespace ReleasePrepTool.Services;

public class PostgresService
{
    private readonly DatabaseConfig _config;
    public string PostgresBinPath { get; set; } = "";

    public class JunkRecord
    {
        public string? TableName { get; set; }
        public string? PrimaryKeyColumn { get; set; }
        public string? PrimaryKeyValue { get; set; }
        public string? ColumnName { get; set; }
        public string? DetectedContent { get; set; }
    }

    public PostgresService(DatabaseConfig config)
    {
        _config = config;
    }

    public async Task<List<string>> GetAllDatabasesAsync()
    {
        var tempConfig = new DatabaseConfig
        {
            Host = _config.Host,
            Port = _config.Port,
            Username = _config.Username,
            Password = _config.Password,
            DatabaseName = "postgres" // default db to connect to
        };

        var databases = new List<string>();
        await using var conn = new NpgsqlConnection(tempConfig.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datistemplate = false;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    public async Task<List<string>> GetPrimaryKeysAsync(string dbName, string tableName, string schemaName = "public")
    {
        var pks = new List<string>();
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();

        var sql = @"
            SELECT a.attname
            FROM   pg_index i
            JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE  i.indrelid = CAST(quote_ident($1) || '.' || quote_ident($2) AS regclass) AND i.indisprimary;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        cmd.Parameters.AddWithValue(tableName);
        
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pks.Add(reader.GetString(0));
            }
        }
        catch { }

        return pks;
    }

    public async Task<List<string>> GetSchemaTablesAsync(string dbName, string schemaName)
    {
        var tables = new List<string>();
        // Use connection string for the specific database
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT table_name FROM information_schema.tables WHERE table_schema = $1 AND table_type = 'BASE TABLE' ORDER BY table_name;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public async Task<long> GetTableRowCountAsync(string tableName, string schemaName = "public")
    {
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {schemaName}.\"{tableName}\"", conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public async Task BackupDatabaseAsync(string outputPath)
    {
        await RunPostgresToolAsync("pg_dump", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -F c -f \"{outputPath}\" {_config.DatabaseName}");
    }

    public async Task DumpFullScriptAsync(string outputPath)
    {
        await RunPostgresToolAsync("pg_dump", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -F p -f \"{outputPath}\" {_config.DatabaseName}");
    }

    public async Task CreateDatabaseIfNotExistsAsync(string dbName)
    {
        // Connect to the default 'postgres' db to issue CREATE DATABASE
        var adminConnStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database=postgres";
        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @name", conn);
        checkCmd.Parameters.AddWithValue("name", dbName);
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists == null)
        {
            // CREATE DATABASE cannot run in a transaction
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task RestoreDatabaseAsync(string fileType, string backupPath, string? targetDbName = null, Action<string>? onOutput = null)
    {
        var db = string.IsNullOrWhiteSpace(targetDbName) ? _config.DatabaseName : targetDbName;
        if (!string.IsNullOrWhiteSpace(targetDbName))
        {
            onOutput?.Invoke($"Checking / creating database '{targetDbName}'...");
            await CreateDatabaseIfNotExistsAsync(targetDbName);
            onOutput?.Invoke($"Database '{targetDbName}' ready.");
        }

        if (fileType == ".sql")
        {
            await RunPostgresToolAsync("psql", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {db} -f \"{backupPath}\"", onOutput);
        }
        else
        {
            await RunPostgresToolAsync("pg_restore", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {db} --clean --if-exists -1 --verbose \"{backupPath}\"", onOutput);
        }
    }

    public async Task ExecuteScriptFileAsync(string scriptPath)
    {
        // using psql for script file execution instead of npgsql to properly handle DO blocks and large scripts
        await RunPostgresToolAsync("psql", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {_config.DatabaseName} -f \"{scriptPath}\" -v ON_ERROR_STOP=1 --single-transaction");
    }

    public async Task ExecuteSqlWithTransactionAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn, transaction);
            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteRecordAsync(string tableName, string pkColumn, string pkValue, string schemaName = "public")
    {
        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();
        var sql = $"DELETE FROM {schemaName}.\"{tableName}\" WHERE \"{pkColumn}\" = @pkValue";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pkValue", pkValue);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<JunkRecord>> SearchJunkDataAsync(string searchKeyword, string schemaName = "public")
    {
        var results = new List<JunkRecord>();
        var keyword = $"%{searchKeyword}%";

        await using var conn = new NpgsqlConnection(_config.GetConnectionString());
        await conn.OpenAsync();

        // Get all tables with string or text columns
        var metaSql = @"
            SELECT t.table_name, c.column_name
            FROM information_schema.tables t
            JOIN information_schema.columns c ON t.table_name = c.table_name AND t.table_schema = c.table_schema
            WHERE t.table_schema = @schema AND c.data_type IN ('text', 'character varying', 'character')
            AND t.table_type = 'BASE TABLE';";

        var tableCols = new Dictionary<string, List<string>>();
        await using (var cmd = new NpgsqlCommand(metaSql, conn))
        {
            cmd.Parameters.AddWithValue("schema", schemaName);
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var table = reader.GetString(0);
                    var col = reader.GetString(1);
                    if (!tableCols.ContainsKey(table)) tableCols[table] = new List<string>();
                    tableCols[table].Add(col);
                }
            }
        }

        foreach (var tc in tableCols)
        {
            var table = tc.Key;
            
            // Try to find PK
            var pkSql = @"
                SELECT a.attname
                FROM   pg_index i
                JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                WHERE  i.indrelid = CAST(quote_ident($1) || '.' || quote_ident($2) AS regclass) AND i.indisprimary LIMIT 1;";
            
            string pkColumn = null;
            await using (var pkCmd = new NpgsqlCommand(pkSql, conn))
            {
                pkCmd.Parameters.AddWithValue(schemaName);
                pkCmd.Parameters.AddWithValue(table);
                try { pkColumn = (string)await pkCmd.ExecuteScalarAsync(); } catch { }
            }

            if (pkColumn == null) continue;

            foreach (var col in tc.Value)
            {
                var searchSql = $"SELECT \"{pkColumn}\", \"{col}\" FROM {schemaName}.\"{table}\" WHERE \"{col}\" ILIKE @kw LIMIT 50";
                await using var searchCmd = new NpgsqlCommand(searchSql, conn);
                searchCmd.Parameters.AddWithValue("kw", keyword);
                
                try 
                {
                    await using var dataReader = await searchCmd.ExecuteReaderAsync();
                    while (await dataReader.ReadAsync())
                    {
                        results.Add(new JunkRecord
                        {
                            TableName = table,
                            PrimaryKeyColumn = pkColumn,
                            PrimaryKeyValue = dataReader[0]?.ToString(),
                            ColumnName = col,
                            DetectedContent = dataReader[1]?.ToString()
                        });
                    }
                } 
                catch { } // handle column cast errors etc gracefully
            }
        }
        
        return results;
    }

    private async Task RunPostgresToolAsync(string toolName, string arguments, Action<string>? onOutput = null)
    {
        string exe = string.IsNullOrEmpty(PostgresBinPath) ? toolName : Path.Combine(PostgresBinPath, toolName);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && string.IsNullOrEmpty(PostgresBinPath))
            exe += ".exe";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.EnvironmentVariables["PGPASSWORD"] = _config.Password;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var errorSb = new System.Text.StringBuilder();

        if (onOutput != null)
        {
            // Stream output lines live
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data != null) { onOutput(e.Data); errorSb.AppendLine(e.Data); } };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        else
        {
            process.Start();
        }

        string finalStdout = onOutput != null ? "" : await process.StandardOutput.ReadToEndAsync();
        string finalStderr = onOutput != null ? "" : await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = onOutput != null ? errorSb.ToString() : finalStderr;
            throw new Exception($"Error running {toolName}: {err}\n{finalStdout}");
        }
    }
}
