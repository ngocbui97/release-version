using System.Drawing;

namespace ReleasePrepTool.UI
{
    public static class UIConstants
    {
        // Modern Fluent UI Color Palette
        public static readonly Color Primary = Color.FromArgb(0, 120, 212);    // Microsoft Blue
        public static readonly Color PrimaryHover = Color.FromArgb(16, 110, 190);
        public static readonly Color Success = Color.FromArgb(16, 124, 16);     // Forest Green
        public static readonly Color Warning = Color.FromArgb(255, 140, 0);     // Orange
        public static readonly Color Danger = Color.FromArgb(216, 59, 1);       // Red-Orange
        public static readonly Color Surface = Color.FromArgb(243, 242, 241);   // Light Gray Surface
        public static readonly Color White = Color.White;
        public static readonly Color Border = Color.FromArgb(225, 223, 221);
        public static readonly Color TextPrimary = Color.FromArgb(50, 49, 48);
        public static readonly Color TextSecondary = Color.FromArgb(102, 101, 100);
        
        // Deep Night Console Colors
        public static readonly Color ConsoleBg = Color.FromArgb(37, 37, 38);
        public static readonly Color ConsoleFg = Color.FromArgb(212, 212, 212);

        // Fonts
        public static readonly string MainFontName = "Segoe UI Variable Display";
        public static readonly string IconFontName = "Segoe MDL2 Assets";

        // Icons (Unicode Segoe MDL2 Assets)
        public static string IconCheck = "\uE73E";      // Accept
        public static string IconError = "\uEA39";      // Error
        public static string IconRefresh = "\uE72C";    // Refresh
        public static string IconFolder = "\uED25";     // OpenFolder
        public static string IconSettings = "\uE713";   // Settings
        public static string IconCompare = "\uE8D1";    // Compare
        public static string IconSync = "\uE895";       // Sync
        public static string IconDatabase = "\uE7B7";   // Database
        public static string IconPlay = "\uE768";       // Play
        public static string IconExport = "\uE8AD";     // Updated for better compatibility
        public static string IconSearch = "\uE721";     // Search
        public static string IconTrash = "\uE74D";      // Delete
        public static string IconInfo = "\uE946";       // Info
        public static string IconSelectAll = "\uE762"; // SelectAll
        public static string IconClear = "\uE894";     // Clear
        public static string IconRobot = "\uE99A";     // Robot icon
        public static string IconEdit = "\uE70F";      // Edit icon
        public static string IconTimer = "\uE916";     // History/Clock icon
        public static string IconCopy = "\uE8C8";      // Copy icon

        // SQL Syntax Palette
        public static readonly Color SqlKeyword = Color.FromArgb(0, 120, 212);
        public static readonly Color SqlType = Color.FromArgb(43, 145, 175);
        public static readonly Color SqlComment = Color.FromArgb(1, 121, 52);
        public static readonly Color SqlString = Color.FromArgb(163, 21, 21);

        // Database Object Icons
        public static string IconTable = "\uE80A";     // Grid
        public static string IconView = "\uE890";      // Preview
        public static string IconFunction = "\uE943";  // Calculator
        public static string IconTrigger = "\uE945";   // Lightning
        public static string IconIndex = "\uE8EF";     // List
        public static string IconExtension = "\uE710"; // Pin
        public static string IconRole = "\uE77B";      // People
        public static string IconSequence = "\uE81C";  // Number list
    }
}
