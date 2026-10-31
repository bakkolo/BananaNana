using System.IO;
using System.Windows.Controls;
using System.Windows.Documents;
namespace BananaNana;
public static class RichContent{
 // XamlPackage embeds resources such as images; RTF alone may lose image data.
 public static string Save(RichTextBox box){using var stream=new MemoryStream();new TextRange(box.Document.ContentStart,box.Document.ContentEnd).Save(stream,System.Windows.DataFormats.XamlPackage);return Convert.ToBase64String(stream.ToArray());}
 public static void Load(RichTextBox box,string? content){box.Document.Blocks.Clear();if(string.IsNullOrEmpty(content))return;using var stream=new MemoryStream(Convert.FromBase64String(content));new TextRange(box.Document.ContentStart,box.Document.ContentEnd).Load(stream,System.Windows.DataFormats.XamlPackage);}
}