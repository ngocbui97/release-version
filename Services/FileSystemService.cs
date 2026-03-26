namespace ReleasePrepTool.Services;

public class FileSystemService
{
    public string BaseReleasePath { get; private set; }

    public FileSystemService(string basePath, string version, string productName = "")
    {
        var productStr = string.IsNullOrWhiteSpace(productName) ? "product" : productName;
        BaseReleasePath = Path.Combine(basePath, $"{productStr}_version_{version}");
    }

    public void EnsureDirectoryStructure()
    {
        Directory.CreateDirectory(BaseReleasePath);
        
        var databaseDir = Path.Combine(BaseReleasePath, "database");
        Directory.CreateDirectory(Path.Combine(databaseDir, "backup"));
        Directory.CreateDirectory(Path.Combine(databaseDir, "script_full"));
        
        var scriptUpdateDir = Path.Combine(databaseDir, "script_update");
        Directory.CreateDirectory(Path.Combine(scriptUpdateDir, "schema"));
        Directory.CreateDirectory(Path.Combine(scriptUpdateDir, "data"));

        Directory.CreateDirectory(Path.Combine(BaseReleasePath, "source_code"));
    }

    public string GetSqlScriptPath(string databaseName, bool isSchema)
    {
        var typeStr = isSchema ? "schema" : "data";
        return Path.Combine(BaseReleasePath, "database", "script_update", typeStr, $"{databaseName}_{typeStr}.sql");
    }

    public string GetFullScriptPath(string databaseName)
    {
        return Path.Combine(BaseReleasePath, "database", "script_full", $"{databaseName}_full.sql");
    }

    public string GetBackupPath(string databaseName)
    {
        return Path.Combine(BaseReleasePath, "database", "backup", $"{databaseName}.backup");
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
}
