using System;

namespace FCCH.Diagnostics;

public struct LogEntry
{
    public DateTime First;
    public DateTime Last;
    public LogLevel Level;
    public string? Category;
    public string Message;
    public int Count;

    public LogEntry(LogLevel level, string message, string? category, DateTime now)
    {
        First = now;
        Last = now;
        Level = level;
        Category = category;
        Message = message;
        Count = 1;
    }
}
