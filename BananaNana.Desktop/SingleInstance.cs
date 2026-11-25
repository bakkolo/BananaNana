using System.Net.Sockets;
using System.Text;
namespace BananaNana.Desktop;

// One running copy. A second launch sends "show" over a Unix socket in $XDG_RUNTIME_DIR and exits; the first
// copy brings its window forward. Nothing here can stop the app from starting: if the socket cannot be used, the app runs anyway.
public sealed class SingleInstance : IDisposable
{
    public const string ShowCommand = "show";
    public const string GuideCommand = "guide";
    public const string GuideSampleCommand = "guide-sample";
    public const string GuideMarkupDemoCommand = "guide-markup-demo";
    public const string GuideMarkupDemoEditCommand = "guide-markup-demo-edit";
    readonly Socket listener;
    volatile Action? onShow;

    SingleInstance(Socket listener) { this.listener = listener; }

    public static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath(),
            "banananana.sock");

    // True when another copy answered and was asked to show its window.
    public static bool TryNotifyExisting(string command = ShowCommand)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            client.Connect(new UnixDomainSocketEndPoint(SocketPath));
            client.Send(Encoding.ASCII.GetBytes(command + "\n"));
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

    // Called with true for the sample guide, false for the plain Guide maker.
    public Action<bool>? OnGuide { get; set; }

    // Called with true for the markup demo with its step opened in the markup window, false for the demo guide alone.
    public Action<bool>? OnMarkupDemo { get; set; }

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
            var buffer = new byte[64];
            int read = client.Receive(buffer);
            switch (Encoding.ASCII.GetString(buffer, 0, read).Trim())
            {
                case ShowCommand: OnShow?.Invoke(); break;
                case GuideCommand: OnGuide?.Invoke(false); break;
                case GuideSampleCommand: OnGuide?.Invoke(true); break;
                case GuideMarkupDemoCommand: OnMarkupDemo?.Invoke(false); break;
                case GuideMarkupDemoEditCommand: OnMarkupDemo?.Invoke(true); break;
            }
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
