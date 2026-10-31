using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace BananaNana;
public partial class EditorWindow:Window{
 public Snippet Result {get;private set;}
 public EditorWindow(Snippet? snippet=null){InitializeComponent();Result=new Snippet{Trigger=snippet?.Trigger??"",Content=snippet?.Content??""};TriggerBox.Text=Result.Trigger;RichContent.Load(Editor,Result.Content);}
 void Bold_Click(object s,RoutedEventArgs e)=>EditingCommands.ToggleBold.Execute(null,Editor);
 void Italic_Click(object s,RoutedEventArgs e)=>EditingCommands.ToggleItalic.Execute(null,Editor);
 void Underline_Click(object s,RoutedEventArgs e)=>EditingCommands.ToggleUnderline.Execute(null,Editor);
 void Image_Click(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Obrazy|*.png;*.jpg;*.jpeg;*.bmp"};if(d.ShowDialog()!=true)return;var bmp=new BitmapImage();bmp.BeginInit();bmp.UriSource=new Uri(d.FileName);bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.EndInit();_=new InlineUIContainer(new System.Windows.Controls.Image{Source=bmp,MaxWidth=500,MaxHeight=350},Editor.CaretPosition);}
 void Save_Click(object s,RoutedEventArgs e){var trigger=TriggerBox.Text.Trim();if(string.IsNullOrWhiteSpace(trigger)){MessageBox.Show("Wpisz skrót.");return;}Result.Trigger=trigger;Result.Content=RichContent.Save(Editor);DialogResult=true;}
 void Cancel_Click(object s,RoutedEventArgs e)=>DialogResult=false;
}