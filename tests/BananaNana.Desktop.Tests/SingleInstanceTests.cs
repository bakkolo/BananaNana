using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

// A running copy receives the guide-maker commands over its socket. The runtime directory is a temp folder.
public class SingleInstanceTests
{
    [Fact]
    public async Task Guide_commands_reach_the_running_copy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bn-socket-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", dir);
        try
        {
            using var listener = SingleInstance.TryListen();
            Assert.NotNull(listener);
            var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var signal = new SemaphoreSlim(0);
            listener!.OnShow = () => { received.Enqueue("show"); signal.Release(); };
            listener.OnGuide = sample => { received.Enqueue(sample ? "sample" : "guide"); signal.Release(); };

            Assert.True(SingleInstance.TryNotifyExisting(SingleInstance.GuideCommand));
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(SingleInstance.TryNotifyExisting(SingleInstance.GuideSampleCommand));
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(SingleInstance.TryNotifyExisting());
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Equal(new[] { "guide", "sample", "show" }, received.ToArray());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", previous);
            Directory.Delete(dir, true);
        }
    }
}
