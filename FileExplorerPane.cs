using Terminal.Gui;

public enum SortColumn { Name, Size, Date }

public class FileExplorerPane
{
    public FrameView Frame { get; }
    public string CurrentPath { get; private set; }

    private readonly TextField _pathField;
    private readonly ListView _fileList;
    private readonly FileListHeaderView _headerView;
    private FileListDataSource? _dataSource;

    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true;

    public event Action<FileExplorerPane>? PathChanged;
    public event Action<FileExplorerPane>? GotFocus;
    public event Action<FileExplorerPane>? SelectionChanged;

    public FileExplorerPane(string initialPath)
    {
        CurrentPath = Path.GetFullPath(initialPath);

        Frame = new FrameView("")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _pathField = new TextField(CurrentPath)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            ColorScheme = new ColorScheme
            {
                Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                Focus     = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                HotFocus  = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                Disabled  = new Terminal.Gui.Attribute(Color.DarkGray,    Color.Black),
            }
        };

        _headerView = new FileListHeaderView
        {
            X = 0, Y = 1,
            Width = Dim.Fill(),
            Height = 1
        };

        _fileList = new ListView
        {
            X = 0, Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            ColorScheme = new ColorScheme
            {
                Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                Focus     = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
                HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                HotFocus  = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
                Disabled  = new Terminal.Gui.Attribute(Color.DarkGray,    Color.Black),
            }
        };

        Frame.Add(_pathField, _headerView, _fileList);

        _headerView.SortHeaderClicked += col =>
        {
            if (_sortColumn == col)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = col;
                _sortAscending = true;
            }
            _headerView.CurrentSort = _sortColumn;
            _headerView.SortAscending = _sortAscending;
            _headerView.SetNeedsDisplay();
            ApplySort();
        };

        _pathField.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.Enter)
            {
                NavigateTo(_pathField.Text?.ToString() ?? CurrentPath);
                _fileList.SetFocus();
                e.Handled = true;
            }
        };

        _fileList.OpenSelectedItem += OnItemOpen;
        _fileList.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.Backspace)
            {
                NavigateTo(Path.GetDirectoryName(CurrentPath) ?? CurrentPath);
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.Space)
            {
                ToggleMark(_fileList.SelectedItem);
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key.CtrlMask | Key.A))
            {
                ToggleSelectAll();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.CursorLeft)
            {
                _fileList.SelectedItem = Math.Max(0, _fileList.SelectedItem - 1);
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.CursorRight)
            {
                _fileList.SelectedItem = Math.Min(_fileList.Source.Count - 1, _fileList.SelectedItem + 1);
                e.Handled = true;
            }
        };
        _fileList.MouseClick += (e) =>
        {
            if (e.MouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked))
            {
                int idx = e.MouseEvent.Y + _fileList.TopItem;
                ToggleMark(idx);
                e.Handled = true;
            }
        };
        _fileList.Enter += (_) => GotFocus?.Invoke(this);
        _pathField.Enter += (_) => GotFocus?.Invoke(this);
        _fileList.SelectedItemChanged += (_) => SelectionChanged?.Invoke(this);

        LoadDirectory(CurrentPath);
    }

    private void OnItemOpen(ListViewItemEventArgs e)
    {
        var item = _dataSource?[e.Item];
        if (item == null) return;

        if (item.IsDirectory)
        {
            string newPath = item.Name == ".."
                ? Path.GetDirectoryName(CurrentPath) ?? CurrentPath
                : Path.Combine(CurrentPath, item.Name);
            NavigateTo(newPath);
        }
        else if (Platform.HasGraphicalEnvironment)
        {
            Platform.OpenFile(Path.Combine(CurrentPath, item.Name));
        }
    }

    public void NavigateTo(string path)
    {
        if (!Directory.Exists(path))
        {
            _pathField.Text = CurrentPath;
            return;
        }

        CurrentPath = Path.GetFullPath(path);
        _pathField.Text = CurrentPath;
        LoadDirectory(CurrentPath);
        PathChanged?.Invoke(this);
    }

    private void LoadDirectory(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var dirs = di.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(FileListItem.FromDir);
            var files = di.GetFiles().Select(FileListItem.FromFile);

            var items = new List<FileListItem> { FileListItem.ParentDir() };
            items.AddRange(dirs);
            items.AddRange(SortFiles(files));

            _dataSource = new FileListDataSource(items);
            _fileList.Source = _dataSource;
            _fileList.SelectedItem = 0;
        }
        catch
        {
            _pathField.Text = CurrentPath;
        }
    }

    private IEnumerable<FileListItem> SortFiles(IEnumerable<FileListItem> files) =>
        _sortColumn switch
        {
            SortColumn.Size => _sortAscending
                ? files.OrderBy(f => f.SizeBytes)
                : files.OrderByDescending(f => f.SizeBytes),
            SortColumn.Date => _sortAscending
                ? files.OrderBy(f => f.LastWriteTime)
                : files.OrderByDescending(f => f.LastWriteTime),
            _ => _sortAscending
                ? files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                : files.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
        };

    private void ApplySort()
    {
        if (_dataSource == null) return;

        var all = Enumerable.Range(0, _dataSource.Count).Select(i => _dataSource[i]!).ToList();
        var parent = all.Where(i => i.Name == "..").ToList();
        var dirs   = all.Where(i => i.IsDirectory && i.Name != "..")
                        .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var files  = all.Where(i => !i.IsDirectory);

        var items = new List<FileListItem>();
        items.AddRange(parent);
        items.AddRange(dirs);
        items.AddRange(SortFiles(files));

        _dataSource = new FileListDataSource(items);
        _fileList.Source = _dataSource;
        _fileList.SetNeedsDisplay();
    }

    public FileListItem? SelectedItem => _dataSource?[_fileList.SelectedItem];

    public string GetHighlightedFullPath()
    {
        var item = SelectedItem;
        if (item == null) return CurrentPath;
        if (item.Name == "..") return Path.GetFullPath(Path.Combine(CurrentPath, ".."));
        return Path.Combine(CurrentPath, item.Name);
    }

    public (long selectedBytes, int selectedFiles, int totalFiles, int selectedDirs, int totalDirs) GetStatusInfo()
    {
        if (_dataSource == null) return (0, 0, 0, 0, 0);
        long selectedBytes = 0;
        int selectedFiles = 0, totalFiles = 0, selectedDirs = 0, totalDirs = 0;
        for (int i = 1; i < _dataSource.Count; i++) // skip ".."
        {
            var item = _dataSource[i];
            if (item == null) continue;
            if (item.IsDirectory)
            {
                totalDirs++;
                if (_dataSource.IsUserMarked(i)) selectedDirs++;
            }
            else
            {
                totalFiles++;
                if (_dataSource.IsUserMarked(i))
                {
                    selectedFiles++;
                    selectedBytes += item.SizeBytes;
                }
            }
        }
        return (selectedBytes, selectedFiles, totalFiles, selectedDirs, totalDirs);
    }

    public void SetFocusOnList() => _fileList.SetFocus();

    private void ToggleMark(int index)
    {
        if (_dataSource == null || index < 0 || index >= _dataSource.Count) return;
        if (_dataSource[index]?.Name == "..") return;
        _dataSource.ToggleUserMark(index);
        _fileList.SetNeedsDisplay();
        SelectionChanged?.Invoke(this);
    }

    private void ToggleSelectAll()
    {
        if (_dataSource == null) return;
        bool selectAll = !_dataSource.AreAllItemsMarked();
        _dataSource.SetAllUserMarks(selectAll);
        _fileList.SetNeedsDisplay();
        SelectionChanged?.Invoke(this);
    }

    public IReadOnlyList<FileListItem> GetSourceItems()
    {
        if (_dataSource == null) return [];

        var marked = new List<FileListItem>();
        for (int i = 0; i < _dataSource.Count; i++)
        {
            if (_dataSource.IsUserMarked(i))
            {
                var it = _dataSource[i];
                if (it != null) marked.Add(it);
            }
        }

        if (marked.Count > 0) return marked;

        var selected = SelectedItem;
        return selected != null && selected.Name != ".." ? [selected] : [];
    }

    public void ClearMarks()
    {
        if (_dataSource == null) return;
        _dataSource.ClearUserMarks();
        _fileList.SetNeedsDisplay();
    }
}

class FileListHeaderView : View
{
    // Layout constants — must match FileListDataSource.Render() right column:
    // sep(1) + SIZE(8) + sep(1) + DATE(16) + sep(1) + ATR(3) = 30 chars
    private const int RightHeaderLength = 30;

    public SortColumn CurrentSort { get; set; } = SortColumn.Name;
    public bool SortAscending { get; set; } = true;

    public event Action<SortColumn>? SortHeaderClicked;

    public FileListHeaderView()
    {
        CanFocus = false;
        MouseClick += HandleHeaderMouseClick;
    }

    private void HandleHeaderMouseClick(MouseEventArgs e)
    {
        if (!e.MouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked)) return;

        int x = e.MouseEvent.X;
        int nameMax = Math.Max(1, Bounds.Width - RightHeaderLength);

        SortColumn? clicked = null;
        if (x < nameMax)
            clicked = SortColumn.Name;
        else if (x >= nameMax + 1 && x <= nameMax + 8)    // SIZE (8 chars) in right header
            clicked = SortColumn.Size;
        else if (x >= nameMax + 10 && x <= nameMax + 25)  // DATE (16 chars) in right header
            clicked = SortColumn.Date;

        if (clicked.HasValue)
        {
            SortHeaderClicked?.Invoke(clicked.Value);
            e.Handled = true;
        }
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(new Terminal.Gui.Attribute(Color.Gray, Color.BrightBlue));

        int width = bounds.Width;
        if (width <= 0) return;

        char ind = SortAscending ? '▲' : '▼';

        // Indicator is appended after the full column name; each label keeps its fixed width.
        string nameLabel = CurrentSort == SortColumn.Name ? "NAME" + ind : "NAME";          // variable-width, PadRight fills the rest
        string sizeLabel = CurrentSort == SortColumn.Size ? "SIZE   " + ind : "SIZE    ";   // 8 chars
        string dateLabel = CurrentSort == SortColumn.Date ? "DATE" + ind + "           " : "DATE            "; // 16 chars

        // Right section: sep(1) + size(8) + sep(1) + date(16) + sep(1) + ATR(3) = 30
        string rightHeader = " " + sizeLabel + " " + dateLabel + " " + "ATR";

        int nameMax = Math.Max(1, width - rightHeader.Length);
        string display = nameLabel.PadRight(nameMax) + rightHeader;
        if (display.Length > width) display = display[..width];
        if (display.Length < width) display = display.PadRight(width);

        Move(0, 0);
        Driver.AddStr(display);
    }
}
