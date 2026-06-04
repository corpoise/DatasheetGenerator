namespace DatasheetGenerator;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using DatasheetGenerator.Services;

public partial class SchemaGraphWindow : Window
{
  private readonly string rootSchemaName;
  private readonly string schemaDirectory;
  private readonly SchemaGraphService graphService;
  private readonly SchemaGraphWriter graphWriter;

  public SchemaGraphWindow(string rootSchemaName, string schemaDirectory)
  {
    this.InitializeComponent();
    this.Title = $"구조 보기 - {rootSchemaName}";
    this.rootSchemaName = rootSchemaName;
    this.schemaDirectory = schemaDirectory;
    this.graphService = new SchemaGraphService(new SchemaService(), new DataEntryService());
    this.graphWriter = new SchemaGraphWriter();
    this.Loaded += async (s, e) => await this.InitializeAsync();
    this.Closed += (s, e) => this.webView.Dispose();
  }

  private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key is Key.Escape)
    {
      this.Close();
    }
  }

  private async Task InitializeAsync()
  {
    var dataDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "schema_graph", "data");
    var buildTask = Task.Run(() =>
    {
      var dto = this.graphService.Build(this.rootSchemaName, this.schemaDirectory);
      this.graphWriter.Write(dto, dataDirectory);
    });

    CoreWebView2Environment env;
    try
    {
      var userDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatasheetGenerator", "WebView2");
      env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }
    catch (Exception)
    {
      var result = MessageBox.Show(
        this,
        "WebView2 Runtime is required but not installed. Open the download page?",
        "WebView2 Runtime Not Found",
        MessageBoxButton.YesNo,
        MessageBoxImage.Error);
      if (result is MessageBoxResult.Yes)
      {
        Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
      }

      this.Close();
      return;
    }

    try
    {
      await buildTask;
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "그래프 생성 실패", MessageBoxButton.OK, MessageBoxImage.Error);
      this.Close();
      return;
    }

    try
    {
      await this.webView.EnsureCoreWebView2Async(env);
      var settings = this.webView.CoreWebView2.Settings;
      settings.AreDefaultContextMenusEnabled = false;
      settings.AreBrowserAcceleratorKeysEnabled = false;
      settings.AreDevToolsEnabled = false;
      settings.IsZoomControlEnabled = false;
      var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
      if (Directory.Exists(wwwroot) is false)
      {
        throw new DirectoryNotFoundException($"Required resource folder not found: {wwwroot}");
      }

      this.webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
        "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
      var url = $"https://app.local/schema_graph/index.html?root={Uri.EscapeDataString(this.rootSchemaName)}";
      this.webView.CoreWebView2.Navigate(url);
    }
    catch (DirectoryNotFoundException ex)
    {
      MessageBox.Show(this, ex.Message, "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
      this.Close();
    }
    catch (Exception)
    {
      var result = MessageBox.Show(
        this,
        "WebView2 Runtime is required but not installed. Open the download page?",
        "WebView2 Runtime Not Found",
        MessageBoxButton.YesNo,
        MessageBoxImage.Error);
      if (result is MessageBoxResult.Yes)
      {
        Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
      }

      this.Close();
    }
  }
}
