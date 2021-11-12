using System;

namespace FileSystemWatcherHelper;

public class FileSystemWatcherEventArgs : EventArgs
{
    public FileSystemWatcherAction Action { get; set; }
    public string Path { get; }

    public FileSystemWatcherEventArgs(FileSystemWatcherAction action, string path)
    {
        Action = action;
        Path = path;
    }
}
