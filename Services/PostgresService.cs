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
        public uint TableOid { get; set; }
    }

    public PostgresService(DatabaseConfig config)
    {
        _config = config;
    }

    public virtual async Task<List<string>> GetAllDatabasesAsync()
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

    public virtual async Task<List<string>> GetPrimaryKeysAsync(string dbName, string tableName, string schemaName = "public")
    {
        var pks = new List<string>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = @"
            SELECT a.attname
            FROM   pg_index i
            JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE  i.indrelid = $1 AND i.indisprimary;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        // If we have an OID, use it directly (as a uint cast to long for safety)
        if (uint.TryParse(tableName, out uint tableOid)) {
            cmd.Parameters.AddWithValue((long)tableOid);
        } else {
            // Fallback to name-based if it's not a numeric OID string
            cmd.CommandText = @"
                SELECT a.attname
                FROM   pg_index i
                JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                JOIN   pg_class c ON c.oid = i.indrelid
                JOIN   pg_namespace n ON n.oid = c.relnamespace
                WHERE  n.nspname = $1 AND c.relname = $2 AND i.indisprimary;";
            cmd.Parameters.AddWithValue(schemaName);
            cmd.Parameters.AddWithValue(tableName);
        }
        
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

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaTablesAsync(string dbName, string schemaName)
    {
        var tables = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT c.relname, c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relkind IN ('r', 'p') ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        }

        return tables;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemasAsync(string dbName)
    {
        var schemas = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        
        var sql = "SELECT nspname, oid FROM pg_namespace WHERE nspname NOT IN ('information_schema', 'pg_catalog') AND nspname NOT LIKE 'pg_toast%' AND nspname NOT LIKE 'pg_temp%' AND nspname NOT LIKE 'pg_toast%';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schemas.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        }
        return schemas;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaViewsAsync(string dbName, string schemaName)
    {
        var views = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT c.relname, c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relkind = 'v' ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            views.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        }
        return views;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaRoutinesAsync(string dbName, string schemaName)
    {
        var routines = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        var sql = "SELECT p.proname, p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 ORDER BY p.proname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            routines.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        }
        return routines;
    }

    public virtual async Task<List<(uint TableOid, string TableName, string Column)>> GetSchemaColumnsAsync(string dbName, string schemaName)
    {
        var cols = new List<(uint, string, string)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        
        var sql = @"
            SELECT c.oid, c.relname, a.attname
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = $1 
              AND a.attnum > 0 
              AND NOT a.attisdropped
              AND c.relkind IN ('r', 'p', 'v', 'm') 
            ORDER BY c.relname, a.attnum;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) 
            cols.Add((reader.GetFieldValue<uint>(0), reader.GetString(1), reader.GetString(2)));
        return cols;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaIndexesAsync(string dbName, string schemaName)
    {
        var indexes = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT c.relname, c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind IN ('i', 'I') AND n.nspname = $1 ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) indexes.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return indexes;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaTypesAsync(string dbName, string schemaName)
    {
        var types = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"
            SELECT t.typname, t.oid 
            FROM pg_type t 
            JOIN pg_namespace n ON n.oid = t.typnamespace 
            LEFT JOIN pg_class c ON c.oid = t.typrelid
            WHERE n.nspname = $1 
              AND t.typtype IN ('b', 'c', 'e', 'm', 'r') 
              AND t.typname NOT LIKE '\_%' 
              AND (t.typrelid = 0 OR (c.oid IS NOT NULL AND c.relkind = 'c'))
            ORDER BY typname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) types.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return types;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetRolesAsync(string dbName)
    {
        var roles = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT rolname, oid FROM pg_roles WHERE rolname NOT LIKE 'pg_%' AND rolname NOT LIKE 'rds%' ORDER BY rolname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) roles.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return roles;
    }

    public virtual async Task<List<(string Table, string Trigger, uint Oid)>> GetSchemaTriggersAsync(string dbName, string schemaName)
    {
        var triggers = new List<(string, string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT c.relname, t.tgname, t.oid FROM pg_trigger t 
                    JOIN pg_class c ON c.oid = t.tgrelid 
                    JOIN pg_namespace n ON n.oid = c.relnamespace 
                    WHERE n.nspname = $1 AND t.tgisinternal = false 
                    ORDER BY t.tgname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) triggers.Add((reader.GetString(0), reader.GetString(1), reader.GetFieldValue<uint>(2)));
        return triggers;
    }

    public virtual async Task<List<(string Parent, string Constraint, uint Oid)>> GetSchemaConstraintsAsync(string dbName, string schemaName)
    {
        var constraints = new List<(string, string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT 
                        COALESCE(c.relname, 'Domain: ' || t.typname) as parent, 
                        con.conname,
                        con.oid
                    FROM pg_constraint con 
                    JOIN pg_namespace n ON n.oid = con.connamespace 
                    LEFT JOIN pg_class c ON c.oid = con.conrelid 
                    LEFT JOIN pg_type t ON t.oid = con.contypid 
                    WHERE n.nspname = $1 
                    ORDER BY con.conname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) constraints.Add((reader.GetString(0), reader.GetString(1), reader.GetFieldValue<uint>(2)));
        return constraints;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaPartitionsAsync(string dbName, string schemaName)
    {
        var parts = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = @"SELECT c.relname, c.oid FROM pg_class c 
                    JOIN pg_namespace n ON n.oid = c.relnamespace 
                    WHERE n.nspname = $1 AND (c.relkind = 'p' OR c.relispartition = true) 
                    ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) parts.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return parts;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaMatViewsAsync(string dbName, string schemaName)
    {
        var views = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT c.relname, c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'm' AND n.nspname = $1 ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) views.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return views;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaSequencesAsync(string dbName, string schemaName)
    {
        var seqs = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT c.relname, c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'S' AND n.nspname = $1 ORDER BY c.relname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) seqs.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return seqs;
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaDomainsAsync(string dbName, string schemaName)
    {
        var domains = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = "SELECT typname, oid FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = $1 AND t.typtype = 'd' ORDER BY typname;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) domains.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        return domains;
    }

    public virtual async Task<string> GetObjectCommentAsync(string dbName, string schemaName, string objectName, JunkType type)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        string sql = "";
        
        switch (type) {
            case JunkType.Table:
            case JunkType.View:
            case JunkType.MaterializedView:
            case JunkType.Sequence:
            case JunkType.Index:
            case JunkType.Partition:
                sql = "SELECT obj_description(c.oid, 'pg_class') FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relname = $2 LIMIT 1;";
                break;
            case JunkType.Routine:
                sql = "SELECT obj_description(p.oid, 'pg_proc') FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.proname = $2 LIMIT 1;";
                break;
            case JunkType.Trigger:
                sql = "SELECT obj_description(t.oid, 'pg_trigger') FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND t.tgname = $2 LIMIT 1;";
                break;
            case JunkType.Constraint:
                sql = "SELECT obj_description(con.oid, 'pg_constraint') FROM pg_constraint con JOIN pg_namespace n ON n.oid = con.connamespace WHERE n.nspname = $1 AND con.conname = $2 LIMIT 1;";
                break;
            case JunkType.Role:
                return await GetGlobalCommentAsync(dbName, objectName, "pg_authid");
            default:
                return "";
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        cmd.Parameters.AddWithValue(objectName);
        var resValue = await cmd.ExecuteScalarAsync();
        return resValue?.ToString() ?? "";
    }

    private async Task<string> GetGlobalCommentAsync(string dbName, string objectName, string typeName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"SELECT obj_description(oid, '{typeName}') FROM pg_roles WHERE rolname = $1 LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(objectName);
        var res = await cmd.ExecuteScalarAsync();
        return res?.ToString() ?? "";
    }

    public virtual async Task<string> GetObjectDefinitionAsync(string dbName, string schemaName, string objectName, JunkType type, uint oid = 0)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        string sql = "";
        
        if (oid > 0)
        {
            switch (type) {
                case JunkType.Routine: 
                    sql = "SELECT pg_get_functiondef($1);"; 
                    break;
                case JunkType.View: 
                case JunkType.MaterializedView:
                    sql = "SELECT 'CREATE OR REPLACE ' || (CASE WHEN c.relkind = 'm' THEN 'MATERIALIZED VIEW ' ELSE 'VIEW ' END) || n.nspname || '.' || c.relname || ' AS\n' || pg_get_viewdef(c.oid, true) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.oid = $1 LIMIT 1;";
                    break;
                case JunkType.Trigger:
                    sql = "SELECT pg_get_triggerdef($1, true);";
                    break;
                case JunkType.Index:
                    sql = "SELECT pg_get_indexdef($1);";
                    break;
                case JunkType.Table:
                case JunkType.Partition:
                    Console.WriteLine($"[DDL] Fetching table definition for: {dbName}.{schemaName}.{objectName} (OID: {oid})");
                    var tableCols = new List<string>();
                    
                    // 1. Try OID-based query first
                    var colSql = @"
                        SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod) as type,
                               (SELECT pg_get_expr(d.adbin, d.adrelid) FROM pg_catalog.pg_attrdef d WHERE d.adrelid = a.attrelid AND d.adnum = a.attnum AND a.atthasdef) as default_val,
                               a.attnotnull
                        FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = $1 AND a.attnum > 0 AND NOT a.attisdropped
                        ORDER BY a.attnum;";

                    await using (var colCmd = new NpgsqlCommand(colSql, conn)) {
                        colCmd.Parameters.AddWithValue((long)oid);  // cast to long for pg_attribute.attrelid compatibility
                        await using (var reader = await colCmd.ExecuteReaderAsync()) {
                            while (await reader.ReadAsync()) {
                                string colName = reader.GetString(0);
                                Console.WriteLine($"[DDL-DEBUG] Table {objectName} found column: {colName}");
                                string colType = reader.GetString(1);
                                string defaultVal = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                bool notNull = reader.GetBoolean(3);
                                string line = $"  \"{colName}\" {colType}";
                                if (!string.IsNullOrEmpty(defaultVal)) line += $" DEFAULT {defaultVal}";
                                line += notNull ? " NOT NULL" : " NULL";
                                tableCols.Add(line);
                            }
                        }
                    }

                    // 2. Fallback to Name-based query if OID didn't work
                    if (tableCols.Count == 0) {
                        Console.WriteLine($"[DDL-WARNING] OID-based query returned 0 columns for OID {oid}. Falling back to Name-based for {schemaName}.{objectName}");
                        var fallbackSql = @"
                            SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod) as type,
                                   (SELECT pg_get_expr(d.adbin, d.adrelid) FROM pg_catalog.pg_attrdef d WHERE d.adrelid = a.attrelid AND d.adnum = a.attnum AND a.atthasdef) as default_val,
                                   a.attnotnull
                            FROM pg_catalog.pg_attribute a
                            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
                            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                            WHERE n.nspname = $1 AND c.relname = $2 
                              AND a.attnum > 0 AND NOT a.attisdropped
                            ORDER BY a.attnum;";
                        await using (var fbCmd = new NpgsqlCommand(fallbackSql, conn)) {
                            fbCmd.Parameters.AddWithValue(schemaName);
                            fbCmd.Parameters.AddWithValue(objectName);
                            await using (var reader = await fbCmd.ExecuteReaderAsync()) {
                                while (await reader.ReadAsync()) {
                                    string colName = reader.GetString(0);
                                    Console.WriteLine($"[DDL-FALLBACK-DEBUG] Table {objectName} found column: {colName}");
                                    string line = $"  \"{colName}\" {reader.GetString(1)}";
                                    if (!reader.IsDBNull(2)) line += $" DEFAULT {reader.GetString(2)}";
                                    line += reader.GetBoolean(3) ? " NOT NULL" : " NULL";
                                    tableCols.Add(line);
                                }
                            }
                        }
                    }
                    
                    // 3. Add Primary Key info (OID prioritized, fallback to name)
                    var pkSql = @"SELECT pg_get_constraintdef(con.oid, true) FROM pg_constraint con WHERE con.conrelid = $1 AND con.contype = 'p';";
                    if (tableCols.Count > 0) {
                        await using (var pkCmd = new NpgsqlCommand(pkSql, conn)) {
                            pkCmd.Parameters.AddWithValue((long)oid);  // cast to long for pg_constraint.conrelid compatibility
                            var pkDefRes = await pkCmd.ExecuteScalarAsync();
                            string? pkDef = pkDefRes?.ToString();

                            if (string.IsNullOrEmpty(pkDef)) {
                                // Fallback PK query by name
                                var pkFbSql = @"SELECT pg_get_constraintdef(con.oid, true) 
                                               FROM pg_constraint con 
                                               JOIN pg_class c ON c.oid = con.conrelid
                                               JOIN pg_namespace n ON n.oid = c.relnamespace
                                               WHERE n.nspname = $1 AND c.relname = $2 AND con.contype = 'p';";
                                await using (var pkFbCmd = new NpgsqlCommand(pkFbSql, conn)) {
                                    pkFbCmd.Parameters.AddWithValue(schemaName);
                                    pkFbCmd.Parameters.AddWithValue(objectName);
                                    pkDef = (await pkFbCmd.ExecuteScalarAsync())?.ToString();
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(pkDef)) {
                                // Important: pg_get_constraintdef usually returns "PRIMARY KEY (col1, col2)"
                                // We check if it already contains the prefix.
                                if (pkDef.Trim().StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
                                    tableCols.Add($"  {pkDef}");
                                else
                                    tableCols.Add($"  PRIMARY KEY {pkDef}");
                            }
                        }
                    }

                    return $"CREATE TABLE \"{schemaName}\".\"{objectName}\" (\n{string.Join(",\n", tableCols)}\n);";

                case JunkType.Constraint:
                    var conSql = @"SELECT 
                                    n.nspname, c.relname, con.conname, pg_get_constraintdef(con.oid, true) 
                                   FROM pg_constraint con 
                                   JOIN pg_class c ON c.oid = con.conrelid 
                                   JOIN pg_namespace n ON n.oid = con.connamespace 
                                   WHERE con.oid = $1 LIMIT 1;";
                    await using (var conCmd = new NpgsqlCommand(conSql, conn)) {
                        var p1 = conCmd.Parameters.AddWithValue((uint)oid);
                        p1.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Oid;
                        await using (var reader = await conCmd.ExecuteReaderAsync()) {
                            if (await reader.ReadAsync()) {
                                return $"ALTER TABLE \"{reader.GetString(0)}\".\"{reader.GetString(1)}\"\n  ADD CONSTRAINT \"{reader.GetString(2)}\"\n  {reader.GetString(3)};";
                            }
                        }
                    }
                    return $"-- Constraint definition not found for OID {oid}";
            }

            if (!string.IsNullOrEmpty(sql))
            {
                await using var cmdOid = new NpgsqlCommand(sql, conn);
                cmdOid.Parameters.AddWithValue((long)oid);  // cast to long for OID function arguments
                var res = await cmdOid.ExecuteScalarAsync();
                return res?.ToString() ?? "";
            }
        }

        switch (type) {
            case JunkType.Routine: 
                sql = "SELECT pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.proname = $2 LIMIT 1;"; 
                break;
            case JunkType.View: 
            case JunkType.MaterializedView:
                sql = "SELECT 'CREATE OR REPLACE ' || (CASE WHEN c.relkind = 'm' THEN 'MATERIALIZED VIEW ' ELSE 'VIEW ' END) || n.nspname || '.' || c.relname || ' AS\n' || pg_get_viewdef(c.oid, true) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relname = $2 LIMIT 1;";
                break;
            case JunkType.Trigger:
                sql = "SELECT pg_get_triggerdef(t.oid, true) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND t.tgname = $2 LIMIT 1;";
                break;
            case JunkType.Index:
                sql = "SELECT pg_get_indexdef(c.oid) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = $1 AND c.relname = $2 LIMIT 1;";
                break;
            case JunkType.Table:
            case JunkType.Partition:
                return $"-- Table/Partition: \"{schemaName}\".\"{objectName}\" (OID not provided for lazy load)";
            case JunkType.Constraint:
                sql = "SELECT pg_get_constraintdef(con.oid, true) FROM pg_constraint con JOIN pg_namespace n ON n.oid = con.connamespace WHERE n.nspname = $1 AND con.conname = $2 LIMIT 1;";
                break;
            default:
                return $"-- Definition for {type}: \"{schemaName}\".\"{objectName}\"";
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(schemaName);
        cmd.Parameters.AddWithValue(objectName);
        var resV = await cmd.ExecuteScalarAsync();
        return resV?.ToString() ?? "";
    }

    public virtual async Task<List<(string Name, uint Oid)>> GetSchemaAggregatesAsync(string dbName, string schemaName)
    {
        var aggs = new List<(string, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        try {
            var sql = "SELECT proname, oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.proisagg = true ORDER BY proname;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(schemaName);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) aggs.Add((reader.GetString(0), reader.GetFieldValue<uint>(1)));
        } catch { 
            await using var cmd2 = new NpgsqlCommand("SELECT proname, oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = $1 AND p.prokind = 'a' ORDER BY proname;", conn);
            cmd2.Parameters.AddWithValue(schemaName);
            await using var reader2 = await cmd2.ExecuteReaderAsync();
            while (await reader2.ReadAsync()) aggs.Add((reader2.GetString(0), (uint)reader2.GetInt64(1)));
        }
        return aggs;
    }

    public virtual async Task<List<(string Schema, string Name, JunkType Type, uint Oid)>> GetDependentObjectsRecursiveAsync(string dbName, uint targetOid, int maxDepth = 5)
    {
        var deps = new List<(string, string, JunkType, uint)>();
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        string sql = @"
            WITH RECURSIVE dependency_tree AS (
                SELECT d.objid, d.classid, 1 as depth
                FROM pg_depend d
                WHERE d.refobjid = @targetOid AND d.deptype IN ('n', 'a')
                UNION
                SELECT d.objid, d.classid, dt.depth + 1
                FROM pg_depend d
                JOIN dependency_tree dt ON d.refobjid = dt.objid
                WHERE d.deptype IN ('n', 'a') AND dt.depth < @maxDepth
            )
            SELECT DISTINCT 
                dt.objid, dt.classid, n.nspname as schema_name,
                COALESCE(c.relname, p.proname, t.typname, trg.tgname, con.conname) as obj_name,
                CASE 
                    WHEN c.relkind = 'r' THEN 'Table'
                    WHEN c.relkind = 'v' THEN 'View'
                    WHEN c.relkind = 'm' THEN 'MaterializedView'
                    WHEN c.relkind = 'S' THEN 'Sequence'
                    WHEN c.relkind = 'i' THEN 'Index'
                    WHEN p.oid IS NOT NULL THEN 'Routine'
                    WHEN t.typtype = 'd' THEN 'Domain'
                    WHEN t.oid IS NOT NULL THEN 'DataType'
                    WHEN trg.oid IS NOT NULL THEN 'Trigger'
                    WHEN con.oid IS NOT NULL THEN 'Constraint'
                    ELSE 'Other'
                END as obj_type
            FROM dependency_tree dt
            LEFT JOIN pg_class c ON c.oid = dt.objid AND dt.classid = 'pg_class'::regclass
            LEFT JOIN pg_proc p ON p.oid = dt.objid AND dt.classid = 'pg_proc'::regclass
            LEFT JOIN pg_type t ON t.oid = dt.objid AND dt.classid = 'pg_type'::regclass
            LEFT JOIN pg_trigger trg ON trg.oid = dt.objid AND dt.classid = 'pg_trigger'::regclass
            LEFT JOIN pg_constraint con ON con.oid = dt.objid AND dt.classid = 'pg_constraint'::regclass
            LEFT JOIN pg_namespace n ON n.oid = COALESCE(c.relnamespace, p.pronamespace, t.typnamespace, con.connamespace)
            WHERE dt.objid <> @targetOid;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("targetOid", (long)targetOid);
        cmd.Parameters.AddWithValue("maxDepth", maxDepth);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var schema = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var name = reader.IsDBNull(3) ? "unknown" : reader.GetString(3);
            var typeStr = reader.GetString(4);
            if (Enum.TryParse<JunkType>(typeStr, out var type))
                deps.Add((schema, name, type, reader.GetFieldValue<uint>(0)));
        }
        return deps;
    }

    public virtual async Task<long> GetTableRowCountAsync(string dbName, string tableName, string schemaName = "public")
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{schemaName}\".\"{tableName}\"", conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    public virtual async Task BackupDatabaseAsync(string outputPath)
    {
        await RunPostgresToolAsync("pg_dump", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -F c -f \"{outputPath}\" {_config.DatabaseName}");
    }

    public virtual async Task DumpFullScriptAsync(string outputPath)
    {
        await RunPostgresToolAsync("pg_dump", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -F p -f \"{outputPath}\" {_config.DatabaseName}");
    }

    public virtual async Task CreateDatabaseIfNotExistsAsync(string dbName)
    {
        var adminConnStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database=postgres";
        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @name", conn);
        checkCmd.Parameters.AddWithValue("name", dbName);
        var exists = await checkCmd.ExecuteScalarAsync();
        if (exists == null)
        {
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    public virtual async Task RestoreDatabaseAsync(string fileType, string backupPath, string? targetDbName = null, Action<string>? onOutput = null)
    {
        var db = string.IsNullOrWhiteSpace(targetDbName) ? _config.DatabaseName : targetDbName;
        if (!string.IsNullOrWhiteSpace(targetDbName)) await CreateDatabaseIfNotExistsAsync(targetDbName);

        if (fileType == ".sql")
            await RunPostgresToolAsync("psql", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {db} -f \"{backupPath}\"", onOutput);
        else
            await RunPostgresToolAsync("pg_restore", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {db} --clean --if-exists -1 --verbose \"{backupPath}\"", onOutput);
    }

    public virtual async Task ExecuteScriptFileAsync(string scriptPath)
    {
        await RunPostgresToolAsync("psql", $"-h {_config.Host} -p {_config.Port} -U {_config.Username} -d {_config.DatabaseName} -f \"{scriptPath}\" -v ON_ERROR_STOP=1 --single-transaction");
    }

    public virtual async Task ExecuteSqlWithTransactionAsync(string sql)
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
        catch { await transaction.RollbackAsync(); throw; }
    }

    public virtual async Task DeleteRecordAsync(string dbName, string tableName, string pkColumn, string pkValue, string schemaName = "public")
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DELETE FROM \"{schemaName}\".\"{tableName}\" WHERE \"{pkColumn}\" = @pkValue";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pkValue", pkValue);
        await cmd.ExecuteNonQueryAsync();
    }

    public virtual async Task DropSchemaAsync(string dbName, string schemaName, bool cascade = true)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP SCHEMA IF EXISTS \"{schemaName}\" {(cascade ? "CASCADE" : "RESTRICT")}";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public virtual async Task DropTableAsync(string dbName, string schemaName, string tableName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP TABLE IF EXISTS \"{schemaName}\".\"{tableName}\" CASCADE";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public virtual async Task DropViewAsync(string dbName, string schemaName, string viewName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP VIEW IF EXISTS \"{schemaName}\".\"{viewName}\" CASCADE";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public virtual async Task DropRoutineAsync(string dbName, string schemaName, string routineName)
    {
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var sql = $"DROP ROUTINE IF EXISTS \"{schemaName}\".\"{routineName}\" CASCADE";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public virtual async Task<List<JunkRecord>> SearchJunkDataAsync(string dbName, string searchKeyword, string schemaName = "public")
    {
        var results = new List<JunkRecord>();
        var keyword = $"%{searchKeyword}%";
        var connStr = $"Host={_config.Host};Port={_config.Port};Username={_config.Username};Password={_config.Password};Database={dbName}";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var metaSql = @"
            SELECT c.relname, a.attname, c.oid
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid
            JOIN pg_type t ON t.oid = a.atttypid
            WHERE n.nspname = @schema 
              AND c.relkind IN ('r', 'p') 
              AND a.attnum > 0 
              AND NOT a.attisdropped
              AND t.typname IN ('text', 'varchar', 'char', 'bpchar');";
        var tableCols = new Dictionary<string, (uint Oid, List<string> Cols)>();
        await using (var cmd = new NpgsqlCommand(metaSql, conn))
        {
            cmd.Parameters.AddWithValue("schema", schemaName);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var col = reader.GetString(1);
                var tOid = reader.GetFieldValue<uint>(2);
                if (!tableCols.ContainsKey(table)) tableCols[table] = (tOid, new List<string>());
                tableCols[table].Cols.Add(col);
            }
        }
        foreach (var tc in tableCols)
        {
            var table = tc.Key;
            var tableOid = tc.Value.Oid;
            var pkSql = @"
                SELECT a.attname 
                FROM pg_index i
                JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                WHERE i.indrelid = $1 AND i.indisprimary LIMIT 1;";
            string? pkColumn = null;
            await using (var pkCmd = new NpgsqlCommand(pkSql, conn))
            {
                pkCmd.Parameters.AddWithValue((long)tableOid);
                try { pkColumn = (await pkCmd.ExecuteScalarAsync())?.ToString(); } 
                catch (Exception ex) { 
                    Console.WriteLine($"[PK-ERROR] Failed to find PK for table OID {tableOid}: {ex.Message}");
                }
            }
            if (pkColumn == null) continue;
            foreach (var col in tc.Value.Cols)
            {
                var searchSql = $"SELECT \"{pkColumn}\", \"{col}\" FROM \"{schemaName}\".\"{table}\" WHERE \"{col}\" ILIKE @kw LIMIT 50";
                await using var searchCmd = new NpgsqlCommand(searchSql, conn);
                searchCmd.Parameters.AddWithValue("kw", keyword);
                try 
                {
                    await using var dataReader = await searchCmd.ExecuteReaderAsync();
                    while (await dataReader.ReadAsync())
                        results.Add(new JunkRecord { 
                            TableName = table, 
                            PrimaryKeyColumn = pkColumn, 
                            PrimaryKeyValue = dataReader[0]?.ToString(), 
                            ColumnName = col, 
                            DetectedContent = dataReader[1]?.ToString(),
                            TableOid = tableOid
                        });
                } catch { }
            }
        }
        return results;
    }

    public virtual async Task<Dictionary<string, string>> GetFullRowDataAsync(string dbName, string schemaName, string tableName, string pkColumn, string pkValue)
    {
        var row = new Dictionary<string, string>();
        var builder = new NpgsqlConnectionStringBuilder(_config.GetConnectionString()) { Database = dbName };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        // Use ::text cast so UUID/integer/other columns compare correctly with the string pkValue
        var sql = $"SELECT * FROM \"{schemaName}\".\"{tableName}\" WHERE \"{pkColumn}\"::text = @pk LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pk", pkValue);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader[i]?.ToString() ?? "NULL";
            else
                Console.WriteLine($"[DETAIL] No row found: {schemaName}.{tableName} WHERE {pkColumn}::text = '{pkValue}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DETAIL-ERROR] GetFullRowDataAsync failed for {schemaName}.{tableName}: {ex.Message}");
        }
        return row;
    }
    public virtual async Task<List<Dictionary<string, string>>> GetRowsByFkAsync(string dbName, string schemaName, string tableName, string fkColumn, string fkValue, int limit = 500)
    {
        var rows = new List<Dictionary<string, string>>();
        var builder = new NpgsqlConnectionStringBuilder(_config.GetConnectionString()) { Database = dbName };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        
        // Use ::text cast so UUID/integer/other columns compare correctly with the string fkValue
        var sql = $"SELECT * FROM \"{schemaName}\".\"{tableName}\" WHERE \"{fkColumn}\"::text = @fk LIMIT {limit};";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("fk", fkValue);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, string>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader[i]?.ToString() ?? "NULL";
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CASCADE-DETAIL-ERROR] GetRowsByFkAsync failed for {schemaName}.{tableName}: {ex.Message}");
        }
        return rows;
    }

    /// <summary>
    /// Finds all FK child records that reference the given primary key value in the given table.
    /// Used for cascade impact analysis on data records.
    /// Returns a list of (ChildSchema, ChildTable, FkColumn, ChildRowCount).
    /// </summary>
    public virtual async Task<List<(string Schema, string Table, string FkColumn, long Count)>>
        GetFkCascadeImpactAsync(string dbName, uint tableOid, string pkColumn, string pkValue)
    {
        var impacts = new List<(string, string, string, long)>();
        
        Console.WriteLine($"[CASCADE] GetFkCascadeImpactAsync called: dbName={dbName}, tableOid={tableOid}, pkColumn={pkColumn}, pkValue={pkValue}");
        
        if (tableOid == 0)
        {
            Console.WriteLine($"[CASCADE-WARN] tableOid is 0! Cannot find FK references. Skipping cascade analysis.");
            return impacts;
        }
        
        var builder = new NpgsqlConnectionStringBuilder(_config.GetConnectionString()) { Database = dbName };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        // 1. Find all FK constraints that reference this table (confrelid = parent table OID)
        // Fixed: use unnest(con.conkey) with JOIN on attnum to correctly resolve FK column names
        var fkSql = @"
            SELECT DISTINCT
                n.nspname               AS child_schema,
                c.relname               AS child_table,
                a.attname               AS fk_column,
                con.oid                 AS constraint_oid
            FROM pg_constraint con
            JOIN pg_class c         ON c.oid = con.conrelid
            JOIN pg_namespace n     ON n.oid = c.relnamespace
            JOIN pg_attribute a     ON a.attrelid = con.conrelid
                                   AND a.attnum = ANY(con.conkey)
                                   AND a.attnum > 0
                                   AND NOT a.attisdropped
            WHERE con.contype = 'f'
              AND con.confrelid = $1
            ORDER BY child_schema, child_table, fk_column;";

        var fkRefs = new List<(string childSchema, string childTable, string fkCol)>();
        try
        {
            await using (var fkCmd = new NpgsqlCommand(fkSql, conn))
            {
                fkCmd.Parameters.AddWithValue((long)tableOid);
                await using var fkReader = await fkCmd.ExecuteReaderAsync();
                while (await fkReader.ReadAsync())
                {
                    var cs = fkReader.GetString(0);
                    var ct = fkReader.GetString(1);
                    var fc = fkReader.GetString(2);
                    fkRefs.Add((cs, ct, fc));
                    Console.WriteLine($"[CASCADE-FK] Found FK reference: {cs}.{ct}.{fc} -> tableOid={tableOid}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CASCADE-ERROR] Failed to query FK references for tableOid={tableOid}: {ex.Message}");
            return impacts;
        }

        Console.WriteLine($"[CASCADE] Found {fkRefs.Count} FK reference(s) for tableOid={tableOid}");

        // 2. For each FK reference, count how many child rows reference the given PK value
        foreach (var (childSchema, childTable, fkCol) in fkRefs)
        {
            try
            {
                // Use ::text cast on FK column to ensure compatibility with UUID and other types
                var countSql = $"SELECT COUNT(*) FROM \"{childSchema}\".\"{childTable}\" WHERE \"{fkCol}\"::text = @pkValue";
                Console.WriteLine($"[CASCADE-COUNT] Running: {countSql} with pkValue='{pkValue}'");
                await using var countCmd = new NpgsqlCommand(countSql, conn);
                countCmd.Parameters.AddWithValue("pkValue", pkValue);
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync() ?? 0L);
                Console.WriteLine($"[CASCADE-COUNT] Result: {count} rows in {childSchema}.{childTable} reference '{pkValue}' via {fkCol}");
                if (count > 0)
                {
                    impacts.Add((childSchema, childTable, fkCol, count));
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[CASCADE-ERROR] Failed to count impact on {childSchema}.{childTable}.{fkCol}: {ex.Message}");
            }
        }

        Console.WriteLine($"[CASCADE] GetFkCascadeImpactAsync done: {impacts.Count} impact(s) found for pkValue='{pkValue}'");
        return impacts;
    }

    /// <summary>
    /// Gets specific PK values of child records for deeper cascade analysis.
    /// </summary>
    public virtual async Task<List<string>> GetFkChildPkValuesAsync(string dbName, string schemaName, string tableName, string pkColumn, string fkColumn, string parentPkValue)
    {
        var pkValues = new List<string>();
        var builder = new NpgsqlConnectionStringBuilder(_config.GetConnectionString()) { Database = dbName };
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        var sql = $"SELECT \"{pkColumn}\"::text FROM \"{schemaName}\".\"{tableName}\" WHERE \"{fkColumn}\"::text = @parentPk LIMIT 1000";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("parentPk", parentPkValue);
        
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                pkValues.Add(reader[0]?.ToString() ?? "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CASCADE-ERROR] Failed to fetch child PKs for {schemaName}.{tableName}: {ex.Message}");
        }

        return pkValues;
    }

    private async Task RunPostgresToolAsync(string toolName, string arguments, Action<string>? onOutput = null)
    {
        string exe = string.IsNullOrEmpty(PostgresBinPath) ? toolName : Path.Combine(PostgresBinPath, toolName);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && string.IsNullOrEmpty(PostgresBinPath)) exe += ".exe";
        var psi = new ProcessStartInfo { FileName = exe, Arguments = arguments, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.EnvironmentVariables["PGPASSWORD"] = _config.Password;
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var errorSb = new System.Text.StringBuilder();
        if (onOutput != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data != null) { onOutput(e.Data); errorSb.AppendLine(e.Data); } };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        else process.Start();
        string stdOut = onOutput != null ? "" : await process.StandardOutput.ReadToEndAsync();
        string stdErr = onOutput != null ? "" : await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception($"Error running {toolName}: {(onOutput != null ? errorSb.ToString() : stdErr)}\n{stdOut}");
    }
}
