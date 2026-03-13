# Copilot Instructions for `elcommander`

## Build and run commands

- Restore/build with the local package source configured in `NuGet.Config`:
  - `dotnet build elcommander.sln`
  - `dotnet build elcommander.csproj`
- Run the app directly from source:
  - `dotnet run --project elcommander.csproj`
- Run the built Debug launcher scripts after a successful Debug build:
  - Linux/macOS: `./elcomm.sh`
  - Windows: `elcomm.bat`

## Test and lint commands

- There are currently no test projects or lint scripts in this repository.
- If tests are added later, prefer standard .NET commands scoped to the relevant test project, for example:
  - Full suite: `dotnet test <test-project>.csproj`
  - Single test: `dotnet test <test-project>.csproj --filter "FullyQualifiedName~<test-name-fragment>"`

## High-level architecture

- `Program.cs` is the application entry point and main composition root. It uses top-level statements to initialize `Terminal.Gui`, enforce a minimum terminal size, create the full window layout, wire pane events, and implement the file-operation commands (`Copy`, `Move`, `New Folder`, `Delete`, `Edit`, `Terminal`, `Exit`, color dialog).
- The UI is a two-pane file manager. `Program.cs` creates two `FileExplorerPane` instances (`leftPane` and `rightPane`) that share the same behavior but operate independently. The active pane determines which path is shown in the read-only path field and which pane receives command-bar actions.
- `FileExplorerPane.cs` owns the per-pane UI and state: current path, editable path field, sortable list header, file list, selection handling, multi-select/mark behavior, directory navigation, and summary/status data for the info panes.
- `FileListDataSource.cs` provides both the file item model (`FileListItem`) and the custom `IListDataSource` implementation that renders each row. This is where name/size/date/attribute columns, display formatting, and user mark state live.
- `Platform.cs` isolates OS-specific behavior for opening a terminal and opening files with the default system application. `Program.cs` and `FileExplorerPane.cs` call into it rather than branching on OS directly.

## Key repository conventions

- This codebase uses C# top-level statements in `Program.cs` instead of a `Program` class. When adding app-level behavior, follow the existing pattern of local functions plus event wiring near the UI setup.
- The file list always includes a synthetic parent entry (`".."`) at index `0`. Logic that iterates real directory contents usually starts at index `1`, and `".."` must stay excluded from bulk mark/select operations.
- Directory ordering and file ordering are intentionally different. Directories stay grouped first and sorted by name, while the selected sort column (`Name`, `Size`, `Date`) only affects files.
- Multi-select is implemented with custom mark tracking in `FileListDataSource` (`BitArray _userMarks`), not Terminal.Gui's built-in marking. `IsMarked`/`SetMark` are intentionally no-ops so Terminal.Gui cannot mutate the selection state.
- The header layout in `FileListHeaderView` must stay aligned with `FileListDataSource.Render()`. If column widths or labels change, update both together.
- File attributes are presented in a 3-character `rwx`-style column. On Windows, execute status is inferred from file extension; on Unix-like systems it is derived from `File.GetUnixFileMode`.
- External file/terminal launching is gated by `Platform.HasGraphicalEnvironment`. Preserve that check when adding GUI-dependent actions so the TUI still behaves sensibly in headless sessions.
- The repo is set up to restore packages from the local `packages/` directory via `NuGet.Config`. Avoid assuming internet-backed NuGet restore is available.
- `elcomm.sh` and `elcomm.bat` are thin launchers for the built Debug output under `bin/Debug/net8.0/`; they are not substitutes for building the project first.
