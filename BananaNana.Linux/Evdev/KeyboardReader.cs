using System.Buffers.Binary;

namespace BananaNana.Linux.Evdev;

// Reads one /dev/input/eventN on its own thread. The device is not grabbed, so other apps still get the keys.
// struct input_event on x86_64 is 24 bytes: timeval (16), u16 type, u16 code, s32 value (little endian).
public sealed class KeyboardReader : IDisposable
{
    const int EventSize = 24, EvKey = 1;
    const int ValueUp = 0, ValueDown = 1, ValueRepeat = 2;

    readonly FileStream stream;
    readonly Thread thread;
    volatile bool disposed;

    public string Node { get; }
    public string Name { get; }

    // isRepeat is true for auto-repeat (value 2). Raised on the reader thread.
    public event Action<int, bool>? KeyDown;
    public event Action<int>? KeyUp;
    // Raised once when the device stops working (unplugged, I/O error). Not raised after Dispose.
    public event Action<KeyboardReader, string>? Stopped;

    // Throws UnauthorizedAccessException when the user may not read the node (not in the 'input' group).
    public KeyboardReader(string node, string name)
    {
        Node = node;
        Name = name;
        // BufferSize 0 disables buffering so every Read returns whole events.
        stream = new FileStream(node, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = 0,
        });
        thread = new Thread(Run) { IsBackground = true, Name = "evdev " + node };
    }

    public void Start() => thread.Start();

    void Run()
    {
        var buf = new byte[EventSize];
        try
        {
            while (true)
            {
                stream.ReadExactly(buf);
                if (BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(16)) != EvKey) continue;
                int code = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(18));
                int value = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(20));
                switch (value)
                {
                    case ValueDown: KeyDown?.Invoke(code, false); break;
                    case ValueRepeat: KeyDown?.Invoke(code, true); break;
                    case ValueUp: KeyUp?.Invoke(code); break;
                }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException)
        {
            // ENODEV after an unplug shows up here.
            if (!disposed) Stopped?.Invoke(this, e.Message);
        }
        catch (ObjectDisposedException) when (disposed)
        {
            // Dispose closed the stream while we were reading.
        }
    }

    // Closing the stream does not interrupt a read already blocked in the kernel; the thread
    // exits on the next event or on unplug (its exception is then ignored).
    public void Dispose()
    {
        disposed = true;
        stream.Dispose();
    }
}
