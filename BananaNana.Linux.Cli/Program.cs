using System.Text.Json;
using BananaNana.Linux;

// Test harness: bn-linux-cli <snippets.json>. Prints keyboards, hotplug changes, triggers and timings.
// Ctrl+C quits and destroys the virtual keyboard.
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: bn-linux-cli <snippets.json>");
    return 2;
}

var items = JsonSerializer.Deserialize<List<SnippetJson>>(
    File.ReadAllText(args[0]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
var snippets = new List<LinuxSnippet>();
foreach (var item in items)
{
    if (string.IsNullOrWhiteSpace(item.Trigger) || item.Trigger.Any(char.IsWhiteSpace))
    {
        Console.Error.WriteLine($"Invalid trigger '{item.Trigger}': triggers must be non-empty and contain no spaces.");
        return 2;
    }
    string text = item.Text ?? item.Html ?? "";
    snippets.Add(new LinuxSnippet(item.Trigger, item.Html ?? text, text));
}

var logLock = new object();
void Log(string message)
{
    lock (logLock) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");
}

using var engine = new LinuxExpansionEngine(Log);
try
{
    engine.SetSnippets(snippets);
    Log($"Triggers: {string.Join(", ", snippets.Select(s => s.Trigger))}");
    engine.Start();
}
catch (Exception e)
{
    Console.Error.WriteLine($"Cannot start: {e.Message}");
    return 1;
}

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
Log("Listening. Press Ctrl+C to quit.");
await stop.Task;
Log("Stopping, destroying the virtual keyboard.");
return 0;

record SnippetJson(string? Trigger, string? Html, string? Text);
