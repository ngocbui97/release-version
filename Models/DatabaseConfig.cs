namespace ReleasePrepTool.Models;

public class DatabaseConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public bool IsValid { get; set; } = false;

    public string GetConnectionString()
    {
        return $"Host={Host};Port={Port};Username={Username};Password={Password};Database={DatabaseName}";
    }
}
