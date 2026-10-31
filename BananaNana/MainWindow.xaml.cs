using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BananaNana.Paste;
using Microsoft.Win32;
namespace BananaNana;
// One list row: the snippet plus a one-line plain-text summary for the list.
public sealed record SnippetRow(Snippet Snippet,string Trigger,string Summary);
public partial class MainWindow : Window {
 readonly LibraryStore store = new();
 readonly ExpansionCoordinator expander = new();
 // Summaries cost a XamlPackage load each, so they are cached by content.
 readonly Dictionary<string,string> summaries = new();
 readonly TrayIcon tray = new();
 readonly AppSettings settings;
 bool exiting, trayHintShown;
 int tip = Random.Shared.Next(TipCount);
 public const int TipCount = 8;
 public MainWindow(AppSettings settings){this.settings=settings;InitializeComponent();store.LoadStartup();expander.StatusChanged+=t=>StatusText.Text=t;expander.Start();tray.OpenRequested+=ShowFromTray;tray.PauseToggled+=()=>SetPaused(!expander.Paused);tray.ExitRequested+=ExitApp;Autostart.RepairIfMoved();Loc.Changed+=OnLanguageChanged;Refresh();SetPaused(false);ShowTip();}
 // Texts set from code (counts, status, summaries, tip) are re-rendered; XAML texts update by themselves.
 void OnLanguageChanged(){summaries.Clear();Refresh();SetPaused(expander.Paused);ShowTip();}
 public void ShowFromTray(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
 // The close button hides to the tray so snippets keep expanding; Exit in the tray menu really quits.
 protected override void OnClosing(System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;Hide();if(!trayHintShown){trayHintShown=true;tray.ShowHint(Loc.T("Tray.HiddenTitle"),Loc.T("Tray.HiddenText"));}}base.OnClosing(e);}
 void ExitApp(){exiting=true;foreach(var w in OwnedWindows.OfType<Window>().ToList())w.Close();Close();Application.Current.Shutdown();}
 Snippet? Selected=>(SnippetsList.SelectedItem as SnippetRow)?.Snippet;
 void Refresh(Snippet? select=null){select??=Selected;var rows=store.Library.Snippets.Where(s=>s.Trigger.Contains(SearchBox.Text,StringComparison.OrdinalIgnoreCase)).OrderBy(s=>s.Trigger,StringComparer.OrdinalIgnoreCase).Select(s=>new SnippetRow(s,s.Trigger,Summary(s.Content))).ToList();SnippetsList.ItemsSource=rows;SnippetsList.SelectedItem=rows.FirstOrDefault(r=>ReferenceEquals(r.Snippet,select))??rows.FirstOrDefault();int n=store.Library.Snippets.Count;Title="Banana Nana | "+store.Library.Name;LibraryText.Text=$"{store.Library.Name} · {Loc.F("Main.SnippetCount",n)}";EmptyText.Visibility=n==0?Visibility.Visible:Visibility.Collapsed;SearchHint.Visibility=SearchBox.Text.Length==0?Visibility.Visible:Visibility.Collapsed;expander.SetSnippets(store.Library.Snippets);ShowPreview();}
 string Summary(string content){if(string.IsNullOrEmpty(content))return Loc.T("Main.SummaryEmpty");if(summaries.TryGetValue(content,out var cached))return cached;string text;try{var doc=RichContentRenderer.LoadXamlPackage(content);text=string.Join(' ',new TextRange(doc.ContentStart,doc.ContentEnd).Text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));if(text.Length==0)text=Loc.T("Main.SummaryImage");}catch(Exception ex)when(ex is FormatException or InvalidOperationException or ArgumentException){text=Loc.T("Main.SummaryUnreadable");}return summaries[content]=text.Length>120?text[..120]+"…":text;}
 void ShowPreview(){var s=Selected;PreviewTitle.Text=s?.Trigger??"";SelectionActions.Visibility=s==null?Visibility.Hidden:Visibility.Visible;try{RichContent.Load(Preview,s?.Content);}catch{Preview.Document.Blocks.Clear();}}
 void ShowTip()=>TipText.Text=Loc.T($"Tip.{tip+1}");
 void NextTip_Click(object sender,RoutedEventArgs e){tip=(tip+1)%TipCount;ShowTip();}
 void Help_Click(object sender,RoutedEventArgs e)=>ShowHelp(welcome:false);
 public void ShowHelp(bool welcome)=>new HelpWindow(settings,welcome){Owner=IsVisible?this:null}.Show();
 void Pause_Click(object sender,RoutedEventArgs e)=>SetPaused(PauseToggle.IsChecked==true);
 // Window toggle and tray menu both end up here so they always agree.
 void SetPaused(bool paused){expander.Paused=paused;PauseToggle.IsChecked=paused;tray.SetPaused(paused);StatusText.Text=Loc.T(paused?"Status.Paused":"Status.On");PauseLabel.Text=Loc.T(paused?"Main.Resume":"Main.Pause");PauseIcon.Text=paused?"":"";StatusDot.Fill=paused?Brushes.Orange:new SolidColorBrush(Color.FromRgb(0x2E,0xA0,0x43));}
 protected override void OnClosed(EventArgs e){Loc.Changed-=OnLanguageChanged;expander.Dispose();tray.Dispose();base.OnClosed(e);}
 void New_Click(object sender,RoutedEventArgs e)=>Edit(null);
 void Edit_Click(object sender,RoutedEventArgs e){if(Selected is {} s)Edit(s);}
 void Delete_Click(object sender,RoutedEventArgs e){if(Selected is not {} s)return;if(MessageBox.Show(this,Loc.F("Main.DeleteConfirm",s.Trigger),Loc.T("Main.DeleteTitle"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;store.Library.Snippets.Remove(s);try{store.Save();}catch(Exception ex){MessageBox.Show(ex.Message);}Refresh();}
 void Edit(Snippet? current){var window=new EditorWindow(current){Owner=this};if(window.ShowDialog()!=true)return;var updated=window.Result;if(store.Library.Snippets.Any(s=>!ReferenceEquals(s,current)&&string.Equals(s.Trigger,updated.Trigger,StringComparison.OrdinalIgnoreCase))){MessageBox.Show(Loc.T("Main.Duplicate"));return;}if(current==null)store.Library.Snippets.Add(updated);else{current.Trigger=updated.Trigger;current.Content=updated.Content;}try{store.Save();Refresh(current??updated);}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void List_DoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e){if(Selected is {} s)Edit(s);}
 void List_SelectionChanged(object sender,SelectionChangedEventArgs e)=>ShowPreview();
 void Search_Changed(object sender,TextChangedEventArgs e){if(SnippetsList!=null)Refresh();}
 void Import_Click(object sender,RoutedEventArgs e){var paths=store.StandardPaths.Where(System.IO.File.Exists).ToArray();string? path=null;if(paths.Length==1)path=paths[0];else if(paths.Length==2){var choice=MessageBox.Show(Loc.T("Main.ImportChoice"),Loc.T("Main.ImportTitle"),MessageBoxButton.YesNoCancel);if(choice==MessageBoxResult.Yes)path=paths[0];if(choice==MessageBoxResult.No)path=paths[1];}if(path==null){var d=new OpenFileDialog{Filter=Loc.T("Main.LibraryFilter")+"|*.json"};if(d.ShowDialog()==true)path=d.FileName;}if(path!=null)try{store.Open(path);Refresh();}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void Export_Click(object sender,RoutedEventArgs e){var d=new SaveFileDialog{FileName=LibraryStore.FileName,Filter=Loc.T("Main.LibraryFilter")+"|*.json"};if(d.ShowDialog()==true)try{store.Export(d.FileName);}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void Settings_Click(object sender,RoutedEventArgs e)=>new SettingsWindow(settings,store.Library.Name,store.CurrentPath){Owner=this}.ShowDialog();
}
