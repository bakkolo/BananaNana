using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
namespace BananaNana.Desktop;

public enum PickStatus { Picked, Cancelled, Unavailable }

public readonly record struct PickResult(PickStatus Status, string? Path = null);

// What a file dialog is for. Patterns are lower case globs such as "*.png"; SuggestedName is for save dialogs.
public sealed record PickRequest(string Title, string FilterName, string[] Patterns, string StartDirectory, string? SuggestedName = null);

// Every open and save dialog of the app goes through here. The portal (Avalonia's StorageProvider) is tried first.
// Under "newgrp input" the portal refuses the app's D-Bus calls, so the dialog fails at once; then kdialog, and then
// zenity, are asked instead. A dialog that failed returns in well under 300 ms; a user who cancels takes longer, so
// a quick empty answer counts as a failure and a slow one as a cancel.
public static class FilePicker
{
    public static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"];
    static readonly TimeSpan FailedWithin = TimeSpan.FromMilliseconds(300);

    public static string PicturesFolder => Folder(Environment.SpecialFolder.MyPictures);
    public static string DocumentsFolder => Folder(Environment.SpecialFolder.MyDocuments);

    public static Task<PickResult> OpenAsync(Window owner, PickRequest request) => PickAsync(owner, request, save: false);

    public static Task<PickResult> SaveAsync(Window owner, PickRequest request) => PickAsync(owner, request, save: true);

    static string Folder(Environment.SpecialFolder special)
    {
        var path = Environment.GetFolderPath(special);
        return path.Length > 0 ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    static async Task<PickResult> PickAsync(Window owner, PickRequest request, bool save)
    {
        var watch = Stopwatch.StartNew();
        string? path = null;
        bool failed = false;
        try
        {
            path = save ? await SaveWithPortal(owner, request) : await OpenWithPortal(owner, request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failed = true;
        }
        if (path is not null) return new PickResult(PickStatus.Picked, path);
        if (!failed && watch.Elapsed >= FailedWithin) return new PickResult(PickStatus.Cancelled);
        return await FallbackAsync(owner, request, save);
    }

    static async Task<string?> OpenWithPortal(Window owner, PickRequest r)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = r.Title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(r.FilterName) { Patterns = PortalPatterns(r.Patterns) }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    static async Task<string?> SaveWithPortal(Window owner, PickRequest r)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = r.Title,
            SuggestedFileName = r.SuggestedName,
            DefaultExtension = r.Patterns.Length > 0 ? Path.GetExtension(r.Patterns[0]).TrimStart('.') : null,
            FileTypeChoices = [new FilePickerFileType(r.FilterName) { Patterns = PortalPatterns(r.Patterns) }],
        });
        return file?.TryGetLocalPath();
    }

    // The portal has always been given upper case too (IMG.PNG), so both spellings are listed.
    static string[] PortalPatterns(string[] patterns) =>
        patterns.Concat(patterns.Select(p => p.ToUpperInvariant())).Distinct().ToArray();

    // kdialog, then zenity. Each runs in the background while the owner window is disabled (modal-ish).
    static async Task<PickResult> FallbackAsync(Window owner, PickRequest request, bool save)
    {
        Console.WriteLine($"File dialog: the portal did not open; falling back to kdialog, then zenity ({request.Title}).");
        PickResult result = new(PickStatus.Unavailable);
        bool wasEnabled = owner.IsEnabled;
        owner.IsEnabled = false;
        try
        {
            var (exit, stdout, stderr) = await RunAsync("kdialog", KdialogArguments(request, save));
            result = Interpret(exit, stdout, stderr);
            if (result.Status == PickStatus.Unavailable)
            {
                (exit, stdout, stderr) = await RunAsync("zenity", ZenityArguments(request, save));
                result = Interpret(exit, stdout, stderr);
            }
        }
        finally
        {
            owner.IsEnabled = wasEnabled;
        }
        if (result.Status == PickStatus.Unavailable)
            await Dialogs.Message(owner, request.Title, Loc.T("Picker.Unavailable"));
        return result;
    }

    // kdialog --title T --getopenfilename <dir> "Filter (*.a *.b)"; a save dialog starts at <dir>/<suggested name>.
    public static IReadOnlyList<string> KdialogArguments(PickRequest r, bool save)
    {
        string filter = $"{r.FilterName} ({string.Join(" ", r.Patterns)})";
        string start = r.SuggestedName is { Length: > 0 } name ? Path.Combine(r.StartDirectory, name) : r.StartDirectory;
        return ["--title", r.Title, save ? "--getsavefilename" : "--getopenfilename", start, filter];
    }

    // zenity --file-selection --title=T --file-filter="Filter | *.a *.b" --filename=<dir>/
    public static IReadOnlyList<string> ZenityArguments(PickRequest r, bool save)
    {
        var arguments = new List<string>
        {
            "--file-selection",
            $"--title={r.Title}",
            $"--file-filter={r.FilterName} | {string.Join(" ", r.Patterns)}",
        };
        if (save)
        {
            arguments.Add("--save");
            arguments.Add("--confirm-overwrite");
            arguments.Add($"--filename={Path.Combine(r.StartDirectory, r.SuggestedName ?? "")}");
        }
        else
        {
            arguments.Add($"--filename={r.StartDirectory}/");
        }
        return arguments;
    }

    // Exit 0 with a path is a pick. Exit 1 with nothing on stderr is a cancel (both tools use it). Anything else,
    // including a tool that is not installed (exit is null), means the tool did not work.
    public static PickResult Interpret(int? exitCode, string stdout, string stderr)
    {
        if (exitCode == 0)
        {
            var line = stdout.Trim().Split('\n')[0].Trim();
            return line.Length > 0 ? new PickResult(PickStatus.Picked, line) : new PickResult(PickStatus.Unavailable);
        }
        if (exitCode == 1 && stderr.Trim().Length == 0) return new PickResult(PickStatus.Cancelled);
        return new PickResult(PickStatus.Unavailable);
    }

    // Runs the tool and reads its output. Exit is null when the tool cannot be started (not installed).
    static async Task<(int? Exit, string Stdout, string Stderr)> RunAsync(string tool, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return (null, "", "");
        }
        if (process is null) return (null, "", "");
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await stdout, await stderr);
        }
    }
}
