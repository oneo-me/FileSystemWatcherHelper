using System;
using System.IO;

namespace FileSystemWatcherHelper;

class FileSystemWatcherCore
{
    public event EventHandler<FileSystemWatcherEventArgs>? Event;
    public event EventHandler<Exception>? Error;

    readonly System.IO.FileSystemWatcher watcher;

    public FileSystemWatcherCore(string watchPath)
    {
        watcher = new System.IO.FileSystemWatcher
        {
            Path = watchPath, IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        watcher.Changed += (_, e) =>
        {
            OnEvent(FileSystemWatcherAction.Changed, e.FullPath);
        };

        watcher.Created += (_, e) =>
        {
            OnEvent(FileSystemWatcherAction.Created, e.FullPath);
        };

        watcher.Deleted += (_, e) =>
        {
            OnEvent(FileSystemWatcherAction.Deleted, e.FullPath);
        };

        watcher.Renamed += (_, e) =>
        {
            var newInPath = e.FullPath.StartsWith(watchPath);
            var oldInPath = e.OldFullPath.StartsWith(watchPath);

            if (newInPath)
                OnEvent(FileSystemWatcherAction.Created, e.FullPath);

            if (oldInPath)
                OnEvent(FileSystemWatcherAction.Deleted, e.OldFullPath);
        };

        watcher.Error += (_, e) =>
        {
            OnError(e.GetException());
        };

        watcher.InternalBufferSize = 32768; // changing this to a higher value can lead into issues when watching UNC drives
    }

    public void Start()
    {
        watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }

    void OnEvent(FileSystemWatcherAction action, string path)
    {
        Event?.Invoke(this, new FileSystemWatcherEventArgs(action, path));
    }

    void OnError(Exception e)
    {
        Error?.Invoke(this, e);
    }
}
