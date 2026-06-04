namespace DatasheetGenerator;

using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using DatasheetGenerator.Models;
using DatasheetGenerator.Services;
using Newtonsoft.Json;

public partial class DataEntryWindow : Window
{
  private const string HtmlContent = """
    <!DOCTYPE html>
    <html><head>
      <meta charset="utf-8">
      <link rel="stylesheet" href="https://app.local/handsontable.full.min.css">
      <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        html, body { height: 100%; overflow: hidden; }
        #hot { width: 100%; height: 100%; }
      </style>
    </head>
    <body>
      <div id="hot"></div>
      <script src="https://app.local/handsontable.full.min.js"></script>
      <script>
        const COLOR_SETS = [
          { l1: '#FFB347', l2: '#FFCA76', leaf: '#FFDFB0' },
          { l1: '#74B9E8', l2: '#95CAF0', leaf: '#B8DCF5' },
          { l1: '#74C774', l2: '#96D896', leaf: '#B8E8B8' },
          { l1: '#B094D4', l2: '#C4B0E0', leaf: '#D8CCEC' },
          { l1: '#F08080', l2: '#F5A0A0', leaf: '#FAC0C0' },
          { l1: '#5BC0AE', l2: '#80D0C2', leaf: '#A5DFD6' },
          { l1: '#E8C840', l2: '#EDD470', leaf: '#F2E0A0' },
          { l1: '#F09858', l2: '#F5B280', leaf: '#FACBA8' },
          { l1: '#A098D8', l2: '#B8B2E4', leaf: '#D0CCED' },
          { l1: '#60D0A8', l2: '#80DCBC', leaf: '#A0E8D0' }
        ];

        let hot = null;
        let initColumns = [];
        const colorMap = {};
        let ctrlHeld = false;
        document.addEventListener('keydown', e => { if (e.ctrlKey) ctrlHeld = true; });
        document.addEventListener('keyup', e => { if (!e.ctrlKey) ctrlHeld = false; });
        document.addEventListener('blur', () => { ctrlHeld = false; });

        function initialize(data) {
          initColumns = data.columns;

          const level1Groups = [...new Set(initColumns.map(c => c.level1Group).filter(Boolean))];
          level1Groups.forEach((group, i) => {
            colorMap[group] = COLOR_SETS[i % COLOR_SETS.length];
          });

          const nestedHeaders = buildNestedHeaders(initColumns);
          const rowData = (data.rows || []).map(row => row.map(cell => cell ?? ''));

          hot = new Handsontable(document.getElementById('hot'), {
            data: rowData,
            nestedHeaders: nestedHeaders,
            rowHeaders: true,
            columns: initColumns.map(() => ({ type: 'text' })),
            licenseKey: 'non-commercial-and-evaluation',
            contextMenu: ['row_above', 'row_below', 'separator', 'copy', 'cut'],
            manualColumnResize: true,
            stretchH: 'all',
            height: '100%',
            fillHandle: { autoInsertRow: false },
            beforeAutofill: function(selectionData, sourceRange, targetRange, direction) {
              if (!ctrlHeld) { return; }
              const srcRows = selectionData.length;
              const srcCols = (selectionData[0] ?? []).length;
              if (!srcRows || !srcCols) { return; }
              const tgtRows = targetRange.to.row - targetRange.from.row + 1;
              const tgtCols = targetRange.to.col - targetRange.from.col + 1;
              const isDown = direction === 'down';
              const isUp = direction === 'up';
              const isRight = direction === 'right';
              function numericStep(vals) {
                const nums = vals.map(v => parseFloat(v));
                if (nums.some(n => isNaN(n))) { return null; }
                return nums.length > 1 ? nums[nums.length - 1] - nums[nums.length - 2] : 1;
              }
              function fillVal(lastNum, step, offset, isInt) {
                const v = lastNum + step * offset;
                return isInt ? String(Math.round(v)) : String(v);
              }
              const result = [];
              if (isDown || isUp) {
                for (let r = 0; r < tgtRows; r++) {
                  const row = [];
                  for (let c = 0; c < tgtCols; c++) {
                    const colVals = selectionData.map(sr => sr[c % srcCols]);
                    const step = numericStep(colVals);
                    if (step === null) {
                      row.push(colVals[r % srcRows]);
                    } else {
                      const lastNum = parseFloat(colVals[srcRows - 1]);
                      const isInt = colVals.every(v => Number.isInteger(parseFloat(v)));
                      row.push(fillVal(lastNum, step, isDown ? r + 1 : -(r + 1), isInt));
                    }
                  }
                  result.push(row);
                }
              } else {
                for (let r = 0; r < tgtRows; r++) {
                  const row = [];
                  const rowVals = selectionData[r % srcRows];
                  const step = numericStep(rowVals);
                  for (let c = 0; c < tgtCols; c++) {
                    if (step === null) {
                      row.push(rowVals[c % srcCols]);
                    } else {
                      const lastNum = parseFloat(rowVals[srcCols - 1]);
                      const isInt = rowVals.every(v => Number.isInteger(parseFloat(v)));
                      row.push(fillVal(lastNum, step, isRight ? c + 1 : -(c + 1), isInt));
                    }
                  }
                  result.push(row);
                }
              }
              return result;
            },
            afterGetColHeader: applyHeaderColors
          });
        }

        function buildNestedHeaders(columns) {
          const rows = [
            buildGroupRow(columns, 'level1Group'),
            buildGroupRow(columns, 'level2Group'),
            columns.map(c => c.leafName)
          ];
          return rows.filter(row => row.some(cell =>
            typeof cell === 'string' ? cell !== '' : (cell?.label ?? '') !== ''
          ));
        }

        function buildGroupRow(columns, groupKey) {
          const row = [];
          let i = 0;
          while (i < columns.length) {
            const group = columns[i][groupKey];
            if (group) {
              let j = i + 1;
              while (j < columns.length && columns[j][groupKey] === group && columns[j].level1Group === columns[i].level1Group) {
                j++;
              }
              const colspan = j - i;
              row.push(colspan > 1 ? { label: group, colspan } : group);
              i = j;
            } else {
              row.push('');
              i++;
            }
          }
          return row;
        }

        function applyHeaderColors(col, TH, headerLevel) {
          if (col < 0 || col >= initColumns.length) return;
          const group = initColumns[col].level1Group;
          if (!group) return;
          const colorSet = colorMap[group];
          if (!colorSet) return;
          if (headerLevel === 0) TH.style.backgroundColor = colorSet.l1;
          else if (headerLevel === 1) TH.style.backgroundColor = colorSet.l2;
          else if (headerLevel === 2) TH.style.backgroundColor = colorSet.leaf;
        }

        function addRow() {
          if (!hot) return;
          const count = hot.countRows();
          if (count === 0) {
            hot.alter('insert_row_above', 0, 1);
            return;
          }
          const selected = hot.getSelected();
          if (selected && selected.length > 0) {
            const maxRow = selected.reduce((max, [r1, , r2]) => Math.max(max, r1, r2), -1);
            hot.alter('insert_row_below', maxRow, 1);
          } else {
            hot.alter('insert_row_below', count - 1, 1);
          }
        }

        function getSelectedRowsInfo() {
          if (!hot) return { hasData: false, rowCount: 0 };
          const selected = hot.getSelected();
          if (!selected || selected.length === 0) return { hasData: false, rowCount: 0 };
          const rowIndices = new Set();
          for (const [startRow, , endRow] of selected) {
            const min = Math.min(startRow, endRow);
            const max = Math.max(startRow, endRow);
            for (let i = min; i <= max; i++) rowIndices.add(i);
          }
          const hasData = [...rowIndices].some(i =>
            hot.getDataAtRow(i).some(cell => cell !== null && cell !== '' && cell !== undefined));
          return { hasData, rowCount: rowIndices.size };
        }

        function deleteSelectedRows() {
          if (!hot) return;
          const selected = hot.getSelected();
          if (!selected || selected.length === 0) return;
          const rowIndices = new Set();
          for (const [startRow, , endRow] of selected) {
            const min = Math.min(startRow, endRow);
            const max = Math.max(startRow, endRow);
            for (let i = min; i <= max; i++) rowIndices.add(i);
          }
          [...rowIndices].sort((a, b) => b - a).forEach(i => hot.alter('remove_row', i, 1));
        }

        function getTableData() {
          if (!hot) return [];
          return hot.getData().map(row => row.map(cell => (cell === null || cell === undefined) ? '' : String(cell)));
        }
      </script>
    </body>
    </html>
    """;

  public bool IsReady { get; private set; }
  private bool isSaving;
  private readonly DataEntryService entryService;
  private readonly IReadOnlyList<SchemaColumn> schemaColumns;
  private readonly IReadOnlyList<FlatColumn> flatColumns;
  private readonly DataTable dataTable;
  private readonly string jsonPath;
  private readonly string xlsxPath;
  private readonly PivotInfo? pivotInfo;
  private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> refValues;
  private readonly IReadOnlyList<string> schemaDomains;
  private readonly IReadOnlyDictionary<string, string> domainJsonPaths;

  public DataEntryWindow(
    string schemaName,
    IReadOnlyList<SchemaColumn> schemaColumns,
    IReadOnlyList<FlatColumn> flatColumns,
    DataTable dataTable,
    DataEntryService entryService,
    string jsonPath,
    string xlsxPath,
    PivotInfo? pivotInfo,
    IReadOnlyDictionary<string, IReadOnlySet<string>> refValues,
    IReadOnlyList<string> schemaDomains,
    IReadOnlyDictionary<string, string> domainJsonPaths)
  {
    this.InitializeComponent();
    this.Title = $"Data Entry - {schemaName}";
    this.entryService = entryService;
    this.schemaColumns = schemaColumns;
    this.flatColumns = flatColumns;
    this.dataTable = dataTable;
    this.jsonPath = jsonPath;
    this.xlsxPath = xlsxPath;
    this.pivotInfo = pivotInfo;
    this.refValues = refValues;
    this.schemaDomains = schemaDomains;
    this.domainJsonPaths = domainJsonPaths;

    this.Loaded += async (s, e) => await this.InitializeWebViewAsync();
    this.Closed += (s, e) => this.webView.Dispose();
  }

  private async Task InitializeWebViewAsync()
  {
    try
    {
      var userDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DatasheetGenerator", "WebView2");
      var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
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
      this.webView.NavigationCompleted += this.OnNavigationCompleted;
      this.webView.CoreWebView2.NavigateToString(HtmlContent);
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

  private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
  {
    this.webView.NavigationCompleted -= this.OnNavigationCompleted;
    if (e.IsSuccess is false)
    {
      MessageBox.Show(this, "Failed to load the interface.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
      this.Close();
      return;
    }
    await this.webView.CoreWebView2.ExecuteScriptAsync($"initialize({this.BuildInitDataJson()})");
    this.IsReady = true;
    this.addRowButton.IsEnabled = true;
    this.deleteRowButton.IsEnabled = true;
    this.validateButton.IsEnabled = true;
    this.saveButton.IsEnabled = true;
  }

  private string BuildInitDataJson()
  {
    var columns = this.flatColumns.Select(col => new
    {
      path = col.Path,
      leafName = col.ShowRequiredMark ? $"★ {col.LeafName}" : col.LeafName,
      level1Group = col.Level1Group,
      level2Group = col.Level2Group,
      jsonType = col.JsonType,
      isRequired = col.IsRequired
    });

    var rows = new List<List<string?>>();
    foreach (DataRow row in this.dataTable.Rows)
    {
      var cells = this.flatColumns.Select(col =>
      {
        var val = row[col.Path];
        return val is DBNull ? null : Convert.ToString(val);
      }).ToList();
      rows.Add(cells);
    }

    return JsonConvert.SerializeObject(
      new { columns, rows },
      new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii });
  }

  private async void AddRowClick(object sender, RoutedEventArgs e)
  {
    try
    {
      if (this.webView.CoreWebView2 is null) { return; }
      await this.webView.CoreWebView2.ExecuteScriptAsync("addRow()");
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async void DeleteRowClick(object sender, RoutedEventArgs e)
  {
    try
    {
      if (this.webView.CoreWebView2 is null) { return; }

      var infoJson = await this.webView.CoreWebView2.ExecuteScriptAsync("getSelectedRowsInfo()");
      var info = JsonConvert.DeserializeObject<SelectedRowsInfo>(infoJson)!;

      if (info.RowCount is 0) { return; }

      if (info.HasData)
      {
        var message = info.RowCount is 1
          ? "The selected row has data. Delete it?"
          : $"Some rows have data. Delete {info.RowCount} rows?";

        var confirm = MessageBox.Show(
          this,
          message,
          "Delete Row",
          MessageBoxButton.OKCancel,
          MessageBoxImage.Warning);

        if (confirm is not MessageBoxResult.OK) { return; }
      }

      await this.webView.CoreWebView2.ExecuteScriptAsync("deleteSelectedRows()");
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async void ValidateClick(object sender, RoutedEventArgs e)
  {
    try
    {
      if (this.webView.CoreWebView2 is null) { return; }

      var dataJson = await this.webView.CoreWebView2.ExecuteScriptAsync("getTableData()");
      var rows = JsonConvert.DeserializeObject<List<List<string?>>>(dataJson) ?? [];

      var tempTable = this.entryService.CreateDataTable(this.flatColumns);
      foreach (var rowData in rows.Where(r => r.Any(cell => string.IsNullOrEmpty(cell) is false)))
      {
        var row = tempTable.NewRow();
        for (var i = 0; i < this.flatColumns.Count; i++)
        {
          row[this.flatColumns[i].Path] = rowData.Count > i ? rowData[i] ?? string.Empty : string.Empty;
        }

        tempTable.Rows.Add(row);
      }

      var errors = this.entryService.ValidateAll(tempTable, this.flatColumns, this.refValues);

      if (errors.Count is 0)
      {
        MessageBox.Show(this, "검증을 통과했습니다.", "검증 완료", MessageBoxButton.OK, MessageBoxImage.Information);
      }
      else
      {
        MessageBox.Show(this, string.Join("\n", errors), "검증 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private void CancelClick(object sender, RoutedEventArgs e)
  {
    this.Close();
  }

  private async void SaveClick(object sender, RoutedEventArgs e)
  {
    if (this.webView.CoreWebView2 is null) { return; }
    if (this.isSaving) { return; }

    this.isSaving = true;
    try
    {
      var dataJson = await this.webView.CoreWebView2.ExecuteScriptAsync("getTableData()");
      var rows = JsonConvert.DeserializeObject<List<List<string?>>>(dataJson) ?? [];
      var nonEmptyRows = rows.Where(r => r.Any(cell => string.IsNullOrEmpty(cell) is false));

      this.dataTable.Rows.Clear();
      foreach (var rowData in nonEmptyRows)
      {
        var row = this.dataTable.NewRow();
        for (var i = 0; i < this.flatColumns.Count; i++)
        {
          row[this.flatColumns[i].Path] = rowData.Count > i ? rowData[i] ?? string.Empty : string.Empty;
        }

        this.dataTable.Rows.Add(row);
      }

      var validation = this.entryService.ValidateData(this.dataTable, this.flatColumns, this.refValues);
      if (validation.IsValid is false)
      {
        MessageBox.Show(this, validation.Message, "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      this.entryService.SaveData(this.dataTable, this.schemaColumns, this.flatColumns, this.jsonPath, this.xlsxPath, this.pivotInfo);

      foreach (var (domain, domainPath) in this.domainJsonPaths)
      {
        this.entryService.SaveDomainJson(this.dataTable, this.schemaColumns, this.schemaDomains, domain, domainPath, this.pivotInfo);
      }

      MessageBox.Show(this, "Saved successfully.", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
      this.Close();
    }
    finally
    {
      this.isSaving = false;
    }
  }

  private sealed record SelectedRowsInfo
  {
    public bool HasData { get; init; }
    public int RowCount { get; init; }
  }
}
