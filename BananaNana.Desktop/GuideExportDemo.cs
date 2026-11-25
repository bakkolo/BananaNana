namespace BananaNana.Desktop;

// banananana --guide-export-demo <outdir>: makes (or remakes) the "Markup demo" guide, as --guide-markup-demo does,
// and exports it to demo.pdf, demo.docx and demo.html in outdir. It runs on its own: no window, no hand-over to a
// running app, and no clipboard or snippet library is touched. Program.Main calls it before anything else.
public static class GuideExportDemo
{
    public const string Argument = "--guide-export-demo";

    public static int Run(string[] args)
    {
        int at = Array.IndexOf(args, Argument);
        if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Usage: banananana {Argument} <outdir>");
            return 2;
        }
        string outDir = Path.GetFullPath(args[at + 1]);
        try
        {
            Directory.CreateDirectory(outDir);
            var store = new GuideStore(GuideStore.DefaultFolder());
            var guide = GuideExport.Prepare(store.Load(GuideMarkupDemo.Create(store)));

            string pdf = Path.Combine(outDir, "demo.pdf");
            string docx = Path.Combine(outDir, "demo.docx");
            string html = Path.Combine(outDir, "demo.html");
            using (var file = File.Create(pdf)) GuidePdf.Write(guide, file);
            using (var file = File.Create(docx)) GuideDocx.Write(guide, file);
            File.WriteAllText(html, GuideExport.Document(guide), new System.Text.UTF8Encoding(false));

            foreach (var path in new[] { pdf, docx, html }) Console.WriteLine(path);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
