using System.Text.Json;
using BananaNana.Linux;
using BananaNana.Linux.Evdev;

// Test harness: bn-linux-cli [--layout <xkb layout>[:variant]] <snippets.json>
// Prints keyboards, hotplug changes, the layout in use, each trigger and the timings.
// Ctrl+C quits and destroys the virtual keyboard.
const string Usage = "usage: bn-linux-cli [--layout <xkb layout>[:variant]] <snippets.json>";

string? snippetPath = null;
LayoutConfig? layout = null;
try
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--layout" && i + 1 < args.Length) layout = LayoutConfig.Parse(args[++i]);
        else if (args[i].StartsWith("--layout=", StringComparison.Ordinal)) layout = LayoutConfig.Parse(args[i]["--layout=".Length..]);
        else snippetPath = args[i];
    }
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
if (snippetPath is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var items = JsonSerializer.Deserialize<List<SnippetJson>>(
    File.ReadAllText(snippetPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
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

using var engine = new LinuxExpansionEngine(Log, layout);
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
