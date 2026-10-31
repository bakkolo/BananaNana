using System.Diagnostics;
using System.IO;
using System.Windows;
namespace BananaNana;
public sealed record LanguageItem(string Code,string Name);
public partial class SettingsWindow:Window{
 readonly AppSettings settings;readonly string libraryName;readonly string? libraryPath;
 public SettingsWindow(AppSettings settings,string libraryName,string? path){this.settings=settings;this.libraryName=libraryName;libraryPath=path;InitializeComponent();LanguagePicker.Fill(LanguageBox);AutostartBox.IsChecked=Autostart.Enabled;ShowLibrary();Loc.Changed+=ShowLibrary;Closed+=(_,_)=>Loc.Changed-=ShowLibrary;}
 void ShowLibrary(){LibraryName.Text=Loc.F("Set.Library",libraryName);LibraryPath.Text=libraryPath??Loc.T("Set.NotSaved");}
 void Language_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e)=>LanguagePicker.Apply(LanguageBox,settings);
 void Autostart_Click(object s,RoutedEventArgs e){try{Autostart.Enabled=AutostartBox.IsChecked==true;}catch(Exception ex)when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException){MessageBox.Show(this,Loc.T("Set.AutostartFailed")+ex.Message);AutostartBox.IsChecked=Autostart.Enabled;}}
 void OpenFolder_Click(object s,RoutedEventArgs e){var folder=Path.GetDirectoryName(libraryPath)??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"BananaNana");if(Directory.Exists(folder))Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});else MessageBox.Show(this,Loc.T("Set.NoFolder")+folder);}
}
// Shared by Settings and the welcome guide: a combo box of languages (in their own names) that applies and saves the choice.
static class LanguagePicker{
 public static void Fill(System.Windows.Controls.ComboBox box){var items=Loc.Languages.Select(l=>new LanguageItem(l.Code,l.Name)).ToList();box.ItemsSource=items;box.SelectedItem=items.First(i=>i.Code==Loc.Current);}
 public static void Apply(System.Windows.Controls.ComboBox box,AppSettings settings){if(box.SelectedItem is not LanguageItem item||item.Code==Loc.Current)return;Loc.Apply(item.Code);settings.Language=item.Code;settings.Save();}
}
