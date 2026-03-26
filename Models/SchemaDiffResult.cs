namespace ReleasePrepTool.Models;

public class SchemaDiffResult
{
    public string ObjectType { get; set; } // "Table", "View", "Routine", "Index", "Sequence", "Type"
    public string ObjectName { get; set; }
    public string SourceDDL { get; set; }
    public string TargetDDL { get; set; }
    public string DiffScript { get; set; }
    public string DiffType { get; set; } // "Added", "Removed", "Altered"
}
