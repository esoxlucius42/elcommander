using Terminal.Gui;

public enum SortColumn { Name, Size, Date }

public class FileExplorerPane
{
    private enum DragMarkMode { Select, Deselect }

    public FrameView Frame { get; }
    public string CurrentPath { get; private set; }

    private readonly PathTextField _pathField;
    private readonly PathActionButton _addFavoriteButton;
    private readonly PathActionButton _removeFavoriteButton;
    private readonly FileListView _fileList;
    private readonly FileListHeaderView _headerView;
    private FileListDataSource? _dataSource;
    private List<string> _favoriteDirectories;

    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true;
    private DragMarkMode? _dragMarkMode;
    private int _lastDragItemIndex = -1;
    private bool _suppressNextRightClick;

    public event Action<FileExplorerPane>? PathChanged;
    public event Action<FileExplorerPane>? GotFocus;
    public event Action<FileExplorerPane>? SelectionChanged;
    public event Action<FileExplorerPane>? AddFavoriteRequested;
    public event Action<FileExplorerPane>? RemoveFavoriteRequested;

    public FileExplorerPane(string initialPath, IEnumerable<string> favoriteDirectories)
    {
        CurrentPath = Path.GetFullPath(initialPath);
        _favoriteDirectories = [.. favoriteDirectories];

        Frame = new FrameView("")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _pathField = new PathTextField(CurrentPath)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(9),
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

        _addFavoriteButton = new PathActionButton("[+]")
        {
            X = Pos.Right(_pathField) + 2,
            Y = 0,
            Width = 3,
            Height = 1
        };

        _removeFavoriteButton = new PathActionButton("[-]")
        {
            X = Pos.Right(_addFavoriteButton) + 1,
            Y = 0,
            Width = 3,
            Height = 1
        };

        _headerView = new FileListHeaderView
        {
            X = 0, Y = 1,
            Width = Dim.Fill(),
            Height = 1
        };

        _fileList = new FileListView
        {
            X = 0, Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            WantMousePositionReports = true,
            ColorScheme = new ColorScheme
            {
                Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                Focus     = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
                HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                HotFocus  = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
                Disabled  = new Terminal.Gui.Attribute(Color.DarkGray,    Color.Black),
            }
        };

        Frame.Add(_pathField, _addFavoriteButton, _removeFavoriteButton, _headerView, _fileList);

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
        _pathField.FavoritesRequested += ShowFavoritePicker;

        _fileList.RawMouseEvent += HandleFileListMouseEvent;
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
        _addFavoriteButton.Clicked += () =>
        {
            GotFocus?.Invoke(this);
            AddFavoriteRequested?.Invoke(this);
        };
        _removeFavoriteButton.Clicked += () =>
        {
            GotFocus?.Invoke(this);
            RemoveFavoriteRequested?.Invoke(this);
        };
        _fileList.Enter += (_) => GotFocus?.Invoke(this);
        _pathField.Enter += (_) => GotFocus?.Invoke(this);
        _addFavoriteButton.Enter += (_) => GotFocus?.Invoke(this);
        _removeFavoriteButton.Enter += (_) => GotFocus?.Invoke(this);
        _fileList.SelectedItemChanged += (_) => SelectionChanged?.Invoke(this);

        LoadDirectory(CurrentPath);
        SyncPathInput();
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
            SyncPathInput();
            return;
        }

        CurrentPath = Path.GetFullPath(path);
        SyncPathInput();
        LoadDirectory(CurrentPath);
        PathChanged?.Invoke(this);
    }

    private void LoadDirectory(string path)
    {
        ResetDragSelection();

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
            SyncPathInput();
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

        ResetDragSelection();

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

    public void RefreshFavoriteDirectories(IEnumerable<string> favoriteDirectories)
    {
        _favoriteDirectories = [.. favoriteDirectories];

        SyncPathInput();
    }

    private bool ToggleMark(int index)
    {
        if (_dataSource == null || index < 0 || index >= _dataSource.Count)
            return false;

        bool currentValue = _dataSource.IsUserMarked(index);
        return SetMark(index, !currentValue);
    }

    private bool SetMark(int index, bool marked)
    {
        if (!CanMark(index) || _dataSource == null)
            return false;

        if (!_dataSource.SetUserMark(index, marked))
            return false;

        _fileList.SetNeedsDisplay();
        SelectionChanged?.Invoke(this);
        return true;
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

    private void SyncPathInput()
    {
        _pathField.Text = CurrentPath;
        UpdateFavoriteButtons();
    }

    private void UpdateFavoriteButtons()
    {
        bool isFavorite = FindFavoriteIndex(CurrentPath) >= 0;
        _addFavoriteButton.Enabled = !isFavorite;
        _removeFavoriteButton.Enabled = isFavorite;
    }

    private int FindFavoriteIndex(string path)
    {
        for (int i = 0; i < _favoriteDirectories.Count; i++)
        {
            if (FavoriteDirectoriesStore.DirectoryPathComparer.Equals(_favoriteDirectories[i], path))
                return i;
        }

        return -1;
    }

    private void ShowFavoritePicker()
    {
        GotFocus?.Invoke(this);

        if (_favoriteDirectories.Count == 0)
            return;

        int initialSelection = FindFavoriteIndex(CurrentPath);
        var favoritesList = new ListView(_favoriteDirectories)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            SelectedItem = initialSelection >= 0 ? initialSelection : 0
        };
        var cancelButton = new Button("Cancel");
        int dialogHeight = Math.Max(8, Math.Min(16, _favoriteDirectories.Count + 4));
        var dialog = new Dialog("Favorite directories", 80, dialogHeight, cancelButton);
        string? selectedPath = null;

        void AcceptSelection()
        {
            int index = favoritesList.SelectedItem;
            if (index < 0 || index >= _favoriteDirectories.Count)
                return;

            selectedPath = _favoriteDirectories[index];
            Application.RequestStop();
        }

        favoritesList.OpenSelectedItem += _ => AcceptSelection();
        favoritesList.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key != Key.Enter)
                return;

            AcceptSelection();
            e.Handled = true;
        };
        cancelButton.Clicked += () => Application.RequestStop();

        dialog.Add(favoritesList);
        favoritesList.SetFocus();
        Application.Run(dialog);

        if (selectedPath is null)
        {
            _pathField.SetFocus();
            return;
        }

        NavigateTo(selectedPath);
        _fileList.SetFocus();
    }

    private void HandleFileListMouseEvent(MouseEvent me)
    {
        if (me.Flags.HasFlag(MouseFlags.Button3Released))
        {
            if (_dragMarkMode.HasValue)
            {
                ContinueDragSelection(me.Y);
                ResetDragSelection();
                me.Handled = true;
            }
            return;
        }

        if (_dragMarkMode.HasValue)
        {
            if (me.Flags.HasFlag(MouseFlags.Button3Pressed) || me.Flags.HasFlag(MouseFlags.ReportMousePosition))
            {
                ContinueDragSelection(me.Y);
                me.Handled = true;
            }
            return;
        }

        if (me.Flags.HasFlag(MouseFlags.Button3Pressed))
        {
            _suppressNextRightClick = true;
            BeginDragSelection(me.Y);
            me.Handled = true;
            return;
        }

        if (!me.Flags.HasFlag(MouseFlags.Button3Clicked))
            return;

        if (_suppressNextRightClick)
        {
            _suppressNextRightClick = false;
            me.Handled = true;
            return;
        }

        if (ToggleMark(GetItemIndexFromMouseY(me.Y)))
            me.Handled = true;
    }

    private void BeginDragSelection(int mouseY)
    {
        ResetDragSelection();

        int index = GetItemIndexFromMouseY(mouseY);
        if (!CanMark(index) || _dataSource == null)
            return;

        GotFocus?.Invoke(this);
        _fileList.SetFocus();

        _dragMarkMode = _dataSource.IsUserMarked(index) ? DragMarkMode.Deselect : DragMarkMode.Select;
        _lastDragItemIndex = index;
        SetMark(index, _dragMarkMode == DragMarkMode.Select);
    }

    private void ContinueDragSelection(int mouseY)
    {
        if (!_dragMarkMode.HasValue)
            return;

        int index = GetItemIndexFromMouseY(mouseY);
        if (!CanMark(index) || index == _lastDragItemIndex)
            return;

        _lastDragItemIndex = index;
        SetMark(index, _dragMarkMode == DragMarkMode.Select);
    }

    private void ResetDragSelection()
    {
        _dragMarkMode = null;
        _lastDragItemIndex = -1;
    }

    private int GetItemIndexFromMouseY(int mouseY)
    {
        if (_dataSource == null)
            return -1;

        int index = mouseY + _fileList.TopItem;
        return index >= 0 && index < _dataSource.Count ? index : -1;
    }

    private bool CanMark(int index) =>
        _dataSource != null &&
        index >= 0 &&
        index < _dataSource.Count &&
        _dataSource[index]?.Name != "..";
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

class FileListView : ListView
{
    public event Action<MouseEvent>? RawMouseEvent;

    public override bool MouseEvent(MouseEvent me)
    {
        RawMouseEvent?.Invoke(me);
        return me.Handled || base.MouseEvent(me);
    }
}

class PathTextField : TextField
{
    public event Action? FavoritesRequested;

    public PathTextField(string text) : base(text)
    {
    }

    public override bool MouseEvent(MouseEvent me)
    {
        if (me.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            SetFocus();
            FavoritesRequested?.Invoke();
            return true;
        }

        return base.MouseEvent(me);
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.CursorDown)
        {
            FavoritesRequested?.Invoke();
            return true;
        }

        return base.ProcessKey(keyEvent);
    }
}

class PathActionButton : View
{
    private readonly string _label;

    public event Action? Clicked;

    public PathActionButton(string label)
    {
        _label = label;
        CanFocus = true;
        TabStop = true;
        Width = label.Length;
        Height = 1;
        ColorScheme = new ColorScheme
        {
            Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            Focus     = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
            HotFocus  = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
            Disabled  = new Terminal.Gui.Attribute(Color.DarkGray,    Color.Black),
        };
    }

    public override void Redraw(Rect bounds)
    {
        Driver.SetAttribute(Enabled
            ? (HasFocus ? ColorScheme.Focus : ColorScheme.Normal)
            : ColorScheme.Disabled);

        Move(0, 0);
        Driver.AddStr(_label);
    }

    public override bool MouseEvent(MouseEvent me)
    {
        if (!Enabled) return false;
        if (!me.Flags.HasFlag(MouseFlags.Button1Clicked)) return false;

        SetFocus();
        OnClicked();
        return true;
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        if (!Enabled) return false;
        if (keyEvent.Key != Key.Enter && keyEvent.Key != Key.Space) return false;

        OnClicked();
        return true;
    }

    private void OnClicked() => Clicked?.Invoke();
}
