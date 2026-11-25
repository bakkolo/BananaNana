namespace BananaNana.Linux.Evdev;

// Opens every keyboard and mouse under /dev/input and follows hotplug. Devices that fail are dropped.
public sealed class InputMonitor : IDisposable
{
    static readonly TimeSpan OpenRetry = TimeSpan.FromSeconds(1);
    readonly Action<string> log;
    readonly Action<int, bool> keyDown;
    readonly Action<int> keyUp;
    readonly Dictionary<string, KeyboardReader> readers = new(); // keyed by node path
    readonly HashSet<string> keyboardNodes = new(); // the subset of readers that are keyboards
    readonly object gate = new();
    FileSystemWatcher? watcher;
    bool disposed;

    // Keyboards being read right now (mice are not counted).
    public int KeyboardCount { get { lock (gate) return keyboardNodes.Count; } }

    public InputMonitor(Action<string> log, Action<int, bool> keyDown, Action<int> keyUp)
    {
        this.log = log;
        this.keyDown = keyDown;
        this.keyUp = keyUp;
    }

    public void Start()
    {
        foreach (var info in InputDevices.Enumerate()) Attach(info);

        // udev creates the node first and sets its permissions a moment later, so new nodes are retried.
        watcher = new FileSystemWatcher(InputDevices.DevDir, "event*") { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, e) => Task.Run(() => AttachWhenReady(e.Name ?? string.Empty));
        watcher.Deleted += (_, e) => Detach(e.FullPath);
        watcher.EnableRaisingEvents = true;
    }

    void Attach(InputDeviceInfo info)
    {
        if (info.Kind == DeviceKind.Other) return;
        if (Open(info) is { } error) log(error);
    }

    void AttachWhenReady(string eventName)
    {
        var deadline = DateTime.UtcNow + OpenRetry;
        while (true)
        {
            var info = InputDevices.Describe(eventName);
            if (info is { Kind: DeviceKind.Other }) return;
            if (info is not null)
            {
                var error = Open(info);
                if (error is null) return;
                if (DateTime.UtcNow >= deadline) { log(error); return; }
            }
            else if (DateTime.UtcNow >= deadline)
            {
                return;
            }
            Thread.Sleep(50);
        }
    }

    // Returns an error message when the device cannot be opened, else null.
    string? Open(InputDeviceInfo info)
    {
        lock (gate)
        {
            if (disposed || readers.ContainsKey(info.Node)) return null;
            KeyboardReader reader;
            try
            {
                reader = new KeyboardReader(info.Node, info.Name);
            }
            catch (UnauthorizedAccessException)
            {
                return $"Cannot read {info.Node}: permission denied. Add your user to the 'input' group (see docs/LINUX.md).";
            }
            catch (IOException e)
            {
                return $"Cannot open {info.Node}: {e.Message}";
            }
            reader.KeyDown += keyDown;
            reader.KeyUp += keyUp;
            reader.Stopped += OnStopped;
            readers[info.Node] = reader;
            if (info.Kind == DeviceKind.Keyboard) keyboardNodes.Add(info.Node);
            reader.Start();
            log($"{info.Kind} connected: {info.Name} ({info.Node})");
            return null;
        }
    }

    void Detach(string node)
    {
        KeyboardReader? reader;
        lock (gate)
        {
            readers.Remove(node, out reader);
            keyboardNodes.Remove(node);
        }
        if (reader is null) return;
        reader.Dispose();
        log($"disconnected: {reader.Name} ({node})");
    }

    void OnStopped(KeyboardReader reader, string reason)
    {
        bool removed;
        lock (gate)
        {
            removed = readers.Remove(reader.Node);
            keyboardNodes.Remove(reader.Node);
        }
        if (!removed) return; // Detach already handled it.
        reader.Dispose();
        log($"disconnected: {reader.Name} ({reader.Node}): {reason}");
    }

    public void Dispose()
    {
        List<KeyboardReader> toClose;
        lock (gate)
        {
            disposed = true;
            watcher?.Dispose();
            watcher = null;
            toClose = readers.Values.ToList();
            readers.Clear();
            keyboardNodes.Clear();
        }
        foreach (var r in toClose) r.Dispose();
    }
}
