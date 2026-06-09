namespace DatasheetGenerator;

using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

public partial class CodePreviewWindow : Window
{
  private const string HtmlContent = """
    <!DOCTYPE html>
    <html>
    <head>
      <meta charset="utf-8">
      <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
          background: #1e1e1e;
          color: #d4d4d4;
          font: 13px/1.6 Consolas,'Courier New',monospace;
          overflow: hidden;
        }
        #editor {
          display: flex;
          height: 100vh;
          overflow: auto;
        }
        #lines {
          padding: 8px 0;
          min-width: 48px;
          text-align: right;
          color: #858585;
          user-select: none;
          background: #1e1e1e;
          border-right: 1px solid #2a2a2a;
          position: sticky;
          left: 0;
          z-index: 1;
          flex-shrink: 0;
        }
        #lines span { display: block; padding: 0 10px 0 8px; }
        #code { padding: 8px 16px; white-space: pre; flex: 1; }
        .kw { color: #569CD6; }
        .bt { color: #569CD6; }
        .ty { color: #4EC9B0; }
        .st { color: #CE9178; }
        .cm { color: #6A9955; }
        .at { color: #9CDCFE; }
        .nm { color: #B5CEA8; }
      </style>
    </head>
    <body>
      <div id="editor">
        <div id="lines"></div>
        <div id="code"></div>
      </div>
      <script>
        const KEYWORDS = new Set([
          'public','private','protected','internal','sealed','static','abstract','virtual',
          'override','readonly','const','new','return','void','class','struct','record',
          'enum','interface','namespace','using','get','set','init','null','true','false',
          'var','if','else','for','foreach','while','do','switch','case','break','continue',
          'throw','try','catch','finally','in','out','ref','is','as','partial','where'
        ]);
        const BUILTINS = new Set([
          'int','long','short','byte','float','double','decimal','bool','char',
          'string','object','dynamic'
        ]);

        function esc(s) {
          return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
        }

        function highlightLine(line) {
          if (/^\s*\/\//.test(line)) {
            return '<span class="cm">' + esc(line) + '</span>';
          }

          let result = '';
          let i = 0;
          const n = line.length;

          while (i < n) {
            const ch = line[i];

            // Inline comment
            if (ch === '/' && line[i + 1] === '/') {
              result += '<span class="cm">' + esc(line.slice(i)) + '</span>';
              break;
            }

            // String literal
            if (ch === '"') {
              let j = i + 1;
              while (j < n && line[j] !== '"') {
                if (line[j] === '\\') j++;
                j++;
              }
              result += '<span class="st">' + esc(line.slice(i, j + 1)) + '</span>';
              i = j + 1;
              continue;
            }

            // Attribute or array — attribute only if nothing but whitespace precedes [
            if (ch === '[') {
              const before = line.slice(0, i).trim();
              const isAttr = before === '' || before.endsWith('{') || before.endsWith(';') || before.endsWith(']');
              if (isAttr) {
                let j = i + 1;
                while (j < n && line[j] !== ']') j++;
                result += '<span class="at">' + esc(line.slice(i, j + 1)) + '</span>';
                i = j + 1;
              } else {
                result += esc(ch);
                i++;
              }
              continue;
            }

            // Identifier / keyword
            if (/[A-Za-z_]/.test(ch)) {
              let j = i + 1;
              while (j < n && /[A-Za-z0-9_]/.test(line[j])) j++;
              const word = line.slice(i, j);
              if (KEYWORDS.has(word)) {
                result += '<span class="kw">' + word + '</span>';
              } else if (BUILTINS.has(word)) {
                result += '<span class="bt">' + word + '</span>';
              } else if (/^[A-Z]/.test(word)) {
                result += '<span class="ty">' + esc(word) + '</span>';
              } else {
                result += esc(word);
              }
              i = j;
              continue;
            }

            // Number
            if (/[0-9]/.test(ch)) {
              let j = i + 1;
              while (j < n && /[0-9._]/.test(line[j])) j++;
              result += '<span class="nm">' + esc(line.slice(i, j)) + '</span>';
              i = j;
              continue;
            }

            result += esc(ch);
            i++;
          }
          return result;
        }

        window.setCode = function(rawCode) {
          const lines = rawCode.split('\n');
          document.getElementById('lines').innerHTML =
            lines.map((_, i) => '<span>' + (i + 1) + '</span>').join('');
          document.getElementById('code').innerHTML =
            lines.map(highlightLine).join('\n');
        };
      </script>
    </body>
    </html>
    """;

  private readonly string generatedCode;
  private readonly string? outputFilePath;

  public CodePreviewWindow(string schemaName, string generatedCode, string? outputFilePath)
  {
    this.InitializeComponent();
    this.generatedCode = generatedCode;
    this.outputFilePath = outputFilePath;
    this.Title = $"C# 코드 미리보기 - {schemaName}GameData";

    if (string.IsNullOrEmpty(outputFilePath))
    {
      this.outputPathText.Text = "CodeOutputPath가 설정되지 않았습니다. Config에 추가하세요.";
      this.generateButton.IsEnabled = false;
    }
    else
    {
      this.outputPathText.Text = $"출력: {outputFilePath}";
      this.generateButton.IsEnabled = true;
    }

    this.Loaded += async (s, e) => await this.InitializeWebViewAsync();
    this.Closed += (s, e) => this.codeWebView.Dispose();
  }

  private async Task InitializeWebViewAsync()
  {
    try
    {
      var userDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatasheetGenerator", "WebView2");
      var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
      await this.codeWebView.EnsureCoreWebView2Async(env);
      var settings = this.codeWebView.CoreWebView2.Settings;
      settings.AreDefaultContextMenusEnabled = false;
      settings.AreBrowserAcceleratorKeysEnabled = false;
      settings.AreDevToolsEnabled = false;
      settings.IsZoomControlEnabled = false;
      this.codeWebView.NavigationCompleted += this.OnNavigationCompleted;
      this.codeWebView.CoreWebView2.NavigateToString(HtmlContent);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "초기화 실패", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
  {
    this.codeWebView.NavigationCompleted -= this.OnNavigationCompleted;
    var serialized = JsonConvert.SerializeObject(this.generatedCode);
    await this.codeWebView.CoreWebView2.ExecuteScriptAsync($"setCode({serialized})");
  }

  private void GenerateClick(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrEmpty(this.outputFilePath)) { return; }

    try
    {
      var directory = Path.GetDirectoryName(this.outputFilePath);
      if (directory is not null)
      {
        Directory.CreateDirectory(directory);
      }

      File.WriteAllText(this.outputFilePath, this.generatedCode, Encoding.UTF8);
      MessageBox.Show(this, $"파일이 생성되었습니다:\n{this.outputFilePath}", "생성 완료", MessageBoxButton.OK, MessageBoxImage.Information);
      this.Close();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "생성 실패", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private void CloseClick(object sender, RoutedEventArgs e)
  {
    this.Close();
  }
}
