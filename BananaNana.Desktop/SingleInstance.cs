using System.Net.Sockets;
using System.Text;
namespace BananaNana.Desktop;

// One running copy. A second launch sends "show" over a Unix socket in $XDG_RUNTIME_DIR and exits; the first
// copy brings its window forward. Nothing here can stop the app from starting: if the socket cannot be used, the app runs anyway.
public sealed class SingleInstance : IDisposable
{
    const string Command = "show";
    readonly Socket listener;
    volatile Action? onShow;

    SingleInstance(Socket listener) { this.listener = listener; }

    public static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath(),
            "banananana.sock");

    // True when another copy answered and was asked to show its window.
    public static bool TryNotifyExisting()
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            client.Connect(new UnixDomainSocketEndPoint(SocketPath));
            client.Send(Encoding.ASCII.GetBytes(Command + "\n"));
            return true;
        }
        catch (SocketException)
        {
            return false; // nobody is listening (a stale socket file is replaced below)
        }
    }

    // Null when the socket cannot be created; the app then runs without single-instance support.
    public static SingleInstance? TryListen()
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            File.Delete(SocketPath);
            socket.Bind(new UnixDomainSocketEndPoint(SocketPath));
            socket.Listen(4);
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            socket.Dispose();
            Console.Error.WriteLine($"Single-instance socket unavailable: {e.Message}");
            return null;
        }
        var instance = new SingleInstance(socket);
        _ = Task.Run(instance.Serve);
        return instance;
    }

    // Called on a background thread when a second copy asks for the window.
    public Action? OnShow { get => onShow; set => onShow = value; }

    async Task Serve()
    {
        while (true)
        {
            Socket client;
            try { client = await listener.AcceptAsync(); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    void Handle(Socket client)
    {
        try
        {
            var buffer = new byte[32];
            int read = client.Receive(buffer);
            if (Encoding.ASCII.GetString(buffer, 0, read).StartsWith(Command, StringComparison.Ordinal)) OnShow?.Invoke();
        }
        catch (SocketException) { }
        finally { client.Dispose(); }
    }

    public void Dispose()
    {
        listener.Dispose();
        try { File.Delete(SocketPath); } catch (IOException) { }
    }
}
