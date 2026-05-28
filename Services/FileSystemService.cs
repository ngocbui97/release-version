namespace ReleasePrepTool.Services;

public class FileSystemService
{
    public string BaseReleasePath { get; private set; }
    public string Version { get; private set; }

    public FileSystemService(string basePath, string version, string productName = "")
    {
        Version = version;
        var productStr = string.IsNullOrWhiteSpace(productName) ? "product" : productName;
        BaseReleasePath = Path.Combine(basePath, $"{productStr}_version_{version}");
    }

    public void EnsureDirectoryStructure()
    {
        Directory.CreateDirectory(BaseReleasePath);
        
        var databaseDir = Path.Combine(BaseReleasePath, "db");
        Directory.CreateDirectory(Path.Combine(databaseDir, "backup"));
        Directory.CreateDirectory(Path.Combine(databaseDir, "script_full"));
        
        var scriptUpdateDir = Path.Combine(databaseDir, "script_update");
        Directory.CreateDirectory(Path.Combine(scriptUpdateDir, "schema"));
        Directory.CreateDirectory(Path.Combine(scriptUpdateDir, "data"));

        Directory.CreateDirectory(Path.Combine(BaseReleasePath, "source_code"));

        // Auto-generate note.txt inside script_update folder to guide running scripts in correct order
        UpdateScriptUpdateNote();
    }

    public void UpdateScriptUpdateNote()
    {
        var databaseDir = Path.Combine(BaseReleasePath, "db");
        var scriptUpdateDir = Path.Combine(databaseDir, "script_update");
        var schemaDir = Path.Combine(scriptUpdateDir, "schema");
        var dataDir = Path.Combine(scriptUpdateDir, "data");
        var guidePath = Path.Combine(scriptUpdateDir, "note.txt");

        var schemaFiles = Directory.Exists(schemaDir)
            ? Directory.GetFiles(schemaDir, "*.sql").Select(Path.GetFileName).OrderBy(f => f).ToList()
            : new List<string?>();

        var dataFiles = Directory.Exists(dataDir)
            ? Directory.GetFiles(dataDir, "*.sql").Select(Path.GetFileName).OrderBy(f => f).ToList()
            : new List<string?>();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Update database version {Version}");
        sb.AppendLine();
        sb.AppendLine("1, update schema script:");
        foreach (var file in schemaFiles)
        {
            if (!string.IsNullOrEmpty(file))
            {
                sb.AppendLine($" - {file}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("2, update data script:");
        foreach (var file in dataFiles)
        {
            if (!string.IsNullOrEmpty(file))
            {
                sb.AppendLine($" - {file}");
            }
        }

        Directory.CreateDirectory(scriptUpdateDir);
        File.WriteAllText(guidePath, sb.ToString());
    }

    public string GetSqlScriptPath(string databaseName, bool isSchema)
    {
        var typeStr = isSchema ? "schema" : "data";
        return Path.Combine(BaseReleasePath, "db", "script_update", typeStr, $"{databaseName}_{typeStr}.sql");
    }

    public string GetFullScriptPath(string databaseName)
    {
        return Path.Combine(BaseReleasePath, "db", "script_full", $"{databaseName}_full.sql");
    }

    public string GetBackupPath(string databaseName)
    {
        return Path.Combine(BaseReleasePath, "db", "backup", $"{databaseName}.backup");
    }

    public string GetNoteFilePath()
    {
        return Path.Combine(BaseReleasePath, "source_code", "note.txt");
    }

    public void WriteToFile(string path, string content)
    {
        File.WriteAllText(path, content);
    }
    
    public void AppendToFile(string path, string content)
    {
         File.AppendAllText(path, content + Environment.NewLine);
     }

    public string SaveSqlScript(string fileName, string content, bool isSchema)
    {
        var typeStr = isSchema ? "schema" : "data";
        var dir = Path.Combine(BaseReleasePath, "db", "script_update", typeStr);
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
