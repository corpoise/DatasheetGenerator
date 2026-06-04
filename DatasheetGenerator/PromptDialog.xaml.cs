namespace DatasheetGenerator;

using System.Windows;

public partial class PromptDialog : Window
{
  public PromptDialog(string title, string message, string defaultValue)
  {
    this.InitializeComponent();
    this.Title = title;
    this.messageTextBlock.Text = message;
    this.inputTextBox.Text = defaultValue;
    this.inputTextBox.SelectAll();
    this.inputTextBox.Focus();
  }

  public string Response
  {
    get
    {
      return this.inputTextBox.Text;
    }
  }

  public static string? Show(Window owner, string title, string message, string defaultValue)
  {
    var dialog = new PromptDialog(title, message, defaultValue)
    {
      Owner = owner
    };

    if (dialog.ShowDialog() == true)
    {
      return dialog.Response;
    }

    return null;
  }

  private void OkClick(object sender, RoutedEventArgs e)
  {
    this.DialogResult = true;
  }
}
