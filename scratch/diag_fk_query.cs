
using Npgsql;
using System;
using System.Threading.Tasks;

public class Program {
    public static async Task Main() {
        var connStr = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // Let's find a table that is referenced by someone else
        var sql = @"
            SELECT con.confrelid, c_ref.relname as ref_table, c_child.relname as child_table
            FROM pg_constraint con
            JOIN pg_class c_ref ON c_ref.oid = con.confrelid
            JOIN pg_class c_child ON c_child.oid = con.conrelid
            WHERE con.contype = 'f'
            LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync()) {
            uint targetOid = reader.GetFieldValue<uint>(0);
            string refTable = reader.GetString(1);
            string childTable = reader.GetString(2);
            Console.WriteLine($"Found FK: {childTable} references {refTable} (OID: {targetOid})");

            await reader.CloseAsync();

            // Now test the query from the code
            var fkSql = @"
                SELECT DISTINCT
                    n.nspname   AS child_schema,
                    c.relname   AS child_table,
                    a.attname   AS fk_column
                FROM pg_constraint con
                JOIN pg_class c      ON c.oid = con.conrelid
                JOIN pg_namespace n  ON n.oid = c.relnamespace
                JOIN pg_attribute a  ON a.attrelid = c.oid AND a.attnum = ANY(con.conkey)
                WHERE con.contype = 'f'
                  AND con.confrelid = $1;";

            await using var fkCmd = new NpgsqlCommand(fkSql, conn);
            // fkCmd.Parameters.AddWithValue((long)targetOid); // The suspicious cast
            
            // Try with uint first
            fkCmd.Parameters.AddWithValue((uint)targetOid);
            int count = 0;
            await using (var fkReader = await fkCmd.ExecuteReaderAsync()) {
                while (await fkReader.ReadAsync()) count++;
            }
            Console.WriteLine($"Query with uint: Found {count} results.");

            // Try with long 
            var fkCmd2 = new NpgsqlCommand(fkSql, conn);
            fkCmd2.Parameters.AddWithValue((long)targetOid);
            count = 0;
            try {
                await using (var fkReader2 = await fkCmd2.ExecuteReaderAsync()) {
                    while (await fkReader2.ReadAsync()) count++;
                }
                Console.WriteLine($"Query with long: Found {count} results.");
            } catch (Exception ex) {
                Console.WriteLine($"Query with long failed: {ex.Message}");
            }
        } else {
            Console.WriteLine("No foreign keys found in the 'postgres' database to test with.");
        }
    }
}
