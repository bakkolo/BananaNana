using System.Windows;
namespace BananaNana;
// Quick guide and tips. As the welcome screen (first start) its heading greets the user; the language picker
// sits at the top so a new user can switch before reading.
public partial class HelpWindow:Window{
 readonly AppSettings settings;
 public HelpWindow(AppSettings settings,bool welcome){this.settings=settings;InitializeComponent();Heading.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty,welcome?"Help.Welcome":"Help.Title");LanguagePicker.Fill(LanguageBox);}
 void Language_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e)=>LanguagePicker.Apply(LanguageBox,settings);
 void Close_Click(object s,RoutedEventArgs e)=>Close();
}
