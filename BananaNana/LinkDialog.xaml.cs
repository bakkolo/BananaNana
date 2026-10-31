using System.Windows;
namespace BananaNana;
// Result: Uri set = create/update the link; RemoveLink = unlink. Text is what the link shows.
public partial class LinkDialog:Window{
 public Uri? Uri {get;private set;}
 public string LinkText=>TextBox.Text;
 public bool RemoveLink {get;private set;}
 public LinkDialog(string text,string url,bool editing){InitializeComponent();TextBox.Text=text;UrlBox.Text=url;RemoveButton.Visibility=editing?Visibility.Visible:Visibility.Collapsed;Loaded+=(_,_)=>(string.IsNullOrEmpty(text)?TextBox:UrlBox).Focus();}
 void Ok_Click(object s,RoutedEventArgs e){Uri=LinkUrl.TryNormalize(UrlBox.Text);if(Uri==null||string.IsNullOrWhiteSpace(TextBox.Text)){ErrorText.Text=Loc.T(Uri==null?"Link.BadAddress":"Link.NoText");ErrorText.Visibility=Visibility.Visible;return;}DialogResult=true;}
 void Remove_Click(object s,RoutedEventArgs e){RemoveLink=true;DialogResult=true;}
}
