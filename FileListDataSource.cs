using System.Collections;
using Terminal.Gui;

public class FileListItem
{
    public string Name { get; init; }
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastWriteTime { get; init; }
    public FileAttributes Attributes { get; init; }

    // Full absolute path — used for Unix execute-bit check; empty for ".."
    private readonly string _fullPath;

    private static readonly HashSet<string> ExecutableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".com", ".ps1", ".msi" };

    public FileListItem(string name, bool isDirectory, long sizeBytes,
        DateTime lastWriteTime, FileAttributes attributes, string fullPath = "")
    {
        Name = name;
        IsDirectory = isDirectory;
        SizeBytes = sizeBytes;
        LastWriteTime = lastWriteTime;
        Attributes = attributes;
        _fullPath = fullPath;
    }

    public static FileListItem ParentDir() =>
        new("..", true, 0, DateTime.MinValue, default);

    public static FileListItem FromDir(DirectoryInfo di) =>
        new(di.Name, true, 0, di.LastWriteTime, di.Attributes, di.FullName);

    public static FileListItem FromFile(FileInfo fi) =>
        new(fi.Name, false, fi.Length, fi.LastWriteTime, fi.Attributes, fi.FullName);

    /// <summary>3-char string like "rwx", "r--", "rw-" representing current-user access.</summary>
    public string RwxString
    {
        get
        {
            if (Name == "..") return "   ";

            char r = 'r';
            char w = Attributes.HasFlag(FileAttributes.ReadOnly) ? '-' : 'w';
            char x = GetExecuteBit();
            return new string([r, w, x]);
        }
    }

    /// <summary>Date formatted as "dd.MM.yyyy HH:mm" (16 chars), or 16 spaces for "..".</summary>
    public string DateString =>
        LastWriteTime == DateTime.MinValue
            ? "                "
            : LastWriteTime.ToString("dd.MM.yyyy HH:mm");

    private char GetExecuteBit()
    {
        if (IsDirectory) return 'x';

        if (OperatingSystem.IsWindows())
            return ExecutableExtensions.Contains(Path.GetExtension(Name)) ? 'x' : '-';

        // Linux / macOS: check the file's Unix execute bit for the owner
        try
        {
            var mode = File.GetUnixFileMode(_fullPath);
            return mode.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-';
        }
        catch
        {
            return '-';
        }
    }
}

public class FileListDataSource : IListDataSource
{
    private readonly List<FileListItem> _items;
    private readonly BitArray _userMarks; // our marks — Terminal.Gui never touches these

    public FileListDataSource(IEnumerable<FileListItem> items)
    {
        _items = [.. items];
        _userMarks = new BitArray(_items.Count);
    }

    public int Count => _items.Count;

    public int Length => _items.Count == 0
        ? 0
        : _items.Max(i => i.IsDirectory ? i.Name.Length + 1 : i.Name.Length + 10) + 21; // +21 for " DATE ATTR"

    public FileListItem? this[int index] =>
        index >= 0 && index < _items.Count ? _items[index] : null;

    // ── IListDataSource — kept as no-ops so Terminal.Gui cannot corrupt our marks ──

    public bool IsMarked(int item) => false;
    public void SetMark(int item, bool value) { }
    public IList ToList() => _items;

    // ── User mark API ─────────────────────────────────────────────────────────────

    public bool IsUserMarked(int index) =>
        index >= 0 && index < _userMarks.Length && _userMarks[index];

    public void ToggleUserMark(int index)
    {
        if (index >= 0 && index < _userMarks.Length)
            _userMarks[index] = !_userMarks[index];
    }

    public void ClearUserMarks()
    {
        _userMarks.SetAll(false);
    }

    public bool AreAllItemsMarked()
    {
        // Start at 1 to skip the ".." entry; returns false if there are no real items
        for (int i = 1; i < _userMarks.Length; i++)
            if (!_userMarks[i]) return false;
        return _userMarks.Length > 1;
    }

    public void SetAllUserMarks(bool value)
    {
        // Start at 1 to skip the ".." entry
        for (int i = 1; i < _userMarks.Length; i++)
            _userMarks[i] = value;
    }

    internal static string FormatSize(long bytes)
    {
        string[] units = { "B", "kB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        for (int i = 0; i < units.Length; i++)
        {
            double rounded = Math.Round(value, 1);
            if (rounded < 1000.0 || i == units.Length - 1)
            {
                string num = i == 0 ? $"{(long)rounded}" : $"{rounded:F1}";
                return $"{num} {units[i]}".PadLeft(8);
            }
            value /= 1000.0;
        }
        return ""; // unreachable
    }

    public void Render(ListView container, ConsoleDriver driver, bool selected,
        int item, int col, int line, int width, int start)
    {
        if (item < 0 || item >= _items.Count) return;

        var fi = _items[item];
        string display;

        string sizeCol = fi.IsDirectory ? "   <DIR>" : FormatSize(fi.SizeBytes);
        string dateCol = fi.DateString;   // always 16 chars
        string attrCol = fi.RwxString;    // always 3 chars

        string rightCol = sizeCol + " " + dateCol + " " + attrCol;
        int nameMax = Math.Max(1, width - rightCol.Length - 1);
        string label = fi.IsDirectory ? "[" + fi.Name + "]" : fi.Name;
        if (label.Length > nameMax) label = label[..nameMax];
        display = label.PadRight(nameMax) + " " + rightCol;

        if (display.Length > width) display = display[..width];

        // Always set the attribute explicitly — Terminal.Gui does not reset it
        // between Render calls, so skipping it causes color bleed to subsequent items.
        (Color fg, Color bg) = (_userMarks[item], selected) switch
        {
            (true,  true)  => (Color.BrightRed,   Color.BrightGreen),
            (true,  false) => (Color.BrightRed,   Color.Black),
            (false, true)  => (Color.Black,        Color.BrightGreen),
            (false, false) => (Color.BrightGreen,  Color.Black),
        };
        driver.SetAttribute(new Terminal.Gui.Attribute(fg, bg));
        driver.AddStr(display);
    }
}
