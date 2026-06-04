namespace DatasheetGenerator;

using System.Diagnostics;
using System.Linq;
using System.Windows;
using Microsoft.Web.WebView2.Core;

public partial class App : Application
{
  public App()
  {
    this.DispatcherUnhandledException += (s, e) =>
    {
      if (e.Exception is WebView2RuntimeNotFoundException)
      {
        var result = MessageBox.Show(
          "WebView2 Runtime is not installed and the application cannot start.\n\nWould you like to open the download page?",
          "WebView2 Runtime Required",
          MessageBoxButton.YesNo,
          MessageBoxImage.Error);
        if (result is MessageBoxResult.Yes)
        {
          try
          {
            Process.Start(new ProcessStartInfo
            {
              FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
              UseShellExecute = true
            });
          }
          catch { }
        }
        Current.Windows.OfType<DataEntryWindow>().FirstOrDefault()?.Close();
      }
      else
      {
        MessageBox.Show(e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        var dataEntryWindow = Current.Windows.OfType<DataEntryWindow>().FirstOrDefault();
        if (dataEntryWindow?.IsReady is false)
        {
          dataEntryWindow.Close();
        }
      }
      e.Handled = true;
    };
  }
}
