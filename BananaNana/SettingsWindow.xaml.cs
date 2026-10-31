using System.Diagnostics;
using System.IO;
using System.Windows;
namespace BananaNana;
public partial class SettingsWindow:Window{
 readonly string? libraryPath;
 public SettingsWindow(string libraryName,string? path){InitializeComponent();libraryPath=path;AutostartBox.IsChecked=Autostart.Enabled;LibraryName.Text="Library: "+libraryName;LibraryPath.Text=path??"Not saved yet. It will be created in Documents\\BananaNana when you save the first snippet.";}
 void Autostart_Click(object s,RoutedEventArgs e){try{Autostart.Enabled=AutostartBox.IsChecked==true;}catch(Exception ex)when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException){MessageBox.Show(this,"Could not change the Windows startup setting: "+ex.Message);AutostartBox.IsChecked=Autostart.Enabled;}}
 void OpenFolder_Click(object s,RoutedEventArgs e){var folder=Path.GetDirectoryName(libraryPath)??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"BananaNana");if(Directory.Exists(folder))Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});else MessageBox.Show(this,"The folder does not exist yet: "+folder);}
}
