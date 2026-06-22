namespace ReleasePrepTool.Models;

public class RestoreOptions
{
    public bool OnlyData { get; set; } = false;
    public bool OnlySchema { get; set; } = false;
    public bool NoOwner { get; set; } = false;
    public bool NoPrivileges { get; set; } = false;
    public bool NoTablespaces { get; set; } = false;
    public bool CleanBeforeRestore { get; set; } = false;
    public bool IncludeIfExists { get; set; } = true;
    public bool SingleTransaction { get; set; } = false;
    public bool DisableTriggers { get; set; } = false;
    public bool Verbose { get; set; } = true;
    public int NumberOfJobs { get; set; } = 1;
    public string Format { get; set; } = "Auto";
    public string RoleName { get; set; } = "";
    public string Section { get; set; } = "All";
    public bool IncludeCreateDb { get; set; } = false;
    public bool NoDataFailedTables { get; set; } = false;
    public bool ExitOnError { get; set; } = false;
    public bool UseSetSessionAuth { get; set; } = false;
    public System.Collections.Generic.List<string> ExtensionsToInstall { get; set; } = new();
}
