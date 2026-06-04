namespace DatasheetGenerator;

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DatasheetGenerator.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class SchemaEditorWindow : Window
{
  private static readonly string DefaultTemplate = new JObject
  {
    ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
    ["additionalProperties"] = false,
    ["domain"] = new JArray("json"),
    ["type"] = "object",
    ["properties"] = new JObject
    {
      ["Id"] = new JObject { ["type"] = "integer", ["minimum"] = 1, ["description"] = "고유식별자" }
    },
    ["required"] = new JArray("Id")
  }.ToString(Formatting.Indented);

  private readonly SchemaService schemaService;
  private readonly string schemaDirectory;
  private readonly string? existingFilePath;
  private readonly IReadOnlyList<string> configuredDomains;
  private ScrollViewer? lineNumberScrollViewer;
  private int selectionAnchor = -1;

  public bool WasSaved { get; private set; }
  public string? SavedSchemaPath { get; private set; }

  public SchemaEditorWindow(SchemaService schemaService, string schemaDirectory, IReadOnlyList<string> configuredDomains, string? existingFilePath = null)
  {
    this.InitializeComponent();
    this.schemaService = schemaService;
    this.schemaDirectory = schemaDirectory;
    this.configuredDomains = configuredDomains;
    this.existingFilePath = existingFilePath;

    if (existingFilePath is not null)
    {
      this.fileNameTextBox.Text = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(existingFilePath));
      this.fileNameTextBox.IsReadOnly = true;
      this.fileNameTextBox.Background = System.Windows.Media.Brushes.WhiteSmoke;
      this.schemaTextBox.Text = this.schemaService.LoadSchemaText(existingFilePath);
    }
    else
    {
      this.schemaTextBox.Text = DefaultTemplate;
    }

    this.saveButton.IsEnabled = string.IsNullOrWhiteSpace(this.fileNameTextBox.Text) is false;
  }

  private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key is Key.Escape)
    {
      this.Close();
    }
  }

  private void WindowLoaded(object sender, RoutedEventArgs e)
  {
    var schemaScrollViewer = FindScrollViewer(this.schemaTextBox);
    if (schemaScrollViewer is not null)
    {
      schemaScrollViewer.ScrollChanged += this.OnSchemaScrollChanged;
    }
    this.lineNumberScrollViewer = FindScrollViewer(this.lineNumbersTextBox);
    this.UpdateLineNumbers();
  }

  private void OnSchemaScrollChanged(object sender, ScrollChangedEventArgs e)
  {
    this.lineNumberScrollViewer?.ScrollToVerticalOffset(e.VerticalOffset);
  }

  private void UpdateLineNumbers()
  {
    var lineCount = this.schemaTextBox.LineCount;
    if (lineCount < 1)
    {
      lineCount = 1;
    }
    var sb = new StringBuilder();
    for (var i = 1; i <= lineCount; i++)
    {
      if (i > 1)
      {
        sb.Append('\n');
      }
      sb.Append(i);
    }
    this.lineNumbersTextBox.Text = sb.ToString();
  }

  private static ScrollViewer? FindScrollViewer(DependencyObject obj)
  {
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
    {
      var child = VisualTreeHelper.GetChild(obj, i);
      if (child is ScrollViewer sv)
      {
        return sv;
      }
      var result = FindScrollViewer(child);
      if (result is not null)
      {
        return result;
      }
    }
    return null;
  }

  private void FileNameTextChanged(object sender, TextChangedEventArgs e)
  {
    this.saveButton.IsEnabled = string.IsNullOrWhiteSpace(this.fileNameTextBox.Text) is false;
  }

  private void SchemaTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
  {
    this.selectionAnchor = -1;
    this.UpdateLineNumbers();

    if (string.IsNullOrWhiteSpace(this.schemaTextBox.Text))
    {
      this.validationTextBlock.Text = string.Empty;
      return;
    }

    try
    {
      JToken.Parse(this.schemaTextBox.Text);
      this.validationTextBlock.Text = string.Empty;
    }
    catch (JsonReaderException ex)
    {
      this.validationTextBlock.Text = $"JSON 구문 오류: {ex.Message}";
    }
  }

  private void SchemaTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
  {
    var isShiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) is not ModifierKeys.None;

    if (!isShiftHeld)
    {
      this.selectionAnchor = -1;
    }

    if (e.Key is Key.End && isShiftHeld)
    {
      e.Handled = true;
      var caretIndex = this.schemaTextBox.CaretIndex;
      if (this.selectionAnchor < 0 || this.schemaTextBox.SelectionLength is 0)
      {
        this.selectionAnchor = caretIndex;
      }
      var lineIndex = this.schemaTextBox.GetLineIndexFromCharacterIndex(caretIndex);
      var lineCharStart = this.schemaTextBox.GetCharacterIndexFromLineIndex(lineIndex);
      var lineEnd = lineCharStart + this.schemaTextBox.GetLineLength(lineIndex);
      var schemaText = this.schemaTextBox.Text;
      while (lineEnd > lineCharStart && lineEnd <= schemaText.Length && schemaText[lineEnd - 1] is '\r' or '\n')
      {
        lineEnd--;
      }
      this.schemaTextBox.SelectionStart = Math.Min(this.selectionAnchor, lineEnd);
      this.schemaTextBox.SelectionLength = Math.Abs(lineEnd - this.selectionAnchor);
      return;
    }

    if (e.Key is Key.Home && isShiftHeld)
    {
      e.Handled = true;
      var caretIndex = this.schemaTextBox.CaretIndex;
      if (this.selectionAnchor < 0 || this.schemaTextBox.SelectionLength is 0)
      {
        this.selectionAnchor = caretIndex;
      }
      var homeLineIndex = this.schemaTextBox.GetLineIndexFromCharacterIndex(caretIndex);
      var homeLineStart = this.schemaTextBox.GetCharacterIndexFromLineIndex(homeLineIndex);
      this.schemaTextBox.SelectionStart = Math.Min(this.selectionAnchor, homeLineStart);
      this.schemaTextBox.SelectionLength = Math.Abs(homeLineStart - this.selectionAnchor);
      return;
    }

    if (e.Key is Key.Return)
    {
      e.Handled = true;
      var returnCaretIndex = this.schemaTextBox.SelectionStart;
      var returnLineIndex = this.schemaTextBox.GetLineIndexFromCharacterIndex(returnCaretIndex);
      var returnLineStart = this.schemaTextBox.GetCharacterIndexFromLineIndex(returnLineIndex);
      var returnLineLen = this.schemaTextBox.GetLineLength(returnLineIndex);
      var returnText = this.schemaTextBox.Text;
      var returnLineText = returnText.Substring(returnLineStart, Math.Min(returnLineLen, returnText.Length - returnLineStart));
      var returnIndent = new string(returnLineText.TakeWhile(c => c is ' ' or '\t').ToArray());
      var textBeforeCaret = returnText.Substring(returnLineStart, returnCaretIndex - returnLineStart).TrimEnd();
      var extraIndent = textBeforeCaret.EndsWith('{') ? "  " : string.Empty;
      var insertText = "\n" + returnIndent + extraIndent;
      this.schemaTextBox.SelectedText = insertText;
      this.schemaTextBox.SelectionStart = returnCaretIndex + insertText.Length;
      this.schemaTextBox.SelectionLength = 0;
      return;
    }

    if (e.Key is not Key.Tab || this.schemaTextBox.SelectionLength is 0)
    {
      return;
    }

    e.Handled = true;
    var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) is not ModifierKeys.None;
    var text = this.schemaTextBox.Text;
    var selStart = this.schemaTextBox.SelectionStart;
    var selEnd = selStart + this.schemaTextBox.SelectionLength;

    var lineStart = selStart;
    while (lineStart > 0 && text[lineStart - 1] is not '\n')
    {
      lineStart--;
    }

    var region = text[lineStart..selEnd];
    var newLine = region.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    const string indent = "  ";

    var lines = region.Split(new[] { newLine }, StringSplitOptions.None).ToList();
    for (var i = 0; i < lines.Count; i++)
    {
      if (i == lines.Count - 1 && string.IsNullOrEmpty(lines[i]))
      {
        break;
      }

      if (isShift)
      {
        if (lines[i].StartsWith(indent, StringComparison.Ordinal))
        {
          lines[i] = lines[i][indent.Length..];
        }
      }
      else
      {
        lines[i] = indent + lines[i];
      }
    }

    var modified = string.Join(newLine, lines);
    this.schemaTextBox.SelectionStart = lineStart;
    this.schemaTextBox.SelectionLength = selEnd - lineStart;
    this.schemaTextBox.SelectedText = modified;
    this.schemaTextBox.SelectionStart = lineStart;
    this.schemaTextBox.SelectionLength = modified.Length;
  }

  private void FormatClick(object sender, RoutedEventArgs e)
  {
    var text = this.schemaTextBox.Text;
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    try
    {
      var formatted = JToken.Parse(text).ToString(Formatting.Indented);
      this.schemaTextBox.Text = formatted;
    }
    catch (JsonReaderException)
    {
    }
  }

  private void CancelClick(object sender, RoutedEventArgs e)
  {
    this.Close();
  }

  private void SaveClick(object sender, RoutedEventArgs e)
  {
    var fileName = this.fileNameTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(fileName))
    {
      MessageBox.Show(this, "파일명을 입력하세요.", "저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    var schemaText = this.schemaTextBox.Text;
    var validation = this.schemaService.ValidateSchemaText(schemaText, this.configuredDomains);
    if (validation.IsValid is false)
    {
      MessageBox.Show(this, validation.Message, "검증 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    var targetPath = this.existingFilePath ?? Path.Combine(this.schemaDirectory, $"{fileName}.schema.json");

    if (this.existingFilePath is null && File.Exists(targetPath))
    {
      var answer = MessageBox.Show(
        this,
        $"'{fileName}.schema.json' 파일이 이미 존재합니다. 덮어쓰시겠습니까?",
        "덮어쓰기 확인",
        MessageBoxButton.OKCancel,
        MessageBoxImage.Question);

      if (answer is not MessageBoxResult.OK)
      {
        return;
      }
    }

    var formattedText = JToken.Parse(schemaText).ToString(Formatting.Indented);

    try
    {
      this.schemaService.SaveSchema(targetPath, formattedText);
      this.WasSaved = true;
      this.SavedSchemaPath = targetPath;
      this.schemaTextBox.Text = formattedText;
      this.Close();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}
