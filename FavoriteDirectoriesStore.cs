using System.Text;

public sealed class FavoriteDirectoriesStore
{
    public static StringComparer DirectoryPathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly string _storagePath;
    public string StoragePath => _storagePath;

    public FavoriteDirectoriesStore(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(Directory.GetCurrentDirectory(), "favorite-dirs.txt");
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(_storagePath)) return [];

        var favorites = new List<string>();
        var seen = new HashSet<string>(DirectoryPathComparer);

        foreach (string line in File.ReadLines(_storagePath))
        {
            if (!TryNormalizePath(line, out string normalized) || !seen.Add(normalized))
                continue;

            favorites.Add(normalized);
        }

        return favorites;
    }

    public IReadOnlyList<string> Save(IEnumerable<string> directories)
    {
        var favorites = Normalize(directories);
        string directory = Path.GetDirectoryName(_storagePath)
            ?? throw new InvalidOperationException("Favorites storage path must include a directory.");

        Directory.CreateDirectory(directory);

        string tempFile = Path.Combine(directory, Path.GetRandomFileName());
        File.WriteAllLines(tempFile, favorites, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (OperatingSystem.IsWindows() && File.Exists(_storagePath))
            File.Replace(tempFile, _storagePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(tempFile, _storagePath, overwrite: true);

        return favorites;
    }

    public IReadOnlyList<string> Normalize(IEnumerable<string> directories)
    {
        var favorites = new List<string>();
        var seen = new HashSet<string>(DirectoryPathComparer);

        foreach (string directory in directories)
        {
            if (!TryNormalizePath(directory, out string normalized) || !seen.Add(normalized))
                continue;

            favorites.Add(normalized);
        }

        return favorites;
    }

    public static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            normalizedPath = Path.GetFullPath(path.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
