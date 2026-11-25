using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace BananaNana;
public sealed class Snippet{public string Trigger{get;set;}="";public string Content{get;set;}="";}
public sealed class SnippetLibrary{public string Name{get;set;}="My library";public List<Snippet> Snippets{get;set;}=new();}
// Build-time generated JSON code: no reflection, so it also works in trimmed builds. Same file format as before.
[JsonSerializable(typeof(SnippetLibrary))][JsonSourceGenerationOptions(WriteIndented=true)]
internal partial class LibraryJson:JsonSerializerContext{}
public sealed class LibraryStore{
 public const string FileName="BananaNana.library.json";
 // Set once by the WPF app (LibraryStore.Localize = Loc.T) so messages stay translated. Default: the key.
 public static Func<string,string> Localize{get;set;}=key=>key;
 public SnippetLibrary Library{get;private set;}=new();
 public string? CurrentPath{get;private set;}
 public string[] StandardPaths=>new[]{Path.Combine(AppContext.BaseDirectory,FileName),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"BananaNana",FileName)};
 public void LoadStartup(Func<string,string,bool?> choose){var matches=StandardPaths.Where(File.Exists).ToArray();if(matches.Length==1)Open(matches[0]);else if(matches.Length==2){var choice=choose(Localize("Lib.TwoFound"),"Banana Nana");if(choice==true)Open(matches[0]);else if(choice==false)Open(matches[1]);}}
 public void Open(string path){var data=JsonSerializer.Deserialize(File.ReadAllText(path),LibraryJson.Default.SnippetLibrary)??throw new InvalidDataException(Localize("Lib.Invalid"));if(data.Snippets.Any(s=>string.IsNullOrWhiteSpace(s.Trigger))||data.Snippets.GroupBy(s=>s.Trigger,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException(Localize("Lib.Duplicates"));Library=data;CurrentPath=path;}
 public void Save(){CurrentPath??=StandardPaths[1];Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath)!);var temp=CurrentPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(Library,LibraryJson.Default.SnippetLibrary));File.Move(temp,CurrentPath,true);}
 public void Export(string path)=>File.WriteAllText(path,JsonSerializer.Serialize(Library,LibraryJson.Default.SnippetLibrary));
}