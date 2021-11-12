using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FileSystemWatcherHelper;

class EventProcessor
{
    const int EVENT_DELAY = 50; // aggregate and only emit events when changes have stopped for this duration (in ms)
    const int EVENT_SPAM_WARNING_THRESHOLD = 60 * 1000 * 10000; // warn after certain time span of event spam (in ticks)

    readonly object LOCK = new();
    Task? delayTask;

    readonly List<FileSystemWatcherEventArgs> events = new();
    readonly Action<FileSystemWatcherEventArgs> handleEvent;

    readonly Action<string> logger;

    long lastEventTime;
    long delayStarted;

    long spamCheckStartTime;
    bool spamWarningLogged;

    public EventProcessor(Action<FileSystemWatcherEventArgs> onEvent, Action<string> onLogging)
    {
        handleEvent = onEvent;
        logger = onLogging;
    }

    public void ProcessEvent(FileSystemWatcherEventArgs fileSystemEvent)
    {
        lock (LOCK)
        {
            var now = DateTime.Now.Ticks;

            // Check for spam
            if (events.Count == 0)
            {
                spamWarningLogged = false;
                spamCheckStartTime = now;
            }
            else if (!spamWarningLogged && spamCheckStartTime + EVENT_SPAM_WARNING_THRESHOLD < now)
            {
                spamWarningLogged = true;
                logger($"Warning: Watcher is busy catching up wit {events.Count} file changes in 60 seconds. Latest path is '{fileSystemEvent.Path}'");
            }

            // Add into our queue
            events.Add(fileSystemEvent);
            lastEventTime = now;

            // Process queue after delay
            if (delayTask != null)
                return;

            // Create function to buffer events
            void Func(Task value)
            {
                // Check if another event has been received in the meantime
                lock (LOCK)
                {
                    if (delayStarted == lastEventTime)
                    {
                        // Normalize and handle
                        var normalized = NormalizeEvents(events.ToArray());

                        foreach (var e in normalized)
                            handleEvent(e);

                        // Reset
                        events.Clear();
                        delayTask = null;
                    }

                    // Otherwise we have received a new event while this task was
                    // delayed and we reschedule it.
                    else
                    {
                        delayStarted = lastEventTime;
                        delayTask = Task.Delay(EVENT_DELAY).ContinueWith(Func);
                    }
                }
            }

            // Start function after delay
            delayStarted = lastEventTime;
            delayTask = Task.Delay(EVENT_DELAY).ContinueWith(Func);
        }
    }

    IEnumerable<FileSystemWatcherEventArgs> NormalizeEvents(FileSystemWatcherEventArgs[] es)
    {
        var mapPathToEvents = new Dictionary<string, FileSystemWatcherEventArgs>();
        var eventsWithoutDuplicates = new List<FileSystemWatcherEventArgs>();

        // Normalize Duplicates
        foreach (var e in es)
        {
            // Existing event
            if (mapPathToEvents.ContainsKey(e.Path))
            {
                var existingEvent = mapPathToEvents[e.Path];
                var currentChangeType = existingEvent.Action;
                var newChangeType = e.Action;

                switch (currentChangeType)
                {
                    // ignore CREATE followed by DELETE in one go
                    // flatten DELETE followed by CREATE into CHANGE
                    case FileSystemWatcherAction.Created when newChangeType == FileSystemWatcherAction.Deleted:

                        mapPathToEvents.Remove(existingEvent.Path);
                        eventsWithoutDuplicates.Remove(existingEvent);

                        break;

                    // Do nothing. Keep the created event
                    case FileSystemWatcherAction.Deleted when newChangeType == FileSystemWatcherAction.Created:

                        existingEvent.Action = FileSystemWatcherAction.Changed;

                        break;

                    // Otherwise apply change type
                    case FileSystemWatcherAction.Created when newChangeType == FileSystemWatcherAction.Changed:

                        break;

                    default:

                        existingEvent.Action = newChangeType;

                        break;
                }
            }

            // New event
            else
            {
                mapPathToEvents.Add(e.Path, e);
                eventsWithoutDuplicates.Add(e);
            }
        }

        // Handle deletes
        var addedChangeEvents = new List<FileSystemWatcherEventArgs>();
        var deletedPaths = new List<string>();

        // This algorithm will remove all DELETE events up to the root folder
        // that got deleted if any. This ensures that we are not producing
        // DELETE events for each file inside a folder that gets deleted.
        //
        // 1.) split ADD/CHANGE and DELETED events
        // 2.) sort short deleted paths to the top
        // 3.) for each DELETE, check if there is a deleted parent and ignore the event in that case

        return eventsWithoutDuplicates
            .Where(e =>
            {
                if (e.Action == FileSystemWatcherAction.Deleted)
                    return true;

                addedChangeEvents.Add(e);

                return false; // remove ADD / CHANGE
            })
            .OrderBy(e => e.Path.Length) // shortest path first
            .Where(e =>
            {
                if (deletedPaths.Any(d => IsParent(e.Path, d)))
                    return false; // DELETE is ignored if parent is deleted already

                // otherwise mark as deleted
                deletedPaths.Add(e.Path);

                return true;
            })
            .Concat(addedChangeEvents);
    }

    static bool IsParent(string p, string candidate)
    {
        return p.IndexOf(candidate + '\\', StringComparison.CurrentCultureIgnoreCase) == 0;
    }
}
