using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using BananaNana.Input;
using Microsoft.Win32;
namespace BananaNana;
public partial class EditorWindow:Window{
 public Snippet Result {get;private set;}
 public EditorWindow(Snippet? snippet=null){InitializeComponent();Result=new Snippet{Trigger=snippet?.Trigger??"",Content=snippet?.Content??""};TriggerBox.Text=Result.Trigger;RichContent.Load(Editor,Result.Content);DataObject.AddPastingHandler(Editor,Editor_Pasting);Editor.AddHandler(Hyperlink.RequestNavigateEvent,new System.Windows.Navigation.RequestNavigateEventHandler(Link_Navigate));}
 // Links are live in the editor (IsDocumentEnabled): a plain click edits, Ctrl+click opens the browser.
 void Link_Navigate(object s,System.Windows.Navigation.RequestNavigateEventArgs e){e.Handled=true;if(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)&&LinkUrl.TryNormalize(e.Uri.ToString()) is {} uri)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}
 void Link_Click(object s,RoutedEventArgs e)=>EditLink();
 void EditLink(){var existing=FindParent<Hyperlink>(Editor.Selection.Start.Parent);var range=existing!=null?new TextRange(existing.ContentStart,existing.ContentEnd):Editor.Selection;var dialog=new LinkDialog(range.Text.Trim(),existing?.NavigateUri?.ToString()??"",existing!=null){Owner=this};if(dialog.ShowDialog()!=true)return;
  if(existing!=null){var text=dialog.RemoveLink?range.Text:dialog.LinkText;var run=new Run(text);existing.SiblingInlines.InsertBefore(existing,run);existing.SiblingInlines.Remove(existing);if(dialog.RemoveLink){Editor.Focus();return;}_=new Hyperlink(run.ContentStart,run.ContentEnd){NavigateUri=dialog.Uri};Editor.Focus();return;}
  // New link: the dialog text replaces the selection (or is inserted at the caret), then gets wrapped.
  try{Editor.Selection.Text=dialog.LinkText;_=new Hyperlink(Editor.Selection.Start,Editor.Selection.End){NavigateUri=dialog.Uri};Editor.Selection.Select(Editor.Selection.End,Editor.Selection.End);}catch(InvalidOperationException){MessageBox.Show("A link has to stay within one paragraph.");}Editor.Focus();}
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
 // Clicking an image shows corner handles to resize it (IsDocumentEnabled lets the image receive the click).
 // Clicking elsewhere or typing hides them; clicks on the handles themselves are left to the handles.
 ImageResizeAdorner? resizer;
 void Editor_MouseDown(object s,System.Windows.Input.MouseButtonEventArgs e){if(e.OriginalSource is DependencyObject d&&FindParent<System.Windows.Controls.Primitives.Thumb>(d)!=null)return;resizer?.Detach();resizer=null;if(e.OriginalSource is not System.Windows.Controls.Image image)return;if(e.ClickCount==2){e.Handled=true;EditImage(image);return;}resizer=ImageResizeAdorner.Attach(image);}
 void EditImage_Click(object s,RoutedEventArgs e){if(resizer!=null)EditImage(resizer.Target);else MessageBox.Show("Click a picture first, then Edit image (or double-click the picture).");}
 // Drawings are flattened into a new picture with the same pixel size, so the size shown in the snippet stays.
 void EditImage(System.Windows.Controls.Image image){if(image.Source is not BitmapSource source)return;var editor=new ImageEditorWindow(source){Owner=this};if(editor.ShowDialog()!=true||editor.Result==null)return;var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(editor.Result));using var ms=new MemoryStream();encoder.Save(ms);image.Source=LoadImage(ms.ToArray());}
 void Editor_KeyDown(object s,System.Windows.Input.KeyEventArgs e){resizer?.Detach();resizer=null;if(e.Key==System.Windows.Input.Key.K&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;EditLink();}}
 // Covers loaded, inserted and pasted images (e.g. from Word), so the editor shows the size that gets pasted.
 void Editor_TextChanged(object s,System.Windows.Controls.TextChangedEventArgs e)=>EditorImages.FixSizes(Editor.Document);
 static T? FindParent<T>(DependencyObject? d)where T:DependencyObject{while(d!=null&&d is not T)d=d is System.Windows.Media.Visual?System.Windows.Media.VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);return d as T;}
}
