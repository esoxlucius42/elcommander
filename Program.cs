using System.Diagnostics;
using NStack;
using Terminal.Gui;

const int MIN_COLS = 128;
const int MIN_ROWS = 32;

if (Console.WindowWidth < MIN_COLS || Console.WindowHeight < MIN_ROWS)
{
    Console.Error.WriteLine($"Terminal too small. Minimum size is {MIN_COLS}×{MIN_ROWS} (current: {Console.WindowWidth}×{Console.WindowHeight}).");
    Environment.Exit(1);
}

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    string msg = args.ExceptionObject is Exception ex ? ex.Message : args.ExceptionObject?.ToString() ?? "Unknown error";
    try
    {
        MessageBox.ErrorQuery("Unhandled Error", msg, "OK");
    }
    catch
    {
        Console.Error.WriteLine($"Fatal error: {msg}");
        Environment.Exit(1);
    }
};

Application.Init();

// ── Main window ──────────────────────────────────────────────────────────────

var win = new Window("el Commander")
{
    X = 0, Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill()
};
win.ColorScheme = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    Focus     = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    HotNormal = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    HotFocus  = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    Disabled  = new Terminal.Gui.Attribute(Color.DarkGray, Color.Blue),
};

// ── File explorer panes ───────────────────────────────────────────────────────

string startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var favoriteDirectoriesStore = new FavoriteDirectoriesStore();
List<string> favoriteDirectories = [.. favoriteDirectoriesStore.Load()];

var leftPane  = new FileExplorerPane(startPath, favoriteDirectories);
var rightPane = new FileExplorerPane(startPath, favoriteDirectories);

leftPane.Frame.X      = 0;
leftPane.Frame.Y      = 0;
leftPane.Frame.Width  = Dim.Percent(50);
leftPane.Frame.Height = Dim.Fill() - 7;

rightPane.Frame.X      = Pos.Percent(50);
rightPane.Frame.Y      = 0;
rightPane.Frame.Width  = Dim.Fill();
rightPane.Frame.Height = Dim.Fill() - 7;

var fileExplorerColors = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    Focus     = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    HotNormal = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    HotFocus  = new Terminal.Gui.Attribute(Color.White,    Color.Blue),
    Disabled  = new Terminal.Gui.Attribute(Color.DarkGray, Color.Black),
};
leftPane.Frame.ColorScheme  = fileExplorerColors;
rightPane.Frame.ColorScheme = fileExplorerColors;

// ── Info panes ────────────────────────────────────────────────────────────────

var infoFrameL = new FrameView("")
{
    X = 0,
    Y = Pos.Bottom(leftPane.Frame),
    Width = Dim.Percent(50),
    Height = 3
};
var infoLabelL = new Label("") { X = 0, Y = 0 };
infoFrameL.Add(infoLabelL);

var infoFrameR = new FrameView("")
{
    X = Pos.Percent(50),
    Y = Pos.Bottom(rightPane.Frame),
    Width = Dim.Fill(),
    Height = 3
};
var infoLabelR = new Label("") { X = 0, Y = 0 };
infoFrameR.Add(infoLabelR);

// ── Command bar ───────────────────────────────────────────────────────────────

var pathDisplayColors = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(Color.Gray, Color.Blue),
    Focus     = new Terminal.Gui.Attribute(Color.Gray, Color.Blue),
    HotNormal = new Terminal.Gui.Attribute(Color.Gray, Color.Blue),
    HotFocus  = new Terminal.Gui.Attribute(Color.Gray, Color.Blue),
    Disabled  = new Terminal.Gui.Attribute(Color.Gray, Color.Blue),
};
var pathDisplay = new TextField("")
{
    X = 0,
    Y = Pos.Bottom(infoFrameL),
    Width = Dim.Fill(),
    Height = 1,
    ReadOnly = true,
    ColorScheme = pathDisplayColors
};

var cmdBarFrame = new FrameView("")
{
    X = 0,
    Y = Pos.Bottom(pathDisplay),
    Width = Dim.Fill(),
    Height = 3
};
var btnColor     = new Button("F1 Color")     { X = 1,                           Y = 0 };
var btnTerminal  = new Button("F2 Terminal")  { X = Pos.Right(btnColor)       + 1, Y = 0 };
var btnEdit      = new Button("F4 Edit")      { X = Pos.Right(btnTerminal)    + 1, Y = 0 };
var btnCopy      = new Button("F5 Copy")      { X = Pos.Right(btnEdit)        + 1, Y = 0 };
var btnMove      = new Button("F6 Move")      { X = Pos.Right(btnCopy)        + 1, Y = 0 };
var btnNewFolder = new Button("F7 New Folder"){ X = Pos.Right(btnMove)        + 1, Y = 0 };
var btnDelete    = new Button("F8 Delete")    { X = Pos.Right(btnNewFolder)   + 1, Y = 0 };
var btnBatchRen  = new Button("F9 BATCH REN") { X = Pos.Right(btnDelete)      + 1, Y = 0 };
var btnExit      = new Button("F12 Exit")     { X = Pos.Right(btnBatchRen)    + 1, Y = 0 };
cmdBarFrame.Add(btnColor, btnTerminal, btnEdit, btnCopy, btnMove, btnNewFolder, btnDelete, btnBatchRen, btnExit);

// ── Assemble window ───────────────────────────────────────────────────────────

win.Add(leftPane.Frame, rightPane.Frame,
        infoFrameL, infoFrameR,
        pathDisplay,
        cmdBarFrame);
Application.Top.Add(win);

// ── Too-small overlay ─────────────────────────────────────────────────────────

var tooSmallOverlay = new Window($"Terminal too small (min {MIN_COLS}×{MIN_ROWS})")
{
    X = Pos.Center(), Y = Pos.Center(),
    Width = 44, Height = 5,
    Visible = false,
    ColorScheme = Colors.Error
};
tooSmallOverlay.Add(new Label("Please resize the terminal.")
{
    X = Pos.Center(), Y = Pos.Center()
});
Application.Top.Add(tooSmallOverlay);

// ── Active pane tracking ──────────────────────────────────────────────────────

FileExplorerPane activePane = leftPane;
bool isBatchRenameFormOpen = false;
Action? closeBatchRenameForm = null;
Action? submitBatchRenameForm = null;

void SetActivePane(FileExplorerPane pane)
{
    activePane = pane;
}

void UpdateInfoLabel(FileExplorerPane pane)
{
    var (selectedBytes, selectedFiles, totalFiles, selectedDirs, totalDirs) = pane.GetStatusInfo();
    string size = FileListDataSource.FormatSize(selectedBytes).Trim();
    string info = $"{size};  {selectedFiles} / {totalFiles} files;  {selectedDirs} / {totalDirs} dirs";
    if (pane == leftPane) infoLabelL.Text = info;
    else infoLabelR.Text = info;
}

leftPane.GotFocus  += SetActivePane;
rightPane.GotFocus += SetActivePane;

leftPane.AddFavoriteRequested += HandleAddFavoriteRequested;
rightPane.AddFavoriteRequested += HandleAddFavoriteRequested;
leftPane.RemoveFavoriteRequested += HandleRemoveFavoriteRequested;
rightPane.RemoveFavoriteRequested += HandleRemoveFavoriteRequested;

leftPane.GotFocus  += _ => UpdatePathDisplay();
rightPane.GotFocus += _ => UpdatePathDisplay();

leftPane.PathChanged  += p => UpdateInfoLabel(p);
rightPane.PathChanged += p => UpdateInfoLabel(p);

leftPane.SelectionChanged  += p => UpdateInfoLabel(p);
rightPane.SelectionChanged += p => UpdateInfoLabel(p);

leftPane.PathChanged  += p => { if (activePane == p) UpdatePathDisplay(); };
rightPane.PathChanged += p => { if (activePane == p) UpdatePathDisplay(); };

leftPane.SelectionChanged  += p => { if (activePane == p) UpdatePathDisplay(); };
rightPane.SelectionChanged += p => { if (activePane == p) UpdatePathDisplay(); };

void UpdatePathDisplay() => pathDisplay.Text = activePane.GetHighlightedFullPath();

void RefreshFavoriteDirectories()
{
    leftPane.RefreshFavoriteDirectories(favoriteDirectories);
    rightPane.RefreshFavoriteDirectories(favoriteDirectories);
}

void HandleAddFavoriteRequested(FileExplorerPane pane)
{
    if (!FavoriteDirectoriesStore.TryNormalizePath(pane.CurrentPath, out string normalizedPath))
        return;

    if (favoriteDirectories.Any(path => FavoriteDirectoriesStore.DirectoryPathComparer.Equals(path, normalizedPath)))
        return;

    favoriteDirectories.Add(normalizedPath);
    favoriteDirectories = [.. favoriteDirectoriesStore.Save(favoriteDirectories)];
    RefreshFavoriteDirectories();
}

void HandleRemoveFavoriteRequested(FileExplorerPane pane)
{
    if (!FavoriteDirectoriesStore.TryNormalizePath(pane.CurrentPath, out string normalizedPath))
        return;

    int removed = favoriteDirectories.RemoveAll(path =>
        FavoriteDirectoriesStore.DirectoryPathComparer.Equals(path, normalizedPath));
    if (removed == 0) return;

    favoriteDirectories = [.. favoriteDirectoriesStore.Save(favoriteDirectories)];
    RefreshFavoriteDirectories();
}

UpdateInfoLabel(leftPane);
UpdateInfoLabel(rightPane);
UpdatePathDisplay();

// ── Button handlers ───────────────────────────────────────────────────────────

btnColor.Clicked     += () => ShowColorDialog();
btnTerminal.Clicked  += () => Platform.OpenTerminal(activePane.CurrentPath);
btnExit.Clicked      += () => ConfirmExit();
btnEdit.Clicked      += () => OpenSelectedFile();
btnCopy.Clicked      += () => CopySelected();
btnMove.Clicked      += () => MoveSelected();
btnNewFolder.Clicked += () => CreateNewFolder();
btnDelete.Clicked    += () => DeleteSelected();
btnBatchRen.Clicked  += () => ShowBatchRenameForm();

// ── Global F-key shortcuts ────────────────────────────────────────────────────

Application.Top.KeyPress += (e) =>
{
    if (isBatchRenameFormOpen)
    {
        switch (e.KeyEvent.Key)
        {
            case Key.F1:
                closeBatchRenameForm?.Invoke();
                e.Handled = true;
                return;
            case Key.F5:
                submitBatchRenameForm?.Invoke();
                e.Handled = true;
                return;
            case Key.F2:
            case Key.F4:
            case Key.F6:
            case Key.F7:
            case Key.F8:
            case Key.F9:
            case Key.F12:
                e.Handled = true;
                return;
        }
    }

    switch (e.KeyEvent.Key)
    {
        case Key.F1:  ShowColorDialog();       e.Handled = true; break;
        case Key.F2:  Platform.OpenTerminal(activePane.CurrentPath); e.Handled = true; break;
        case Key.F4:  OpenSelectedFile();      e.Handled = true; break;
        case Key.F5:  CopySelected();     e.Handled = true; break;
        case Key.F6:  MoveSelected();     e.Handled = true; break;
        case Key.F7:  CreateNewFolder();  e.Handled = true; break;
        case Key.F8:  DeleteSelected();   e.Handled = true; break;
        case Key.F9:  ShowBatchRenameForm(); e.Handled = true; break;
        case Key.F12: ConfirmExit();      e.Handled = true; break;
        case Key.Tab:
            var target = activePane == leftPane ? rightPane : leftPane;
            target.SetFocusOnList();
            e.Handled = true;
            break;
    }
};

// ── Resize handler ────────────────────────────────────────────────────────────

Application.Resized += (e) => Application.Top.SetNeedsDisplay();

// ── Run ───────────────────────────────────────────────────────────────────────

try
{
    Application.Run();
}
catch (Exception ex)
{
    MessageBox.ErrorQuery("Fatal Error", ex.Message, "OK");
}
Application.Shutdown();

// ── File operations ───────────────────────────────────────────────────────────

void ShowColorDialog()
{
    const int swatchWidth  = 64; // 4 cols × 16 chars each
    const int swatchHeight = 4;  // 4 rows  (16 colors / 4 per row)
    const int dialogWidth  = swatchWidth + 4;
    const int dialogHeight = 1 + swatchHeight + 1 + 1 + swatchHeight + 4; // = 15

    var btnOk  = new Button("OK", is_default: true);
    var dialog = new Dialog("Colors", dialogWidth, dialogHeight, btnOk);

    dialog.Add(new Label("Colors on black background:") { X = 0, Y = 0 });
    dialog.Add(new ColorSwatchView(Color.Black)
    {
        X = 0, Y = 1,
        Width = swatchWidth,
        Height = swatchHeight
    });

    dialog.Add(new Label("Colors on white background:") { X = 0, Y = 6 });
    dialog.Add(new ColorSwatchView(Color.White)
    {
        X = 0, Y = 7,
        Width = swatchWidth,
        Height = swatchHeight
    });

    btnOk.Clicked += () => Application.RequestStop();
    Application.Run(dialog);
}

void ConfirmExit()
{
    int result = MessageBox.Query("Exit", "Exit el Commander?", "Yes", "No");
    if (result == 0) Application.RequestStop();
}

void OpenSelectedFile()
{
    var item = activePane.GetSourceItems().FirstOrDefault(i => !i.IsDirectory);
    if (item == null) return;
    string path = Path.Combine(activePane.CurrentPath, item.Name);
    // UseShellExecute = true is cross-platform: on Windows it uses the file association;
    // on Linux/macOS .NET delegates to xdg-open / open automatically.
    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    catch (Exception ex) { MessageBox.ErrorQuery("Error", ex.Message, "OK"); }
}

void CopySelected()
{
    var items = activePane.GetSourceItems();
    if (items.Count == 0) return;

    var targetPane = activePane == leftPane ? rightPane : leftPane;
    string summary = items.Count == 1 ? $"'{items[0].Name}'" : $"{items.Count} items";

    int confirm = MessageBox.Query("Copy",
        $"Copy {summary} to:\n{targetPane.CurrentPath}?", "Yes", "No");
    if (confirm != 0) return;

    var errors = new List<string>();
    foreach (var item in items)
    {
        string src = Path.Combine(activePane.CurrentPath, item.Name);
        string dst = Path.Combine(targetPane.CurrentPath, item.Name);
        try
        {
            if (item.IsDirectory) CopyDirectory(src, dst);
            else File.Copy(src, dst, overwrite: false);
        }
        catch (Exception ex) { errors.Add($"{item.Name}: {ex.Message}"); }
    }

    activePane.ClearMarks();
    targetPane.NavigateTo(targetPane.CurrentPath);
    if (errors.Count > 0)
        MessageBox.ErrorQuery("Copy errors", string.Join("\n", errors), "OK");
}

void MoveSelected()
{
    var items = activePane.GetSourceItems();
    if (items.Count == 0) return;

    var targetPane = activePane == leftPane ? rightPane : leftPane;
    string summary = items.Count == 1 ? $"'{items[0].Name}'" : $"{items.Count} items";

    int confirm = MessageBox.Query("Move",
        $"Move {summary} to:\n{targetPane.CurrentPath}?", "Yes", "No");
    if (confirm != 0) return;

    var errors = new List<string>();
    foreach (var item in items)
    {
        string src = Path.Combine(activePane.CurrentPath, item.Name);
        string dst = Path.Combine(targetPane.CurrentPath, item.Name);
        try
        {
            if (item.IsDirectory) Directory.Move(src, dst);
            else File.Move(src, dst, overwrite: false);
        }
        catch (Exception ex) { errors.Add($"{item.Name}: {ex.Message}"); }
    }

    activePane.ClearMarks();
    activePane.NavigateTo(activePane.CurrentPath);
    targetPane.NavigateTo(targetPane.CurrentPath);
    if (errors.Count > 0)
        MessageBox.ErrorQuery("Move errors", string.Join("\n", errors), "OK");
}

void CreateNewFolder()
{
    var nameField = new TextField("") { X = 1, Y = 1, Width = Dim.Fill() - 1 };
    var btnOk     = new Button("OK", is_default: true);
    var btnCancel = new Button("Cancel");
    var dialog    = new Dialog("New Folder", 50, 7, btnOk, btnCancel);
    dialog.Add(new Label("Folder name:") { X = 1, Y = 0 }, nameField);
    nameField.SetFocus();

    btnOk.Clicked += () =>
    {
        string name = nameField.Text?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(name))
        {
            string newPath = Path.Combine(activePane.CurrentPath, name);
            try
            {
                Directory.CreateDirectory(newPath);
                activePane.NavigateTo(activePane.CurrentPath);
            }
            catch (Exception ex) { MessageBox.ErrorQuery("Error", ex.Message, "OK"); }
        }
        Application.RequestStop();
    };
    btnCancel.Clicked += () => Application.RequestStop();

    Application.Run(dialog);
}

void ShowBatchRenameForm()
{
    // The controls pane contains nested FrameViews, so it needs enough room for
    // both their content rows and their borders. If this budget is too small,
    // the bottom action row is clipped off-screen at the minimum terminal size.
    const int batchRenameControlsHeight = 17;
    const int batchRenameTopControlsHeight = 7;
    const int batchRenameSearchReplaceHeight = 5;
    const int batchRenameActionHeight = 3;

    if (isBatchRenameFormOpen)
        return;

    var selectedItems = activePane.GetSourceItems()
        .Where(item => !item.IsDirectory)
        .ToList();
    if (selectedItems.Count == 0)
        return;

    var currentNames = selectedItems.Select(item => item.Name).ToList();

    var formBatchRename = new Window("Batch Rename")
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        ColorScheme = win.ColorScheme
    };
    var batchRenameListColors = new ColorScheme
    {
        Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
        Focus     = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
        HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
        HotFocus  = new Terminal.Gui.Attribute(Color.Black,       Color.BrightGreen),
        Disabled  = new Terminal.Gui.Attribute(Color.DarkGray,    Color.Black),
    };
    var batchRenameMaskFieldColors = new ColorScheme
    {
        Normal    = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
        Focus     = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
        HotNormal = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
        HotFocus  = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
        Disabled  = new Terminal.Gui.Attribute(Color.DarkGray,    Color.Black),
    };

    var frameCurrentNames = new FrameView("Current file names")
    {
        X = 0,
        Y = 0,
        Width = Dim.Percent(50),
        Height = Dim.Fill(batchRenameControlsHeight)
    };
    var listCurrentNames = new ListView(currentNames)
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        AllowsMarking = false,
        ColorScheme = batchRenameListColors
    };
    frameCurrentNames.Add(listCurrentNames);

    var frameFutureNames = new FrameView("Future file names")
    {
        X = Pos.Right(frameCurrentNames),
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(batchRenameControlsHeight)
    };
    var futureNames = new List<string>(currentNames);
    var listFutureNames = new ListView(futureNames)
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        AllowsMarking = false,
        ColorScheme = batchRenameListColors
    };
    frameFutureNames.Add(listFutureNames);

    var frameControls = new FrameView("Controls")
    {
        X = 0,
        Y = Pos.Bottom(frameCurrentNames),
        Width = Dim.Fill(),
        Height = batchRenameControlsHeight
    };
    var frameRenameMask = new FrameView("Rename mask")
    {
        X = 0,
        Y = 0,
        Width = Dim.Percent(45),
        Height = batchRenameTopControlsHeight
    };
    var lblFileNameMask = new Label("File name")
    {
        X = 1,
        Y = 0
    };
    var txtFileNameMask = new TextField("[N]")
    {
        X = 1,
        Y = 1,
        Width = Dim.Percent(62),
        ColorScheme = batchRenameMaskFieldColors
    };
    var lblExtensionMask = new Label("Extension")
    {
        X = Pos.Right(txtFileNameMask) + 2,
        Y = 0
    };
    var txtExtensionMask = new TextField("[E]")
    {
        X = Pos.Right(txtFileNameMask) + 2,
        Y = 1,
        Width = Dim.Fill(2),
        ColorScheme = batchRenameMaskFieldColors
    };
    var lblTemplateLegend = new Label("[N] - file name; [E] - extension; [C] - counter")
    {
        X = 1,
        Y = 3
    };
    frameRenameMask.Add(
        lblFileNameMask,
        txtFileNameMask,
        lblExtensionMask,
        txtExtensionMask,
        lblTemplateLegend);

    var frameCounter = new FrameView("Counter")
    {
        X = Pos.Right(frameRenameMask),
        Y = 0,
        Width = 28,
        Height = batchRenameTopControlsHeight
    };
    var txtCounterStart = new RightAlignedTextField("1")
    {
        X = Pos.AnchorEnd(9),
        Y = 0,
        Width = 8,
        ColorScheme = batchRenameMaskFieldColors
    };
    var txtCounterStep = new RightAlignedTextField("1")
    {
        X = Pos.AnchorEnd(9),
        Y = 2,
        Width = 8,
        ColorScheme = batchRenameMaskFieldColors
    };
    var txtCounterDigits = new RightAlignedTextField("1")
    {
        X = Pos.AnchorEnd(9),
        Y = 4,
        Width = 8,
        ColorScheme = batchRenameMaskFieldColors
    };
    frameCounter.Add(
        new Label("Start at:") { X = 1, Y = 0 },
        txtCounterStart,
        new Label("Step by:") { X = 1, Y = 2 },
        txtCounterStep,
        new Label("Digits:") { X = 1, Y = 4 },
        txtCounterDigits);

    var frameCase = new FrameView("Upper / Lower case")
    {
        X = Pos.Right(frameCounter),
        Y = 0,
        Width = Dim.Fill(),
        Height = batchRenameTopControlsHeight
    };
    var radioCaseMode = new RadioGroup(
        new[]
        {
            (ustring) "Unchanged",
            (ustring) "All upper case",
            (ustring) "All lower case",
            (ustring) "First letter of each word uppercase",
            (ustring) "First letter upper case"
        },
        0)
    {
        X = 1,
        Y = 0,
        Width = Dim.Fill(1),
        Height = Dim.Fill()
    };
    frameCase.Add(radioCaseMode);

    var frameSearchReplace = new FrameView("Search & replace")
    {
        X = 0,
        Y = Pos.Bottom(frameRenameMask),
        Width = Dim.Fill(),
        Height = batchRenameSearchReplaceHeight
    };
    var txtSearchFor = new TextField("")
    {
        X = 15,
        Y = 0,
        Width = Dim.Fill(16),
        ColorScheme = batchRenameMaskFieldColors
    };
    var txtReplaceWith = new TextField("")
    {
        X = 15,
        Y = 2,
        Width = Dim.Fill(16),
        ColorScheme = batchRenameMaskFieldColors
    };
    frameSearchReplace.Add(
        new Label("Search for:") { X = 1, Y = 0 },
        txtSearchFor,
        new Label("Replace with:") { X = 1, Y = 2 },
        txtReplaceWith);

    var btnCancelBatchRename = new Button("F1 Cancel")
    {
        X = 1,
        Y = 0
    };
    var btnStartBatchRename = new Button("F5 Start")
    {
        X = Pos.Right(btnCancelBatchRename) + 2,
        Y = 0
    };
    var statusLabel = new Label("")
    {
        X = Pos.Right(btnStartBatchRename) + 2,
        Y = 0,
        Width = Dim.Fill(1)
    };
    var frameActionButtons = new FrameView("")
    {
        X = 0,
        Y = Pos.Bottom(frameSearchReplace),
        Width = Dim.Fill(),
        Height = batchRenameActionHeight
    };
    frameActionButtons.Add(statusLabel, btnCancelBatchRename, btnStartBatchRename);

    frameControls.Add(
        frameRenameMask,
        frameCounter,
        frameCase,
        frameSearchReplace,
        frameActionButtons);

    formBatchRename.Add(frameCurrentNames, frameFutureNames, frameControls);

    bool syncingPreviewSelection = false;
    BatchRenamePreviewResult? lastPreview = null;

    void SyncPreviewSelection(ListView source, ListView target)
    {
        if (syncingPreviewSelection)
            return;

        syncingPreviewSelection = true;
        target.SelectedItem = source.SelectedItem;
        syncingPreviewSelection = false;
    }

    bool TryReadOptions(out BatchRenameOptions options, out string? error)
    {
        options = null!;
        error = null;

        if (!int.TryParse(txtCounterStart.Text?.ToString(), out int counterStart))
        {
            error = "Counter 'Start at' must be a whole number.";
            return false;
        }

        if (!int.TryParse(txtCounterStep.Text?.ToString(), out int counterStep))
        {
            error = "Counter 'Step by' must be a whole number.";
            return false;
        }

        if (counterStep == 0)
        {
            error = "Counter 'Step by' cannot be zero.";
            return false;
        }

        if (!int.TryParse(txtCounterDigits.Text?.ToString(), out int counterDigits))
        {
            error = "Counter 'Digits' must be a whole number.";
            return false;
        }

        if (counterDigits < 1)
        {
            error = "Counter 'Digits' must be at least 1.";
            return false;
        }

        options = new BatchRenameOptions
        {
            FileNameMask = txtFileNameMask.Text?.ToString() ?? "",
            ExtensionMask = txtExtensionMask.Text?.ToString() ?? "",
            SearchFor = txtSearchFor.Text?.ToString() ?? "",
            ReplaceWith = txtReplaceWith.Text?.ToString() ?? "",
            CounterStart = counterStart,
            CounterStep = counterStep,
            CounterDigits = counterDigits,
            CaseMode = (BatchRenameCaseMode)radioCaseMode.SelectedItem
        };
        return true;
    }

    void UpdateFutureNames(IReadOnlyList<string> names)
    {
        int selectedIndex = Math.Clamp(listCurrentNames.SelectedItem, 0, Math.Max(0, names.Count - 1));
        listFutureNames.SetSource(names.ToList());
        listFutureNames.SelectedItem = selectedIndex;
    }

    void RefreshPreview()
    {
        if (!TryReadOptions(out BatchRenameOptions options, out string? error))
        {
            lastPreview = null;
            UpdateFutureNames(currentNames);
            statusLabel.Text = error ?? "Invalid batch rename settings.";
            btnStartBatchRename.Enabled = false;
            return;
        }

        lastPreview = BatchRenameEngine.BuildPreview(activePane.CurrentPath, selectedItems, options);
        UpdateFutureNames(lastPreview.Items.Select(item =>
            item.Error is null ? item.ProposedName : $"{item.ProposedName} [!]").ToList());
        statusLabel.Text = lastPreview.Message;
        btnStartBatchRename.Enabled = !lastPreview.HasErrors;
    }

    void ApplyBatchRename()
    {
        if (lastPreview is null)
        {
            RefreshPreview();
            if (lastPreview is null)
                return;
        }

        if (lastPreview.HasErrors)
        {
            MessageBox.ErrorQuery("Batch Rename", lastPreview.Message, "OK");
            return;
        }

        BatchRenameApplyResult result = BatchRenameEngine.ApplyPreview(lastPreview);
        if (!result.Success)
        {
            if (result.Errors.Count == 0)
            {
                MessageBox.ErrorQuery("Batch Rename", result.Message, "OK");
            }
            else
            {
                int response = MessageBox.ErrorQuery("Batch Rename", result.Message, "Details", "OK");
                if (response == 0)
                    MessageBox.ErrorQuery("Batch Rename Details", string.Join("\n", result.Errors), "OK");
            }

            RefreshPreview();
            return;
        }

        if (result.RenamedCount == 0)
        {
            MessageBox.Query("Batch Rename", result.Message, "OK");
            return;
        }

        activePane.ClearMarks();
        activePane.NavigateTo(activePane.CurrentPath);
        UpdateInfoLabel(activePane);
        UpdatePathDisplay();
        CloseBatchRenameForm();
    }

    void CloseBatchRenameForm()
    {
        Application.RequestStop();
    }

    listCurrentNames.SelectedItemChanged += _ => SyncPreviewSelection(listCurrentNames, listFutureNames);
    listFutureNames.SelectedItemChanged += _ => SyncPreviewSelection(listFutureNames, listCurrentNames);

    txtFileNameMask.TextChanged += _ => RefreshPreview();
    txtExtensionMask.TextChanged += _ => RefreshPreview();
    txtSearchFor.TextChanged += _ => RefreshPreview();
    txtReplaceWith.TextChanged += _ => RefreshPreview();
    txtCounterStart.TextChanged += _ => RefreshPreview();
    txtCounterStep.TextChanged += _ => RefreshPreview();
    txtCounterDigits.TextChanged += _ => RefreshPreview();
    radioCaseMode.SelectedItemChanged += _ => RefreshPreview();

    btnCancelBatchRename.Clicked += CloseBatchRenameForm;
    btnStartBatchRename.Clicked += ApplyBatchRename;
    formBatchRename.KeyPress += (e) =>
    {
        switch (e.KeyEvent.Key)
        {
            case Key.F1:
                CloseBatchRenameForm();
                e.Handled = true;
                break;
            case Key.F5:
                ApplyBatchRename();
                e.Handled = true;
                break;
        }
    };

    isBatchRenameFormOpen = true;
    closeBatchRenameForm = CloseBatchRenameForm;
    submitBatchRenameForm = ApplyBatchRename;

    RefreshPreview();
    txtFileNameMask.SetFocus();
    Application.Run(formBatchRename);

    isBatchRenameFormOpen = false;
    closeBatchRenameForm = null;
    submitBatchRenameForm = null;
    activePane.SetFocusOnList();
    UpdatePathDisplay();
}

void DeleteSelected()
{
    var items = activePane.GetSourceItems();
    if (items.Count == 0) return;

    string summary = items.Count == 1 ? $"'{items[0].Name}'" : $"{items.Count} items";
    int confirm = MessageBox.Query("Delete", $"Delete {summary}?", "Yes", "No");
    if (confirm != 0) return;

    var errors = new List<string>();
    foreach (var item in items)
    {
        string path = Path.Combine(activePane.CurrentPath, item.Name);
        try
        {
            if (item.IsDirectory) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }
        catch (Exception ex) { errors.Add($"{item.Name}: {ex.Message}"); }
    }

    activePane.ClearMarks();
    activePane.NavigateTo(activePane.CurrentPath);
    if (errors.Count > 0)
        MessageBox.ErrorQuery("Delete errors", string.Join("\n", errors), "OK");
}

void CopyDirectory(string src, string dst){
    Directory.CreateDirectory(dst);
    foreach (var file in Directory.GetFiles(src))
        File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
    foreach (var dir in Directory.GetDirectories(src))
        CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
}

// ── ColorSwatchView ───────────────────────────────────────────────────────────

class ColorSwatchView : View
{
    private readonly Color _background;

    private static readonly (string Name, Color Color)[] Colors =
    [
        ("Black",        Color.Black),
        ("Blue",         Color.Blue),
        ("Green",        Color.Green),
        ("Cyan",         Color.Cyan),
        ("Red",          Color.Red),
        ("Magenta",      Color.Magenta),
        ("Brown",        Color.Brown),
        ("Gray",         Color.Gray),
        ("DarkGray",     Color.DarkGray),
        ("BrightBlue",   Color.BrightBlue),
        ("BrightGreen",  Color.BrightGreen),
        ("BrightCyan",   Color.BrightCyan),
        ("BrightRed",    Color.BrightRed),
        ("BrightMagenta",Color.BrightMagenta),
        ("BrightYellow", Color.BrightYellow),
        ("White",        Color.White),
    ];

    public ColorSwatchView(Color background) => _background = background;

    public override void Redraw(Rect bounds)
    {
        const int perRow   = 4;
        const int boxWidth = 16;

        for (int i = 0; i < Colors.Length; i++)
        {
            int row = i / perRow;
            int col = i % perRow;

            var (name, color) = Colors[i];
            string cell = (" " + name).PadRight(boxWidth);

            Driver.SetAttribute(new Terminal.Gui.Attribute(color, _background));
            Move(col * boxWidth, row);
            Driver.AddStr(cell);
        }

        Driver.SetAttribute(ColorScheme.Normal);
    }
}
