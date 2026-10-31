using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace BananaNana;
public partial class MainWindow : Window {
 readonly LibraryStore store = new();
 public MainWindow(){InitializeComponent();store.LoadStartup();Refresh();}
 void Refresh(){SnippetsList.ItemsSource=store.Library.Snippets.Where(s=>s.Trigger.Contains(SearchBox.Text,StringComparison.OrdinalIgnoreCase)).OrderBy(s=>s.Trigger).ToList();Title="Banana Nana | "+store.Library.Name;}
 void New_Click(object sender,RoutedEventArgs e)=>Edit(null);
 void Edit(Snippet? current){var window=new EditorWindow(current){Owner=this};if(window.ShowDialog()!=true)return;var updated=window.Result;if(store.Library.Snippets.Any(s=>!ReferenceEquals(s,current)&&string.Equals(s.Trigger,updated.Trigger,StringComparison.OrdinalIgnoreCase))){MessageBox.Show("Taki skrót już istnieje.");return;}if(current==null)store.Library.Snippets.Add(updated);else{current.Trigger=updated.Trigger;current.Content=updated.Content;}try{store.Save();Refresh();}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void List_DoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e){if(SnippetsList.SelectedItem is Snippet s)Edit(s);}
 void List_SelectionChanged(object sender,SelectionChangedEventArgs e){try{RichContent.Load(Preview,(SnippetsList.SelectedItem as Snippet)?.Content);}catch{Preview.Document.Blocks.Clear();}}
 void Search_Changed(object sender,TextChangedEventArgs e){if(SnippetsList!=null)Refresh();}
 void Import_Click(object sender,RoutedEventArgs e){var paths=store.StandardPaths.Where(System.IO.File.Exists).ToArray();string? path=null;if(paths.Length==1)path=paths[0];else if(paths.Length==2){var choice=MessageBox.Show("Tak = folder EXE, Nie = Dokumenty, Anuluj = wybierz plik","Import",MessageBoxButton.YesNoCancel);if(choice==MessageBoxResult.Yes)path=paths[0];if(choice==MessageBoxResult.No)path=paths[1];}if(path==null){var d=new OpenFileDialog{Filter="Biblioteka Banana Nana|*.json"};if(d.ShowDialog()==true)path=d.FileName;}if(path!=null)try{store.Open(path);Refresh();}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void Export_Click(object sender,RoutedEventArgs e){var d=new SaveFileDialog{FileName=LibraryStore.FileName,Filter="Biblioteka Banana Nana|*.json"};if(d.ShowDialog()==true)try{store.Export(d.FileName);}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void Settings_Click(object sender,RoutedEventArgs e){MessageBox.Show($"Biblioteka: {store.Library.Name}\nŚcieżka: {store.CurrentPath}\nAutostart i edycja ustawień: do ukończenia przez Codexa.");}
}