namespace DatasheetGenerator;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using DatasheetGenerator.Configuration;
using DatasheetGenerator.Export;
using DatasheetGenerator.Models;
using DatasheetGenerator.Services;

public partial class MainWindow : Window
{
  private readonly ObservableCollection<SchemaInfo> schemas;
  private readonly SchemaService schemaService;
  private readonly DataEntryService dataEntryService;
  private readonly ICollectionView? schemaView;
  private string outputRootPath = string.Empty;
  private string schemaDirectory = string.Empty;
  private string internalDataDirectory = string.Empty;
  private string excelDirectory = string.Empty;
  private string configPath = string.Empty;
  private IReadOnlyList<string> configuredDomains = [];
  private SchemaInfo? selectedSchema;

  public MainWindow()
  {
    this.InitializeComponent();

    var workArea = SystemParameters.WorkArea;
    if (this.Height > workArea.Height)
    {
      this.Height = workArea.Height;
    }

    this.schemas = new ObservableCollection<SchemaInfo>();
    this.schemaService = new SchemaService();
    this.dataEntryService = new DataEntryService();

    if (this.TryLoadConfiguration() is false)
    {
      return;
    }

    this.schemaListBox.ItemsSource = this.schemas;
    this.schemaView = CollectionViewSource.GetDefaultView(this.schemas);
    this.schemaView.SortDescriptions.Add(new SortDescription(nameof(SchemaInfo.Name), ListSortDirection.Ascending));
    this.RefreshSchemaList();
  }

  private bool TryLoadConfiguration()
  {
    this.configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var configLoader = new AppConfigLoader();
    var result = configLoader.Load(this.configPath);

    if (result.IsValid is false)
    {
      MessageBox.Show(this, result.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
      Application.Current.Shutdown();
      return false;
    }

    this.outputRootPath = result.OutputRootPath;
    this.configuredDomains = result.Domains;
    this.schemaDirectory = Path.Combine(result.OutputRootPath, "schema");
    this.internalDataDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DatasheetGenerator",
      "data");
    this.excelDirectory = Path.Combine(result.OutputRootPath, "Excel");

    Directory.CreateDirectory(result.OutputRootPath);
    Directory.CreateDirectory(this.schemaDirectory);
    Directory.CreateDirectory(this.internalDataDirectory);
    Directory.CreateDirectory(this.excelDirectory);

    return true;
  }

  private void RefreshSchemaList()
  {
    var selected = this.selectedSchema;
    this.schemas.Clear();

    foreach (var schema in this.schemaService.GetSchemaFiles(this.schemaDirectory))
    {
      this.schemas.Add(schema);
    }

    if (selected is not null)
    {
      var restored = this.schemas.FirstOrDefault(s => s.FilePath == selected.FilePath);
      if (restored is not null)
      {
        this.schemaListBox.SelectedItem = restored;
      }
    }

    this.SetStatus($"스키마 {this.schemas.Count}개 로드됨.");
  }

  private void SchemaSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    this.selectedSchema = this.schemaListBox.SelectedItem as SchemaInfo;
    this.RefreshInfoPanel();
  }

  private void RefreshInfoPanel()
  {
    var hasSelection = this.selectedSchema is not null;
    this.schemaGraphButton.IsEnabled = hasSelection;
    this.editSchemaButton.IsEnabled = hasSelection;
    this.dataEntryButton.IsEnabled = hasSelection;
    this.renameSchemaButton.IsEnabled = hasSelection;
    this.deleteSchemaButton.IsEnabled = hasSelection;
    this.schemaInfoPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
    this.noSelectionTextBlock.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;

    if (this.selectedSchema is null)
    {
      return;
    }

    this.schemaNameTextBlock.Text = this.selectedSchema.Name;

    try
    {
      var schemaText = this.schemaService.LoadSchemaText(this.selectedSchema.FilePath);
      var columns = this.schemaService.ParseSchema(schemaText);
      this.columnPreviewList.ItemsSource = this.BuildColumnPreview(columns, string.Empty);
    }
    catch
    {
      this.columnPreviewList.ItemsSource = new[] { "(스키마 파싱 실패)" };
    }
  }

  private IReadOnlyList<string> BuildColumnPreview(IReadOnlyList<SchemaColumn> columns, string prefix)
  {
    var items = new List<string>();
    foreach (var col in columns)
    {
      var label = string.IsNullOrEmpty(prefix) ? col.Name : $"{prefix} > {col.Name}";
      if (col.JsonType is "object")
      {
        items.AddRange(this.BuildColumnPreview(col.Children, label));
      }
      else
      {
        items.Add($"• {label} ({col.JsonType})");
      }
    }

    return items;
  }

  private void SchemaSearchChanged(object sender, TextChangedEventArgs e)
  {
    if (this.schemaView is null)
    {
      return;
    }

    var query = this.schemaSearchBox.Text.Trim();
    if (string.IsNullOrEmpty(query))
    {
      this.schemaView.Filter = null;
    }
    else
    {
      this.schemaView.Filter = item => item is SchemaInfo schema &&
        schema.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
  }

  private void RefreshSchemaListClick(object sender, RoutedEventArgs e)
  {
    this.RefreshSchemaList();
  }

  private void ClearSchemaListClick(object sender, RoutedEventArgs e)
  {
    this.schemas.Clear();
    this.selectedSchema = null;
    this.schemaListBox.SelectedItem = null;
    this.RefreshInfoPanel();
    this.SetStatus("목록을 초기화했습니다.");
  }

  private void LoadSchemaClick(object sender, RoutedEventArgs e)
  {
    var dialog = new OpenFileDialog
    {
      Title = "스키마 불러오기",
      Filter = "스키마 파일 (*.schema.json)|*.schema.json",
      InitialDirectory = this.schemaDirectory
    };

    if (dialog.ShowDialog(this) is not true)
    {
      return;
    }

    var filePath = dialog.FileName;
    var existing = this.schemas.FirstOrDefault(s =>
      string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    if (existing is not null)
    {
      this.schemaListBox.SelectedItem = existing;
    }
    else
    {
      var nameWithExt = Path.GetFileNameWithoutExtension(filePath);
      var name = nameWithExt.EndsWith(".schema", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(nameWithExt)
        : nameWithExt;

      var schema = new SchemaInfo { Name = name, FilePath = filePath };
      this.schemas.Add(schema);
      this.schemaListBox.SelectedItem = schema;
    }

    this.schemaListBox.ScrollIntoView(this.schemaListBox.SelectedItem);
  }

  private void NewSchemaClick(object sender, RoutedEventArgs e)
  {
    var window = new SchemaEditorWindow(this.schemaService, this.schemaDirectory, this.configuredDomains)
    {
      Owner = this
    };

    window.ShowDialog();

    if (window.WasSaved)
    {
      this.RefreshSchemaList();
      this.SelectSchemaByPath(window.SavedSchemaPath);
    }
  }

  private void EditSchemaClick(object sender, RoutedEventArgs e)
  {
    if (this.EnsureSelectedSchemaExists() is false)
    {
      return;
    }

    var window = new SchemaEditorWindow(this.schemaService, this.schemaDirectory, this.configuredDomains, this.selectedSchema!.FilePath)
    {
      Owner = this
    };

    window.ShowDialog();

    if (window.WasSaved)
    {
      this.RefreshSchemaList();
      this.RefreshInfoPanel();
    }
  }

  private void SchemaGraphClick(object sender, RoutedEventArgs e)
  {
    if (this.EnsureSelectedSchemaExists() is false)
    {
      return;
    }

    var window = new SchemaGraphWindow(this.selectedSchema!.Name, this.schemaDirectory)
    {
      Owner = this
    };

    window.ShowDialog();
  }

  private void DataEntryClick(object sender, RoutedEventArgs e)
  {
    if (this.EnsureSelectedSchemaExists() is false)
    {
      return;
    }

    IReadOnlyList<SchemaColumn> schemaColumns;
    IReadOnlyList<string> schemaDomains;
    PivotInfo? pivotInfo;
    try
    {
      var schemaText = this.schemaService.LoadSchemaText(this.selectedSchema!.FilePath);
      schemaColumns = this.schemaService.ParseSchema(schemaText);
      schemaDomains = this.schemaService.ParseSchemaDomains(schemaText);
      pivotInfo = this.schemaService.ParsePivotInfo(schemaText);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "스키마 로드 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    var flatColumns = this.dataEntryService.GetFlatColumns(schemaColumns);
    var jsonPath = Path.Combine(this.internalDataDirectory, $"{this.selectedSchema!.Name}.json");
    var xlsxPath = Path.Combine(this.excelDirectory, $"{this.selectedSchema!.Name}.xlsx");

    var pathProvider = new ExportPathProvider(this.outputRootPath);
    var domainJsonPaths = schemaDomains
      .ToDictionary(d => d, d => pathProvider.GetDomainJsonPath(this.selectedSchema!.Name, d));

    var refValues = new Dictionary<string, IReadOnlySet<string>>();
    foreach (var col in flatColumns)
    {
      if (col.Ref is null)
      {
        continue;
      }

      var values = this.dataEntryService.LoadRefValues(this.internalDataDirectory, col.Ref);
      if (values.Count > 0)
      {
        refValues[col.Path] = values;
      }
    }

    DataTable dataTable;
    try
    {
      dataTable = this.dataEntryService.LoadData(jsonPath, flatColumns, pivotInfo);
      if (dataTable.Rows.Count is 0 && File.Exists(jsonPath) is false)
      {
        foreach (var domainPath in domainJsonPaths.Values)
        {
          if (File.Exists(domainPath))
          {
            dataTable = this.dataEntryService.LoadData(domainPath, flatColumns, pivotInfo);
            break;
          }
        }
      }
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "데이터 로드 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    var window = new DataEntryWindow(
      this.selectedSchema.Name,
      schemaColumns,
      flatColumns,
      dataTable,
      this.dataEntryService,
      jsonPath,
      xlsxPath,
      pivotInfo,
      refValues,
      schemaDomains,
      domainJsonPaths)
    {
      Owner = this
    };

    window.ShowDialog();
  }

  private void SelectSchemaByPath(string? path)
  {
    if (path is null)
    {
      return;
    }

    var schema = this.schemas.FirstOrDefault(s => s.FilePath == path);
    if (schema is not null)
    {
      this.schemaListBox.SelectedItem = schema;
    }
  }

  private void OpenConfigFileClick(object sender, RoutedEventArgs e)
  {
    Process.Start(new ProcessStartInfo(this.configPath) { UseShellExecute = true });
  }

  private void RenameSchemaClick(object sender, RoutedEventArgs e)
  {
    if (this.selectedSchema is null)
    {
      return;
    }

    var currentName = this.selectedSchema.Name;
    var newName = PromptDialog.Show(this, "스키마 이름 변경", "새 이름을 입력하세요.", currentName);

    if (newName is null)
    {
      return;
    }

    newName = newName.Trim();

    if (string.IsNullOrWhiteSpace(newName))
    {
      MessageBox.Show(this, "이름을 입력하세요.", "이름 변경 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    if (newName == currentName)
    {
      return;
    }

    if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
      MessageBox.Show(this, "사용할 수 없는 문자가 포함되어 있습니다.", "이름 변경 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    IReadOnlyList<string> schemaDomains = [];
    try
    {
      var schemaText = this.schemaService.LoadSchemaText(this.selectedSchema!.FilePath);
      schemaDomains = this.schemaService.ParseSchemaDomains(schemaText);
    }
    catch { }

    var domainDirectories = schemaDomains
      .Select(d => Path.Combine(this.outputRootPath, d))
      .ToList();

    var result = this.schemaService.RenameSchema(currentName, newName, this.schemaDirectory, this.internalDataDirectory, this.excelDirectory, domainDirectories);

    if (result.IsValid is false)
    {
      MessageBox.Show(this, result.Message, "이름 변경 실패", MessageBoxButton.OK, MessageBoxImage.Error);
      return;
    }

    var newSchemaPath = Path.Combine(this.schemaDirectory, $"{newName}.schema.json");
    this.RefreshSchemaList();
    this.SelectSchemaByPath(newSchemaPath);
  }

  private void DeleteSchemaClick(object sender, RoutedEventArgs e)
  {
    if (this.selectedSchema is null)
    {
      return;
    }

    var confirm = MessageBox.Show(
      this,
      $"'{this.selectedSchema.Name}' 스키마를 삭제하시겠습니까?\n스키마, JSON, Excel 파일이 모두 삭제됩니다.",
      "스키마 삭제",
      MessageBoxButton.YesNo,
      MessageBoxImage.Warning);

    if (confirm is not MessageBoxResult.Yes)
    {
      return;
    }

    var name = this.selectedSchema.Name;
    var filesToDelete = new List<string>
    {
      this.selectedSchema.FilePath,
      Path.Combine(this.internalDataDirectory, $"{name}.json"),
      Path.Combine(this.excelDirectory, $"{name}.xlsx"),
    };

    foreach (var domain in this.configuredDomains)
    {
      filesToDelete.Add(Path.Combine(this.outputRootPath, domain, $"{name}.json"));
    }

    try
    {
      foreach (var path in filesToDelete)
      {
        if (File.Exists(path))
        {
          File.Delete(path);
        }
      }
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "삭제 실패", MessageBoxButton.OK, MessageBoxImage.Error);
      return;
    }

    this.RefreshSchemaList();
  }

  private void OpenSchemaFolderClick(object sender, RoutedEventArgs e)
  {
    Process.Start("explorer.exe", this.outputRootPath);
  }

  private bool EnsureSelectedSchemaExists()
  {
    if (this.selectedSchema is null)
    {
      return false;
    }

    if (File.Exists(this.selectedSchema.FilePath))
    {
      return true;
    }

    MessageBox.Show(this, $"'{this.selectedSchema.Name}' 스키마 파일이 존재하지 않습니다.", "파일 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
    this.schemas.Remove(this.selectedSchema);
    this.selectedSchema = null;
    this.schemaListBox.SelectedItem = null;
    this.RefreshInfoPanel();
    return false;
  }

  private void SetStatus(string message)
  {
    this.statusTextBlock.Text = message;
  }
}
