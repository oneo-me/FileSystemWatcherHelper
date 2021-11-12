using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace FileSystemWatcherHelper;

public class FileSystemWatcher
{
    readonly BlockingCollection<FileSystemWatcherEventArgs> fileEventQueue = new();
    readonly EventProcessor processor;
    readonly Thread thread;
    readonly FileSystemWatcherCore watcherCore;

    public FileSystemWatcher(string watchPath)
    {
        // Event processor deals with buffering and normalization of events
        processor = new EventProcessor(e =>
        {
            Console.WriteLine("{0}|{1}", e.Action, e.Path);
        }, msg =>
        {
            Console.WriteLine("{0}", msg);
        });

        thread = new Thread(ThreadFunc) { IsBackground = true };

        watcherCore = new FileSystemWatcherCore(watchPath);
        watcherCore.Event += WatcherCoreOnEvent;
    }

    public void Start()
    {
        thread.Start();
        watcherCore.Start();
    }

    void WatcherCoreOnEvent(object? sender, FileSystemWatcherEventArgs e)
    {
        fileEventQueue.Add(e);
    }

    [SuppressMessage("ReSharper", "FunctionNeverReturns")]
    void ThreadFunc()
    {
        while (true)
        {
            var e = fileEventQueue.Take();
            processor.ProcessEvent(e);
        }
    }
}
