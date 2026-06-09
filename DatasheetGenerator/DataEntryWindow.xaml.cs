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
      <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        html, body { height: 100%; overflow: hidden; background: #fff; color: #000; }
        #grid { width: 100%; height: 100%; display: block; }
        #ctx-menu {
          display: none; position: fixed; z-index: 9999;
          background: #fff; border: 1px solid #aaa;
          box-shadow: 2px 2px 6px rgba(0,0,0,0.18);
          padding: 4px 0; min-width: 160px; font: 13px sans-serif;
        }
        .ctx-item { padding: 6px 16px; cursor: pointer; }
        .ctx-item:hover { background: #e8f0fe; }
        .ctx-sep { margin: 4px 0; border: none; border-top: 1px solid #ddd; }
      </style>
    </head>
    <body>
      <revo-grid id="grid"></revo-grid>
      <div id="ctx-menu">
        <div class="ctx-item" id="ctx-row-above">Insert row above</div>
        <div class="ctx-item" id="ctx-row-below">Insert row below</div>
        <hr class="ctx-sep">
        <div class="ctx-item" id="ctx-copy">Copy</div>
        <div class="ctx-item" id="ctx-cut">Cut</div>
      </div>
      <script>
        // Synchronous stub: queues the C# initialize() call until the async module is ready.
        let _pendingInit = null;
        window.initialize = (data) => { _pendingInit = data; };

        // Set autoSizeColumn before the custom element is defined so Stencil
        // picks it up as a pre-upgrade own property during componentWillLoad.
        document.getElementById('grid').autoSizeColumn = { allColumns: true, mode: 'autoSizeOnTextOverlap' };

        (async () => {
          await import('https://app.local/revogrid/revo-grid.esm.js');
          const { h } = await import('https://app.local/revogrid/index.esm.js');

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

          let grid = null;
          let initColumns = [];
          let sourceData = [];
          let currentSelection = null;
          let ctrlHeld = false;

          document.addEventListener('keydown', e => { if (e.ctrlKey) ctrlHeld = true; });
          document.addEventListener('keyup', e => { if (!e.ctrlKey) ctrlHeld = false; });
          document.addEventListener('blur', () => { ctrlHeld = false; });

          function buildColumnTree(flatCols, colorMap) {
            const result = [];
            let l1Node = null, l1Name = null, l2Node = null, l2Name = null;
            for (const col of flatCols) {
              const colors = colorMap[col.level1Group];
              const leafName = col.leafName;
              const leafProp = col.path;
              const leaf = {
                name: leafName,
                prop: leafProp,
                ...(colors && {
                  columnTemplate: () => h('div', {
                    style: { padding: '0 4px', backgroundColor: colors.leaf, width: '100%', height: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
                  }, leafName)
                })
              };
              if (!col.level1Group) {
                l1Node = null; l1Name = null; l2Node = null; l2Name = null;
                result.push(leaf);
              } else {
                if (col.level1Group !== l1Name) {
                  const gn = col.level1Group;
                  const gc = colorMap[gn];
                  l1Node = {
                    name: gn,
                    children: [],
                    ...(gc && {
                      columnTemplate: () => h('div', {
                        style: { padding: '0 4px', backgroundColor: gc.l1, width: '100%', height: '100%' }
                      }, gn)
                    })
                  };
                  l1Name = gn; l2Node = null; l2Name = null;
                  result.push(l1Node);
                }
                if (!col.level2Group) {
                  l2Node = null; l2Name = null;
                  l1Node.children.push(leaf);
                } else {
                  if (col.level2Group !== l2Name) {
                    const g2n = col.level2Group;
                    const g2c = colorMap[l1Name];
                    l2Node = {
                      name: g2n,
                      children: [],
                      ...(g2c && {
                        columnTemplate: () => h('div', {
                          style: { padding: '0 4px', backgroundColor: g2c.l2, width: '100%', height: '100%' }
                        }, g2n)
                      })
                    };
                    l2Name = g2n;
                    l1Node.children.push(l2Node);
                  }
                  l2Node.children.push(leaf);
                }
              }
            }
            return result;
          }

          function setupContextMenu() {
            const menu = document.getElementById('ctx-menu');
            // Prevent the grid from losing focus when interacting with the menu.
            menu.addEventListener('mousedown', e => { e.preventDefault(); });
            document.addEventListener('contextmenu', e => {
              if (!grid || !grid.contains(e.target)) { return; }
              e.preventDefault();
              menu.style.display = 'block';
              menu.style.left = e.clientX + 'px';
              menu.style.top = e.clientY + 'px';
            });
            document.addEventListener('click', () => { menu.style.display = 'none'; });
            document.addEventListener('keydown', e => { if (e.key === 'Escape') menu.style.display = 'none'; });
            document.getElementById('ctx-row-above').addEventListener('click', () => { menu.style.display = 'none'; insertRow('above'); });
            document.getElementById('ctx-row-below').addEventListener('click', () => { menu.style.display = 'none'; insertRow('below'); });
            document.getElementById('ctx-copy').addEventListener('click', () => {
              menu.style.display = 'none';
              document.dispatchEvent(new ClipboardEvent('copy', { bubbles: true, cancelable: true }));
            });
            document.getElementById('ctx-cut').addEventListener('click', () => {
              menu.style.display = 'none';
              document.dispatchEvent(new ClipboardEvent('cut', { bubbles: true, cancelable: true }));
            });
          }

          function insertRow(direction) {
            const newRow = initColumns.reduce((obj, col) => { obj[col.path] = ''; return obj; }, {});
            if (sourceData.length === 0) {
              sourceData.push(newRow);
            } else if (currentSelection) {
              const idx = direction === 'above'
                ? Math.min(currentSelection.y, currentSelection.y1)
                : Math.max(currentSelection.y, currentSelection.y1) + 1;
              sourceData.splice(idx, 0, newRow);
            } else {
              sourceData.push(newRow);
            }
            grid.source = [...sourceData];
          }

          function initialize(data) {
            initColumns = data.columns;
            const colorMap = {};
            [...new Set(initColumns.map(c => c.level1Group).filter(Boolean))].forEach((g, i) => {
              colorMap[g] = COLOR_SETS[i % COLOR_SETS.length];
            });
            sourceData = (data.rows || []).map(row =>
              initColumns.reduce((obj, col, i) => { obj[col.path] = row[i] ?? ''; return obj; }, {})
            );
            grid = document.getElementById('grid');
            grid.columns = buildColumnTree(initColumns, colorMap);
            grid.source = [...sourceData];
            grid.rowHeaders = true;
            grid.range = true;
            grid.resize = true;
            grid.stretch = 'all';
            grid.theme = 'default';
            grid.hideAttribution = true;
            grid.applyOnClose = true;

            grid.addEventListener('celleditapply', e => {
              const { rowIndex, prop, val } = e.detail;
              if (sourceData[rowIndex] !== undefined) { sourceData[rowIndex][prop] = val; }
            });

            grid.addEventListener('rangeeditapply', e => {
              const { data } = e.detail;
              if (!data) { return; }
              for (const rowIndex in data) {
                const i = parseInt(rowIndex, 10);
                if (sourceData[i] !== undefined) { Object.assign(sourceData[i], data[rowIndex]); }
              }
            });

            // Track current selection range for row operations and autofill source.
            grid.addEventListener('setrange', e => {
              if (e.detail && 'x' in e.detail) { currentSelection = e.detail; }
            });

            // Ctrl+drag autofill: fills target range with numeric step computed from source.
            grid.addEventListener('beforerangedataapply', async e => {
              if (!ctrlHeld || !currentSelection) { return; }
              e.preventDefault();
              const fillRange = e.detail.range;
              const srcRange = currentSelection;
              const srcR0 = Math.min(srcRange.y, srcRange.y1);
              const srcR1 = Math.max(srcRange.y, srcRange.y1);
              const srcC0 = Math.min(srcRange.x, srcRange.x1);
              const srcC1 = Math.max(srcRange.x, srcRange.x1);
              const tgtR0 = Math.min(fillRange.y, fillRange.y1);
              const tgtR1 = Math.max(fillRange.y, fillRange.y1);
              const tgtC0 = Math.min(fillRange.x, fillRange.x1);
              const tgtC1 = Math.max(fillRange.x, fillRange.x1);
              const isDown = tgtR1 > srcR1;
              const isUp = tgtR0 < srcR0;
              const isRight = tgtC1 > srcC1;
              const selectionData = [];
              for (let r = srcR0; r <= srcR1; r++) {
                const row = [];
                for (let c = srcC0; c <= srcC1; c++) {
                  const prop = initColumns[c]?.path;
                  row.push(prop ? (sourceData[r]?.[prop] ?? '') : '');
                }
                selectionData.push(row);
              }
              const srcRows = selectionData.length;
              const srcCols = (selectionData[0] ?? []).length;
              if (!srcRows || !srcCols) { return; }
              let fillR0, fillR1, fillC0, fillC1;
              if (isDown)       { fillR0 = srcR1 + 1; fillR1 = tgtR1; fillC0 = tgtC0; fillC1 = tgtC1; }
              else if (isUp)    { fillR0 = tgtR0; fillR1 = srcR0 - 1; fillC0 = tgtC0; fillC1 = tgtC1; }
              else if (isRight) { fillR0 = tgtR0; fillR1 = tgtR1; fillC0 = srcC1 + 1; fillC1 = tgtC1; }
              else              { fillR0 = tgtR0; fillR1 = tgtR1; fillC0 = tgtC0; fillC1 = srcC0 - 1; }
              const tgtRows = fillR1 - fillR0 + 1;
              const tgtCols = fillC1 - fillC0 + 1;
              if (tgtRows <= 0 || tgtCols <= 0) { return; }
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
              for (let r = 0; r < tgtRows; r++) {
                for (let c = 0; c < tgtCols; c++) {
                  const rowIdx = fillR0 + r;
                  const colIdx = fillC0 + c;
                  const val = result[r][c];
                  const prop = initColumns[colIdx]?.path;
                  if (prop && sourceData[rowIdx] !== undefined) { sourceData[rowIdx][prop] = val; }
                  await grid.setDataAt({ row: rowIdx, col: colIdx, val });
                }
              }
            });

            setupContextMenu();
          }

          function addRow() {
            insertRow('below');
          }

          function getSelectedRowsInfo() {
            if (!currentSelection) { return { hasData: false, rowCount: 0 }; }
            const minRow = Math.min(currentSelection.y, currentSelection.y1);
            const maxRow = Math.max(currentSelection.y, currentSelection.y1);
            const rowCount = maxRow - minRow + 1;
            let hasData = false;
            for (let r = minRow; r <= maxRow; r++) {
              const row = sourceData[r];
              if (row && Object.values(row).some(v => v !== null && v !== '' && v !== undefined)) {
                hasData = true;
                break;
              }
            }
            return { hasData, rowCount };
          }

          function deleteSelectedRows() {
            if (!currentSelection) { return; }
            const minRow = Math.min(currentSelection.y, currentSelection.y1);
            const maxRow = Math.max(currentSelection.y, currentSelection.y1);
            sourceData.splice(minRow, maxRow - minRow + 1);
            grid.source = [...sourceData];
          }

          function getTableData() {
            return sourceData.map(obj =>
              initColumns.map(col => {
                const v = obj[col.path];
                return (v === null || v === undefined) ? '' : String(v);
              })
            );
          }

          // Expose all C#-callable functions to the global scope.
          window.initialize = initialize;
          window.addRow = addRow;
          window.getSelectedRowsInfo = getSelectedRowsInfo;
          window.deleteSelectedRows = deleteSelectedRows;
          window.getTableData = getTableData;

          // Flush queued initialize call from C# (if it arrived before module was ready).
          if (_pendingInit !== null) { initialize(_pendingInit); _pendingInit = null; }
        })();
      </script>
    </body>
    </html>
    """;

  public bool IsReady { get; private set; }
  private bool isSaving;
  private bool isOutputPathValid;
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
  private readonly string schemaName;
  private readonly string codeOutputPath;

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
    IReadOnlyDictionary<string, string> domainJsonPaths,
    string codeOutputPath = "")
  {
    this.InitializeComponent();
    this.schemaName = schemaName;
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
    this.codeOutputPath = codeOutputPath;

    this.isOutputPathValid = string.IsNullOrEmpty(xlsxPath) is false;
    if (this.isOutputPathValid)
    {
      this.savePathText.Text = $"출력: {xlsxPath}";
    }
    else
    {
      this.savePathText.Text = "OutputRootPath가 설정되지 않았습니다. Config에 추가하세요.";
    }

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
    this.saveButton.IsEnabled = this.isOutputPathValid;
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

  private void CodeGenClick(object sender, RoutedEventArgs e)
  {
    try
    {
      var codeService = new CodeGenerationService();
      var code = codeService.GenerateCode(this.schemaName, this.schemaColumns);

      string? outputFilePath = null;
      if (string.IsNullOrEmpty(this.codeOutputPath) is false)
      {
        outputFilePath = Path.Combine(this.codeOutputPath, $"{this.schemaName}GameData.cs");
      }

      var window = new CodePreviewWindow(this.schemaName, code, outputFilePath)
      {
        Owner = this
      };
      window.ShowDialog();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, ex.Message, "코드 생성 실패", MessageBoxButton.OK, MessageBoxImage.Error);
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
