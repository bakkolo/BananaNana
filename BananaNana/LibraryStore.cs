using System.IO;
using System.Text.Json;
namespace BananaNana;
public sealed class Snippet{public string Trigger{get;set;}="";public string Content{get;set;}="";}
public sealed class SnippetLibrary{public string Name{get;set;}="My library";public List<Snippet> Snippets{get;set;}=new();}
public sealed class LibraryStore{
 public const string FileName="BananaNana.library.json";
 public SnippetLibrary Library{get;private set;}=new();
 public string? CurrentPath{get;private set;}
 public string[] StandardPaths=>new[]{Path.Combine(AppContext.BaseDirectory,FileName),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"BananaNana",FileName)};
 public void LoadStartup(){var matches=StandardPaths.Where(File.Exists).ToArray();if(matches.Length==1)Open(matches[0]);else if(matches.Length==2){var choice=System.Windows.MessageBox.Show("Two libraries found. Yes = next to the EXE, No = Documents, Cancel = new library.","Banana Nana",System.Windows.MessageBoxButton.YesNoCancel);if(choice==System.Windows.MessageBoxResult.Yes)Open(matches[0]);else if(choice==System.Windows.MessageBoxResult.No)Open(matches[1]);}}
 public void Open(string path){var data=JsonSerializer.Deserialize<SnippetLibrary>(File.ReadAllText(path))??throw new InvalidDataException("Invalid library file");if(data.Snippets.Any(s=>string.IsNullOrWhiteSpace(s.Trigger))||data.Snippets.GroupBy(s=>s.Trigger,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("The library has duplicate or empty triggers");Library=data;CurrentPath=path;}
 public void Save(){CurrentPath??=StandardPaths[1];Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath)!);var temp=CurrentPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(Library,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,CurrentPath,true);}
 public void Export(string path)=>File.WriteAllText(path,JsonSerializer.Serialize(Library,new JsonSerializerOptions{WriteIndented=true}));
}