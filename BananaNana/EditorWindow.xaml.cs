using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using BananaNana.Input;
using Microsoft.Win32;
namespace BananaNana;
public partial class EditorWindow:Window{
 public Snippet Result {get;private set;}
 public EditorWindow(Snippet? snippet=null){InitializeComponent();Result=new Snippet{Trigger=snippet?.Trigger??"",Content=snippet?.Content??""};TriggerBox.Text=Result.Trigger;RichContent.Load(Editor,Result.Content);DataObject.AddPastingHandler(Editor,Editor_Pasting);}
 void Bold_Click(object s,RoutedEventArgs e)=>EditingCommands.ToggleBold.Execute(null,Editor);
 void Italic_Click(object s,RoutedEventArgs e)=>EditingCommands.ToggleItalic.Execute(null,Editor);
 void Underline_Click(object s,RoutedEventArgs e)=>EditingCommands.ToggleUnderline.Execute(null,Editor);
 void Image_Click(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"};if(d.ShowDialog()!=true)return;try{InsertImage(LoadImage(File.ReadAllBytes(d.FileName)));}catch(Exception ex)when(ex is IOException or NotSupportedException or UnauthorizedAccessException){MessageBox.Show("Could not load the image: "+ex.Message);}}
 // RichTextBox pastes Word content itself (via RTF, images included), but ignores a bare image such as a
 // screenshot. Handle that case: prefer the PNG format (keeps transparency), else the bitmap.
 void Editor_Pasting(object s,DataObjectPastingEventArgs e){var d=e.DataObject;if(d.GetDataPresent(DataFormats.Rtf)||d.GetDataPresent(DataFormats.Xaml)||d.GetDataPresent(DataFormats.XamlPackage))return;BitmapSource? image=null;if(d.GetData("PNG") is MemoryStream png)image=LoadImage(png.ToArray());else if(d.GetData(DataFormats.Bitmap) is BitmapSource bmp)image=bmp;if(image==null)return;e.CancelCommand();InsertImage(image);}
 // Images are re-encoded to PNG and loaded from memory so they never depend on a file or the clipboard later.
 void InsertImage(BitmapSource source){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var ms=new MemoryStream();encoder.Save(ms);var image=new System.Windows.Controls.Image{Source=LoadImage(ms.ToArray()),MaxWidth=500,MaxHeight=350};Editor.Selection.Text="";_=new InlineUIContainer(image,Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward));Editor.Focus();}
 static BitmapImage LoadImage(byte[] bytes){var bmp=new BitmapImage();bmp.BeginInit();bmp.StreamSource=new MemoryStream(bytes);bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.EndInit();bmp.Freeze();return bmp;}
 void Save_Click(object s,RoutedEventArgs e){var trigger=TriggerBox.Text.Trim();if(string.IsNullOrWhiteSpace(trigger)){MessageBox.Show("Enter a trigger.");return;}if(trigger.Any(char.IsWhiteSpace)){MessageBox.Show("The trigger cannot contain spaces: a space ends the trigger while typing.");return;}if(trigger.Length>TriggerMatcher.MaxWordLength){MessageBox.Show($"The trigger can be at most {TriggerMatcher.MaxWordLength} characters.");return;}Result.Trigger=trigger;Result.Content=RichContent.Save(Editor);DialogResult=true;}
 void Cancel_Click(object s,RoutedEventArgs e)=>DialogResult=false;
}
