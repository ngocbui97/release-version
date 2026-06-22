using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ReleasePrepTool.Services
{
    public class ConvertScriptResult
    {
        public string ConvertedSql { get; set; } = "";
        public List<string> IgnoredDetails { get; set; } = new();
        public List<string> ModifiedDetails { get; set; } = new();
        public int TotalOriginal { get; set; }
        public int UnchangedCount { get; set; }

        public string BuildLog(bool tuneScript, string targetSchema,
            bool ignoreOwner, bool ignorePrivileges, bool ignoreTablespaces,
            bool ignoreComments, bool ignorePublications, bool ignoreSubscriptions,
            bool ignoreSecurityLabels, bool ignoreTableAccessMethods,
            bool ignoreData, bool ignoreSchema, bool ignoreTransaction)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("                       SQL SCRIPT CONVERSION LOG");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine("SUMMARY:");
            sb.AppendLine($"- Total original statements:    {TotalOriginal}");
            sb.AppendLine($"- Unchanged statements:         {UnchangedCount}");
            sb.AppendLine($"- Modified statements:          {ModifiedDetails.Count}");
            sb.AppendLine($"- Ignored (removed) statements: {IgnoredDetails.Count}");
            sb.AppendLine();

            if (IgnoredDetails.Count > 0)
            {
                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine($"IGNORED STATEMENTS ({IgnoredDetails.Count}):");
                sb.AppendLine("--------------------------------------------------------------------------------");
                foreach (var detail in IgnoredDetails)
                    sb.AppendLine(detail);
                sb.AppendLine();
            }

            if (ModifiedDetails.Count > 0)
            {
                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine($"MODIFIED STATEMENTS ({ModifiedDetails.Count}):");
                sb.AppendLine("--------------------------------------------------------------------------------");
                foreach (var detail in ModifiedDetails)
                {
                    sb.AppendLine(detail);
                    sb.AppendLine();
                }
            }

            if (IgnoredDetails.Count == 0 && ModifiedDetails.Count == 0)
                sb.AppendLine("No changes detected. The script is identical to the converted output.");

            return sb.ToString();
        }
    }

    public static class SqlTuningHelper
    {
        public static string TuneSchemaScript(string sql, string targetSchema)
        {
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            var lines = sql.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                // 1. CREATE TABLE -> CREATE TABLE IF NOT EXISTS
                if (trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(CREATE\s+TABLE\s+)(.*)", "$1IF NOT EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 2. CREATE INDEX -> CREATE INDEX IF NOT EXISTS
                else if (trimmed.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(CREATE\s+(?:UNIQUE\s+)?INDEX\s+)(.*)", "$1IF NOT EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 3. CREATE SCHEMA -> CREATE SCHEMA IF NOT EXISTS
                else if (trimmed.StartsWith("CREATE SCHEMA", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(CREATE\s+SCHEMA\s+)(.*)", "$1IF NOT EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 4. CREATE SEQUENCE -> CREATE SEQUENCE IF NOT EXISTS
                else if (trimmed.StartsWith("CREATE SEQUENCE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(CREATE\s+SEQUENCE\s+)(.*)", "$1IF NOT EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 5. CREATE EXTENSION -> CREATE EXTENSION IF NOT EXISTS
                else if (trimmed.StartsWith("CREATE EXTENSION", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(CREATE\s+EXTENSION\s+)(.*)", "$1IF NOT EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 6. ALTER TABLE ... ADD COLUMN -> ADD COLUMN IF NOT EXISTS
                else if (trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("ADD COLUMN", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(ADD\s+COLUMN\s+)(.*)", "$1IF NOT EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 7. ALTER TABLE ... DROP COLUMN -> DROP COLUMN IF EXISTS
                else if (trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(DROP\s+COLUMN\s+)(.*)", "$1IF EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 8. ALTER TABLE ... DROP CONSTRAINT -> DROP CONSTRAINT IF EXISTS
                else if (trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("DROP CONSTRAINT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!trimmed.Contains("IF EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        line = Regex.Replace(line, @"(DROP\s+CONSTRAINT\s+)(.*)", "$1IF EXISTS $2", RegexOptions.IgnoreCase);
                    }
                }
                // 9. CREATE TRIGGER -> DROP TRIGGER IF EXISTS followed by CREATE TRIGGER
                else if (trimmed.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase))
                {
                    // Find trigger name and table name
                    // Syntax: CREATE TRIGGER trigger_name BEFORE/AFTER/INSTEAD OF event ON table_name ...
                    var match = Regex.Match(trimmed, @"CREATE\s+TRIGGER\s+(\w+)\s+.*?\s+ON\s+(\S+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string triggerName = match.Groups[1].Value;
                        string tableName = match.Groups[2].Value.TrimEnd(';');
                        result.AppendLine($"DROP TRIGGER IF EXISTS {triggerName} ON {tableName};");
                    }
                }
                // 10. CREATE OR REPLACE for VIEW, FUNCTION, PROCEDURE, ROUTINE
                // The current compare tool already generates CREATE OR REPLACE VIEW or CREATE OR REPLACE FUNCTION/PROCEDURE, but we ensure it.
                else if (trimmed.StartsWith("CREATE VIEW", StringComparison.OrdinalIgnoreCase))
                {
                    line = Regex.Replace(line, @"CREATE\s+VIEW", "CREATE OR REPLACE VIEW", RegexOptions.IgnoreCase);
                }
                else if (trimmed.StartsWith("CREATE FUNCTION", StringComparison.OrdinalIgnoreCase))
                {
                    line = Regex.Replace(line, @"CREATE\s+FUNCTION", "CREATE OR REPLACE FUNCTION", RegexOptions.IgnoreCase);
                }
                else if (trimmed.StartsWith("CREATE PROCEDURE", StringComparison.OrdinalIgnoreCase))
                {
                    line = Regex.Replace(line, @"CREATE\s+PROCEDURE", "CREATE OR REPLACE PROCEDURE", RegexOptions.IgnoreCase);
                }
                // 11. MATERIALIZED VIEW -> Materialized View does not support CREATE OR REPLACE.
                // We must DROP MATERIALIZED VIEW IF EXISTS before CREATE.
                else if (trimmed.StartsWith("CREATE MATERIALIZED VIEW", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(trimmed, @"CREATE\s+MATERIALIZED\s+VIEW\s+(\S+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string viewName = match.Groups[1].Value;
                        result.AppendLine($"DROP MATERIALIZED VIEW IF EXISTS {viewName};");
                    }
                }
                // 12. TYPE (Enum) -> Type does not support CREATE OR REPLACE or IF NOT EXISTS in old versions.
                // We wrap it in a check.
                else if (trimmed.StartsWith("CREATE TYPE", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("AS ENUM", StringComparison.OrdinalIgnoreCase))
                {
                    // Syntax: CREATE TYPE schema.name AS ENUM ('val1', 'val2');
                    var match = Regex.Match(trimmed, @"CREATE\s+TYPE\s+(\S+)\s+AS\s+ENUM\s*\((.*?)\);", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string typeName = match.Groups[1].Value;
                        string enumValues = match.Groups[2].Value;
                        line = $@"DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON t.typnamespace = n.oid WHERE t.typname = '{typeName.Split('.').Last().Replace("\"", "")}') THEN
        CREATE TYPE {typeName} AS ENUM ({enumValues});
    END IF;
END $$;";
                    }
                }
                // 13. DOMAIN -> Wrap in DO $$ BEGIN IF NOT EXISTS
                else if (trimmed.StartsWith("CREATE DOMAIN", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(trimmed, @"CREATE\s+DOMAIN\s+(\S+)\s+AS\s+(.*)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string domainName = match.Groups[1].Value;
                        string domainDef = match.Groups[2].Value;
                        line = $@"DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON t.typnamespace = n.oid WHERE t.typname = '{domainName.Split('.').Last().Replace("\"", "")}') THEN
        CREATE DOMAIN {domainName} AS {domainDef}
    END IF;
END $$;";
                    }
                }
                // 14. POLICY -> DROP POLICY IF EXISTS followed by CREATE POLICY
                else if (trimmed.StartsWith("CREATE POLICY", StringComparison.OrdinalIgnoreCase))
                {
                    // Syntax: CREATE POLICY policy_name ON table_name ...
                    var match = Regex.Match(trimmed, @"CREATE\s+POLICY\s+(\w+)\s+ON\s+(\S+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string policyName = match.Groups[1].Value;
                        string tableName = match.Groups[2].Value;
                        result.AppendLine($"DROP POLICY IF EXISTS {policyName} ON {tableName};");
                    }
                }
                // 15. ROLE -> Wrap in DO $$ BEGIN IF NOT EXISTS
                else if (trimmed.StartsWith("CREATE ROLE", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(trimmed, @"CREATE\s+ROLE\s+""?(\w+)""?", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string roleName = match.Groups[1].Value;
                        line = $@"DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{roleName}') THEN
        {trimmed}
    END IF;
END $$;";
                    }
                }

                result.AppendLine(line);
            }

            return result.ToString();
        }

        public static string TuneDataScript(string sql, string targetSchema)
        {
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            var lines = sql.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new StringBuilder();
            var tableSequencesToSync = new HashSet<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                // 1. Detect INSERT and verify it has ON CONFLICT
                if (trimmed.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse table name
                    var match = Regex.Match(trimmed, @"INSERT\s+INTO\s+(\S+)\s*\(", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string fullTableName = match.Groups[1].Value.Replace("\"", "");
                        string tableNameOnly = fullTableName.Split('.').Last();
                        tableSequencesToSync.Add(tableNameOnly);

                        // If it doesn't have ON CONFLICT already, we can add DO NOTHING as a generic safe fallback
                        if (!trimmed.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase))
                        {
                            // We need to know the primary keys to make ON CONFLICT work correctly, 
                            // but since this is raw text processing, we check if we can add a fallback or if Use UPSERT handles it.
                            // If Use UPSERT is not checked or not possible, DO NOTHING requires knowing conflict columns.
                            // In PostgreSQL, for general ID insertions, we assume 'id' as typical conflict column if it is present in the columns:
                            var colsMatch = Regex.Match(trimmed, @"\((.*?)\)\s*VALUES", RegexOptions.IgnoreCase);
                            if (colsMatch.Success)
                            {
                                var cols = colsMatch.Groups[1].Value.Split(',').Select(c => c.Trim().Replace("\"", ""));
                                if (cols.Contains("id"))
                                {
                                    // Add ON CONFLICT (id) DO NOTHING to prevent duplicate key violations
                                    line = line.TrimEnd(';');
                                    line += " ON CONFLICT (id) DO NOTHING;";
                                }
                            }
                        }
                    }
                }

                result.AppendLine(line);
            }

            // 2. Add Sequence Sync queries at the end of the script to prevent future auto-increment conflicts
            if (tableSequencesToSync.Count > 0)
            {
                result.AppendLine();
                result.AppendLine("-- === STEP 3: SYNC AUTO-INCREMENT SEQUENCES ===");
                foreach (var table in tableSequencesToSync)
                {
                    result.AppendLine($"SELECT setval(pg_get_serial_sequence('{targetSchema}.{table}', 'id'), COALESCE(max(id), 1)) FROM {targetSchema}.\"{table}\";");
                }
            }

            return result.ToString();
        }

        public static string StripComments(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return sql;
            var result = new StringBuilder();
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            string dollarTag = null;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (dollarTag != null)
                {
                    result.Append(c);
                    if (c == '$' && i >= dollarTag.Length - 1)
                    {
                        string possibleEnd = sql.Substring(i - dollarTag.Length + 1, dollarTag.Length);
                        if (possibleEnd == dollarTag)
                        {
                            dollarTag = null;
                        }
                    }
                    continue;
                }

                if (inSingleQuote)
                {
                    result.Append(c);
                    if (c == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            result.Append('\'');
                            i++;
                        }
                        else
                        {
                            inSingleQuote = false;
                        }
                    }
                    continue;
                }

                if (inDoubleQuote)
                {
                    result.Append(c);
                    if (c == '"')
                    {
                        inDoubleQuote = false;
                    }
                    continue;
                }

                // Check for line comment --
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    // Find end of line
                    int eol = sql.IndexOf('\n', i + 2);
                    if (eol == -1)
                    {
                        break; // End of string
                    }
                    i = eol; // Skip to new line (c will become \n or we skip it)
                    result.Append('\n');
                    continue;
                }

                // Check for block comment /*
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    // Find matching */ (respect nesting)
                    int nestLevel = 1;
                    i += 2;
                    while (i < sql.Length)
                    {
                        if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
                        {
                            nestLevel++;
                            i += 2;
                        }
                        else if (i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/')
                        {
                            nestLevel--;
                            i += 2;
                            if (nestLevel == 0)
                            {
                                break;
                            }
                        }
                        else
                        {
                            i++;
                        }
                    }
                    i--; // Adjust index for loop increment
                    continue;
                }

                // Check for dollar tag
                if (c == '$')
                {
                    int nextDollar = sql.IndexOf('$', i + 1);
                    if (nextDollar > i && nextDollar - i < 50)
                    {
                        string tag = sql.Substring(i, nextDollar - i + 1);
                        bool isValidTag = true;
                        for (int j = 1; j < tag.Length - 1; j++)
                        {
                            char tc = tag[j];
                            if (!char.IsLetterOrDigit(tc) && tc != '_')
                            {
                                isValidTag = false;
                                break;
                            }
                        }
                        if (isValidTag)
                        {
                            dollarTag = tag;
                            result.Append(tag);
                            i = nextDollar;
                            continue;
                        }
                    }
                }

                if (c == '\'')
                {
                    inSingleQuote = true;
                }
                else if (c == '"')
                {
                    inDoubleQuote = true;
                }

                result.Append(c);
            }

            return result.ToString();
        }

        public static List<string> SplitSqlStatements(string sql)
        {
            var statements = new List<string>();
            if (string.IsNullOrWhiteSpace(sql)) return statements;
            var current = new StringBuilder();
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            string dollarTag = null;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (dollarTag != null)
                {
                    current.Append(c);
                    if (c == '$' && i >= dollarTag.Length - 1)
                    {
                        string possibleEnd = sql.Substring(i - dollarTag.Length + 1, dollarTag.Length);
                        if (possibleEnd == dollarTag)
                        {
                            dollarTag = null;
                        }
                    }
                    continue;
                }

                if (inSingleQuote)
                {
                    current.Append(c);
                    if (c == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            current.Append('\'');
                            i++;
                        }
                        else
                        {
                            inSingleQuote = false;
                        }
                    }
                    continue;
                }

                if (inDoubleQuote)
                {
                    current.Append(c);
                    if (c == '"')
                    {
                        inDoubleQuote = false;
                    }
                    continue;
                }

                // Check for start of dollar tag
                if (c == '$')
                {
                    int nextDollar = sql.IndexOf('$', i + 1);
                    if (nextDollar > i && nextDollar - i < 50)
                    {
                        string tag = sql.Substring(i, nextDollar - i + 1);
                        bool isValidTag = true;
                        for (int j = 1; j < tag.Length - 1; j++)
                        {
                            char tc = tag[j];
                            if (!char.IsLetterOrDigit(tc) && tc != '_')
                            {
                                isValidTag = false;
                                break;
                            }
                        }
                        if (isValidTag)
                        {
                            dollarTag = tag;
                            current.Append(tag);
                            i = nextDollar;
                            continue;
                        }
                    }
                }

                if (c == '\'')
                {
                    inSingleQuote = true;
                    current.Append(c);
                    continue;
                }

                if (c == '"')
                {
                    inDoubleQuote = true;
                    current.Append(c);
                    continue;
                }

                if (c == ';')
                {
                    current.Append(c);
                    statements.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                string rem = current.ToString();
                if (!string.IsNullOrWhiteSpace(rem))
                {
                    statements.Add(rem);
                }
            }

            return statements;
        }

        public static string GetOnlyComments(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "";
            var result = new StringBuilder();
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            string dollarTag = null;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (dollarTag != null)
                {
                    if (c == '$' && i >= dollarTag.Length - 1)
                    {
                        string possibleEnd = sql.Substring(i - dollarTag.Length + 1, dollarTag.Length);
                        if (possibleEnd == dollarTag)
                        {
                            dollarTag = null;
                        }
                    }
                    continue;
                }

                if (inSingleQuote)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inSingleQuote = false;
                        }
                    }
                    continue;
                }

                if (inDoubleQuote)
                {
                    if (c == '"')
                    {
                        inDoubleQuote = false;
                    }
                    continue;
                }

                // Check for line comment --
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    int eol = sql.IndexOf('\n', i + 2);
                    if (eol == -1)
                    {
                        result.Append(sql.Substring(i));
                        break;
                    }
                    result.Append(sql.Substring(i, eol - i + 1));
                    i = eol;
                    continue;
                }

                // Check for block comment /*
                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    int start = i;
                    int nestLevel = 1;
                    i += 2;
                    while (i < sql.Length)
                    {
                        if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
                        {
                            nestLevel++;
                            i += 2;
                        }
                        else if (i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/')
                        {
                            nestLevel--;
                            i += 2;
                            if (nestLevel == 0)
                            {
                                break;
                            }
                        }
                        else
                        {
                            i++;
                        }
                    }
                    result.Append(sql.Substring(start, i - start));
                    i--; // Adjust index
                    continue;
                }

                // Check for dollar tag
                if (c == '$')
                {
                    int nextDollar = sql.IndexOf('$', i + 1);
                    if (nextDollar > i && nextDollar - i < 50)
                    {
                        string tag = sql.Substring(i, nextDollar - i + 1);
                        bool isValidTag = true;
                        for (int j = 1; j < tag.Length - 1; j++)
                        {
                            char tc = tag[j];
                            if (!char.IsLetterOrDigit(tc) && tc != '_')
                            {
                                isValidTag = false;
                                break;
                            }
                        }
                        if (isValidTag)
                        {
                            dollarTag = tag;
                            i = nextDollar;
                            continue;
                        }
                    }
                }

                if (c == '\'')
                {
                    inSingleQuote = true;
                }
                else if (c == '"')
                {
                    inDoubleQuote = true;
                }
            }

            return result.ToString().TrimEnd();
        }

        private static string GetCleanSql(string stmt)
        {
            string clean = StripComments(stmt);
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            return clean;
        }

        public static ConvertScriptResult ConvertScript(
            string sql,
            bool tuneScript,
            string targetSchema,
            bool ignoreOwner,
            bool ignorePrivileges,
            bool ignoreTablespaces,
            bool ignoreComments,
            bool ignorePublications,
            bool ignoreSubscriptions,
            bool ignoreSecurityLabels,
            bool ignoreTableAccessMethods,
            bool ignoreData,
            bool ignoreSchema,
            bool ignoreTransaction)
        {
            var result = new ConvertScriptResult();
            if (string.IsNullOrWhiteSpace(sql))
            {
                result.ConvertedSql = sql ?? "";
                return result;
            }

            // 1. Line-by-line preprocessing for data inserts and copies
            if (ignoreData)
            {
                var lines = sql.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var nonDataLines = new List<string>();
                bool inCopy = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmedLine = line.Trim();

                    if (inCopy)
                    {
                        if (trimmedLine == "\\.")
                        {
                            inCopy = false;
                        }
                        continue;
                    }

                    if (trimmedLine.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase) && trimmedLine.Contains("FROM stdin", StringComparison.OrdinalIgnoreCase))
                    {
                        inCopy = true;
                        continue;
                    }

                    if (trimmedLine.StartsWith("INSERT INTO ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!trimmedLine.EndsWith(";"))
                        {
                            while (i + 1 < lines.Length && !lines[i].Trim().EndsWith(";"))
                            {
                                i++;
                            }
                        }
                        continue;
                    }

                    nonDataLines.Add(line);
                }
                sql = string.Join("\n", nonDataLines);
            }

            // 2. Split original statements BEFORE stripping comments (to use for counting/logging)
            var originalStatements = SplitSqlStatements(sql);
            result.TotalOriginal = originalStatements.Count(s => !string.IsNullOrWhiteSpace(s));

            // 3. Strip comments (inline and block) if requested - operate on a working copy
            string sqlToProcess = ignoreComments ? StripComments(sql) : sql;

            // 4. Split to statements and filter them
            var rawStatements = SplitSqlStatements(sqlToProcess);
            // Also keep original statements (before stripping) for logging purposes
            var rawOriginalStatements = SplitSqlStatements(sql);
            var filteredStatements = new List<string>();

            // Build index maps: we match by position
            int stmtIdx = 0;
            // Use original raw (pre-strip) for tracking statement numbers
            // We need to iterate original (no-strip) to find comment-only stmts
            int origIdx = 0;
            int processedIdx = 0;

            // Create parallel lists from original (no-stripped) vs processed (stripped) statements
            // that are non-empty after trimming
            var origNonEmpty = rawOriginalStatements
                .Select((s, i) => (stmt: s, origPos: i))
                .Where(x => !string.IsNullOrWhiteSpace(x.stmt))
                .ToList();

            var procNonEmpty = rawStatements
                .Select((s, i) => (stmt: s, origPos: i))
                .Where(x => !string.IsNullOrWhiteSpace(x.stmt))
                .ToList();

            // Track which original statement indices have been matched
            int pIdx = 0;

            for (int oIdx = 0; oIdx < origNonEmpty.Count; oIdx++)
            {
                var origStmt = origNonEmpty[oIdx].stmt;
                var origTrimmed = origStmt.Trim();
                int stmtNumber = oIdx + 1;

                // Check if this original statement becomes empty after stripping comments
                string cleanedStmt = ignoreComments ? StripComments(origStmt) : origStmt;
                string cleanedTrimmed = cleanedStmt.Trim();

                if (ignoreComments && string.IsNullOrWhiteSpace(cleanedTrimmed))
                {
                    // This was a comment-only statement
                    result.IgnoredDetails.Add($"- Statement #{stmtNumber}: Comment statement removed ({origTrimmed.Replace("\r", "").Replace("\n", " ")})");
                    continue;
                }

                string clean = GetCleanSql(cleanedStmt);
                if (string.IsNullOrEmpty(clean)) continue;

                string ignoreReason = null;

                // Check Transactions
                if (ignoreTransaction)
                {
                    var cleanUpper = clean.Trim(';').Trim().ToUpper();
                    if (cleanUpper == "BEGIN" || cleanUpper == "BEGIN TRANSACTION" || cleanUpper == "START TRANSACTION" ||
                        cleanUpper == "COMMIT" || cleanUpper == "COMMIT TRANSACTION" || cleanUpper == "END" ||
                        cleanUpper == "BEGIN WORK" || cleanUpper == "COMMIT WORK")
                    {
                        ignoreReason = "Transaction statement removed";
                    }
                }

                // Check OWNER TO
                if (ignoreReason == null && ignoreOwner && Regex.IsMatch(clean, @"^ALTER\s+.*?\s+OWNER\s+TO\b", RegexOptions.IgnoreCase))
                    ignoreReason = "OWNER TO statement removed";

                // Check Privileges
                if (ignoreReason == null && ignorePrivileges && Regex.IsMatch(clean, @"^(GRANT|REVOKE)\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Privilege statement removed";

                // Check COMMENT ON (SQL COMMENT ON ... statement)
                if (ignoreReason == null && ignoreComments && Regex.IsMatch(clean, @"^COMMENT\s+ON\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Comment statement removed";

                // Check Publications
                if (ignoreReason == null && ignorePublications && Regex.IsMatch(clean, @"^(CREATE|ALTER|DROP)\s+PUBLICATION\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Publication statement removed";

                // Check Subscriptions
                if (ignoreReason == null && ignoreSubscriptions && Regex.IsMatch(clean, @"^(CREATE|ALTER|DROP)\s+SUBSCRIPTION\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Subscription statement removed";

                // Check Security Labels
                if (ignoreReason == null && ignoreSecurityLabels && Regex.IsMatch(clean, @"^SECURITY\s+LABEL\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Security Label statement removed";

                // Check Schema
                if (ignoreReason == null && ignoreSchema && Regex.IsMatch(clean, @"^(CREATE|ALTER|DROP)\s+SCHEMA\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Schema statement removed";

                // Check Tablespaces alter
                if (ignoreReason == null && ignoreTablespaces && Regex.IsMatch(clean, @"^ALTER\s+.*?\s+SET\s+TABLESPACE\b", RegexOptions.IgnoreCase))
                    ignoreReason = "Tablespace alter statement removed";

                if (ignoreReason != null)
                {
                    if (ignoreComments)
                    {
                        // Log inline comments FIRST (they appear before the statement in the file)
                        string origComments = GetOnlyComments(origStmt);
                        if (!string.IsNullOrWhiteSpace(origComments))
                        {
                            string commentPreview = origComments.Replace("\r", "").Replace("\n", " ").Trim();
                            if (commentPreview.Length > 120) commentPreview = commentPreview.Substring(0, 120) + "...";
                            result.IgnoredDetails.Add($"- Statement #{stmtNumber}: Ignore Comments: inline comments stripped ({commentPreview})");
                        }
                    }
                    result.IgnoredDetails.Add($"- Statement #{stmtNumber}: {ignoreReason} ({clean})");
                    if (!ignoreComments)
                    {
                        // Comments are NOT being stripped - preserve them in output
                        string comments = GetOnlyComments(origStmt);
                        if (!string.IsNullOrWhiteSpace(comments))
                        {
                            filteredStatements.Add(comments);
                            result.IgnoredDetails.Add($"- Statement #{stmtNumber}: Comments preserved (Ignore Comments is OFF)");
                        }
                    }
                    continue;
                }

                // Not ignored - process it
                string processedStmt = cleanedStmt;

                // Inline Tablespaces
                if (ignoreTablespaces)
                {
                    processedStmt = Regex.Replace(processedStmt, @"\bTABLESPACE\s+(?:\w+|""[^""]+"")", "", RegexOptions.IgnoreCase);
                    if (Regex.IsMatch(origTrimmed, @"^\s*ALTER\s+.*?\s+SET\s+TABLESPACE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        result.IgnoredDetails.Add($"- Statement #{stmtNumber}: Tablespace alter statement removed ({origTrimmed})");
                        continue;
                    }
                }

                // Table Access Methods
                if (ignoreTableAccessMethods)
                {
                    if (origTrimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        processedStmt = Regex.Replace(processedStmt, @"\bUSING\s+(?:heap|ao_row|ao_column|column|brin|gin|gist|hash|spgist|btree)\b", "", RegexOptions.IgnoreCase);
                    }
                }

                string finalTrimmed = processedStmt.Trim();
                if (string.IsNullOrEmpty(finalTrimmed))
                {
                    result.IgnoredDetails.Add($"- Statement #{stmtNumber}: Statement became empty after processing.");
                    continue;
                }

                // Compare to detect modifications
                string normOriginal = Regex.Replace(origTrimmed, @"\s+", " ").Trim();
                string normProcessed = Regex.Replace(finalTrimmed, @"\s+", " ").Trim();

                if (normOriginal != normProcessed)
                {
                    string commentNote2 = (ignoreComments && StripComments(origTrimmed) != origTrimmed) ? " (comments removed)" : "";
                    result.ModifiedDetails.Add($"[Statement #{stmtNumber}]{commentNote2}\n  BEFORE: {origTrimmed.Replace("\r", "").Replace("\n", "\n          ")}\n  AFTER:  {finalTrimmed.Replace("\r", "").Replace("\n", "\n          ")}");
                }
                else
                {
                    result.UnchangedCount++;
                }

                filteredStatements.Add(processedStmt);
            }

            var resultSql = string.Join("", filteredStatements);

            // Run tuning if requested
            if (tuneScript)
            {
                resultSql = TuneSchemaScript(resultSql, targetSchema);
            }

            result.ConvertedSql = resultSql;
            return result;
        }

        public static string AnalyzeScript(
            string sql,
            bool tuneScript,
            string targetSchema,
            bool ignoreOwner,
            bool ignorePrivileges,
            bool ignoreTablespaces,
            bool ignoreComments,
            bool ignorePublications,
            bool ignoreSubscriptions,
            bool ignoreSecurityLabels,
            bool ignoreTableAccessMethods,
            bool ignoreData,
            bool ignoreSchema,
            bool ignoreTransaction)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "Script is empty.";

            var rawStatements = SplitSqlStatements(sql);
            var ignoredDetails = new List<string>();
            var modifiedDetails = new List<string>();
            int unchangedCount = 0;
            int totalOriginal = rawStatements.Count;

            for (int idx = 0; idx < rawStatements.Count; idx++)
            {
                var stmt = rawStatements[idx];
                var trimmed = stmt.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                string clean = GetCleanSql(stmt);
                if (string.IsNullOrEmpty(clean))
                {
                    if (ignoreComments && !string.IsNullOrEmpty(trimmed))
                    {
                        ignoredDetails.Add($"- Statement #{idx + 1}: Comment statement removed ({trimmed.Replace("\r", "").Replace("\n", " ")})");
                    }
                    continue;
                }

                string ignoreReason = null;

                // 1. Check Transaction
                if (ignoreTransaction)
                {
                    var cleanUpper = clean.Trim(';').Trim().ToUpper();
                    if (cleanUpper == "BEGIN" || cleanUpper == "BEGIN TRANSACTION" || cleanUpper == "START TRANSACTION" || 
                        cleanUpper == "COMMIT" || cleanUpper == "COMMIT TRANSACTION" || cleanUpper == "END" ||
                        cleanUpper == "BEGIN WORK" || cleanUpper == "COMMIT WORK")
                    {
                        ignoreReason = "Transaction statement removed";
                    }
                }

                // 2. Check Data
                if (ignoreReason == null && ignoreData)
                {
                    if (clean.StartsWith("INSERT INTO ", StringComparison.OrdinalIgnoreCase) || 
                        clean.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("\\."))
                    {
                        ignoreReason = "Data/Insert statement removed";
                    }
                }

                // 3. Check OWNER TO
                if (ignoreReason == null && ignoreOwner && Regex.IsMatch(clean, @"^ALTER\s+.*?\s+OWNER\s+TO\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "OWNER TO statement removed";
                }

                // 4. Check Privileges
                if (ignoreReason == null && ignorePrivileges && Regex.IsMatch(clean, @"^(GRANT|REVOKE)\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Privilege statement removed";
                }

                // 5. Check COMMENT ON
                if (ignoreReason == null && ignoreComments && Regex.IsMatch(clean, @"^COMMENT\s+ON\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Comment statement removed";
                }

                // 6. Check Publications
                if (ignoreReason == null && ignorePublications && Regex.IsMatch(clean, @"^(CREATE|ALTER|DROP)\s+PUBLICATION\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Publication statement removed";
                }

                // 7. Check Subscriptions
                if (ignoreReason == null && ignoreSubscriptions && Regex.IsMatch(clean, @"^(CREATE|ALTER|DROP)\s+SUBSCRIPTION\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Subscription statement removed";
                }

                // 8. Check Security Labels
                if (ignoreReason == null && ignoreSecurityLabels && Regex.IsMatch(clean, @"^SECURITY\s+LABEL\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Security Label statement removed";
                }

                // 9. Check Schema
                if (ignoreReason == null && ignoreSchema && Regex.IsMatch(clean, @"^(CREATE|ALTER|DROP)\s+SCHEMA\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Schema statement removed";
                }

                // 10. Check Tablespaces alter
                if (ignoreReason == null && ignoreTablespaces && Regex.IsMatch(clean, @"^ALTER\s+.*?\s+SET\s+TABLESPACE\b", RegexOptions.IgnoreCase))
                {
                    ignoreReason = "Tablespace alter statement removed";
                }

                if (ignoreReason != null)
                {
                    if (ignoreComments)
                    {
                        // Log inline comments FIRST (they appear before the statement in the file)
                        string origComments = GetOnlyComments(stmt);
                        if (!string.IsNullOrWhiteSpace(origComments))
                        {
                            string commentPreview = origComments.Replace("\r", "").Replace("\n", " ").Trim();
                            if (commentPreview.Length > 120) commentPreview = commentPreview.Substring(0, 120) + "...";
                            ignoredDetails.Add($"- Statement #{idx + 1}: Ignore Comments: inline comments stripped ({commentPreview})");
                        }
                    }
                    ignoredDetails.Add($"- Statement #{idx + 1}: {ignoreReason} ({clean})");
                    if (!ignoreComments)
                    {
                        // Comments are NOT being stripped - note they are preserved
                        string comments = GetOnlyComments(stmt);
                        if (!string.IsNullOrWhiteSpace(comments))
                        {
                            ignoredDetails.Add($"- Statement #{idx + 1}: Comments preserved (Ignore Comments is OFF)");
                        }
                    }
                    continue;
                }

                // If not ignored, process
                string processedStmt = stmt;

                // Strip comments if requested
                if (ignoreComments)
                {
                    processedStmt = StripComments(processedStmt);
                }

                // Check Tablespaces
                if (ignoreTablespaces)
                {
                    processedStmt = Regex.Replace(processedStmt, @"\bTABLESPACE\s+(?:\w+|""[^""]+"")", "", RegexOptions.IgnoreCase);
                    if (Regex.IsMatch(trimmed, @"^\s*ALTER\s+.*?\s+SET\s+TABLESPACE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        ignoredDetails.Add($"- Statement #{idx + 1}: Tablespace alter statement removed ({trimmed})");
                        continue;
                    }
                }

                // Check Table Access Methods
                if (ignoreTableAccessMethods)
                {
                    if (trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        processedStmt = Regex.Replace(processedStmt, @"\bUSING\s+(?:heap|ao_row|ao_column|column|brin|gin|gist|hash|spgist|btree)\b", "", RegexOptions.IgnoreCase);
                    }
                }

                // Run tuning if requested
                if (tuneScript)
                {
                    processedStmt = TuneSchemaScript(processedStmt, targetSchema);
                }

                string finalTrimmed = processedStmt.Trim();
                if (string.IsNullOrEmpty(finalTrimmed))
                {
                    ignoredDetails.Add($"- Statement #{idx + 1}: Statement became empty after processing.");
                    continue;
                }

                // Compare trimmed versions
                string normOriginal = Regex.Replace(trimmed, @"\s+", " ").Trim();
                string normProcessed = Regex.Replace(finalTrimmed, @"\s+", " ").Trim();

                if (normOriginal != normProcessed)
                {
                    string commentNote = "";
                    if (ignoreComments && StripComments(trimmed) != trimmed)
                    {
                        commentNote = " (comments removed)";
                    }
                    modifiedDetails.Add($"[Statement #{idx + 1}]{commentNote}\n  BEFORE: {trimmed.Replace("\r", "").Replace("\n", "\n          ")}\n  AFTER:  {finalTrimmed.Replace("\r", "").Replace("\n", "\n          ")}");
                }
                else
                {
                    unchangedCount++;
                }
            }

            var report = new StringBuilder();
            report.AppendLine("================================================================================");
            report.AppendLine("                       SQL SCRIPT ANALYSIS REPORT (DRY RUN)");
            report.AppendLine("================================================================================");
            report.AppendLine();
            report.AppendLine("SUMMARY:");
            report.AppendLine($"- Total original statements:   {totalOriginal}");
            report.AppendLine($"- Unchanged statements:        {unchangedCount}");
            report.AppendLine($"- Modified statements:         {modifiedDetails.Count}");
            report.AppendLine($"- Ignored (removed) statements: {ignoredDetails.Count}");
            report.AppendLine();

            if (ignoredDetails.Count > 0)
            {
                report.AppendLine("--------------------------------------------------------------------------------");
                report.AppendLine($"IGNORED STATEMENTS ({ignoredDetails.Count}):");
                report.AppendLine("--------------------------------------------------------------------------------");
                foreach (var detail in ignoredDetails)
                {
                    report.AppendLine(detail);
                }
                report.AppendLine();
            }

            if (modifiedDetails.Count > 0)
            {
                report.AppendLine("--------------------------------------------------------------------------------");
                report.AppendLine($"MODIFIED STATEMENTS ({modifiedDetails.Count}):");
                report.AppendLine("--------------------------------------------------------------------------------");
                foreach (var detail in modifiedDetails)
                {
                    report.AppendLine(detail);
                    report.AppendLine();
                }
            }

            if (ignoredDetails.Count == 0 && modifiedDetails.Count == 0)
            {
                report.AppendLine("No changes detected. The script is identical to the converted output.");
            }

            return report.ToString();
        }
    }
}
