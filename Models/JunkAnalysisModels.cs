using System;
using System.Collections.Generic;

namespace ReleasePrepTool.Models;

public enum JunkType
{
    Schema,
    Table,
    View,
    Routine, // Function/Procedure
    DataRecord
}

public class JunkItem
{
    public string DatabaseName { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public string ObjectName { get; set; } = ""; // Table, View, or Schema Name itself
    public JunkType Type { get; set; }
    public string? ColumnName { get; set; } // Only for DataRecord
    public string? PrimaryKeyColumn { get; set; } // Only for DataRecord
    public string? PrimaryKeyValue { get; set; } // Only for DataRecord
    public string? DetectedContent { get; set; } // Why it was flagged
    public bool Selected { get; set; } = true;
    
    public string DisplayPath => Type switch
    {
        JunkType.Schema => $"Schema: {SchemaName}",
        JunkType.DataRecord => $"{SchemaName}.{ObjectName} > Row: {PrimaryKeyColumn}={PrimaryKeyValue}",
        _ => $"{SchemaName} > {Type}: {ObjectName}"
    };
}

public class JunkAnalysisResult
{
    public string DatabaseName { get; set; } = "";
    public List<JunkItem> Items { get; set; } = new();
}
