using System.Text.RegularExpressions;

public enum BatchRenameCaseMode
{
    Unchanged,
    Upper,
    Lower,
    TitleCase,
    SentenceCase
}

public sealed class BatchRenameOptions
{
    public required string FileNameMask { get; init; }
    public required string ExtensionMask { get; init; }
    public required string SearchFor { get; init; }
    public required string ReplaceWith { get; init; }
    public required int CounterStart { get; init; }
    public required int CounterStep { get; init; }
    public required int CounterDigits { get; init; }
    public required BatchRenameCaseMode CaseMode { get; init; }
}

public sealed class BatchRenamePreviewItem
{
    public required FileListItem SourceItem { get; init; }
    public required string SourceFullPath { get; init; }
    public required string CurrentName { get; init; }
    public required string ProposedName { get; init; }
    public required string TargetFullPath { get; init; }
    public string? Error { get; set; }
}

public sealed class BatchRenamePreviewResult
{
    public required IReadOnlyList<BatchRenamePreviewItem> Items { get; init; }
    public required string Message { get; init; }
    public bool HasErrors => Items.Any(item => item.Error is not null);
}

public sealed class BatchRenameApplyResult
{
    public required bool Success { get; init; }
    public required int RenamedCount { get; init; }
    public required string Message { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}

public static class BatchRenameEngine
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Regex WordRegex = new(@"[A-Za-z0-9]+", RegexOptions.Compiled);

    public static BatchRenamePreviewResult BuildPreview(
        string currentPath,
        IReadOnlyList<FileListItem> selectedItems,
        BatchRenameOptions options)
    {
        var previewItems = new List<BatchRenamePreviewItem>(selectedItems.Count);
        var sourcePaths = new HashSet<string>(
            selectedItems.Select(item => Path.Combine(currentPath, item.Name)),
            PathComparer);
        var duplicateTracker = new Dictionary<string, List<BatchRenamePreviewItem>>(PathComparer);

        for (int index = 0; index < selectedItems.Count; index++)
        {
            FileListItem item = selectedItems[index];
            string sourceFullPath = Path.Combine(currentPath, item.Name);
            string currentBaseName = Path.GetFileNameWithoutExtension(item.Name);
            string currentExtension = Path.GetExtension(item.Name).TrimStart('.');
            int counterValue = options.CounterStart + (index * options.CounterStep);

            string nextBaseName = ExpandMask(options.FileNameMask, currentBaseName, currentExtension, counterValue, options.CounterDigits);
            string nextExtension = ExpandMask(options.ExtensionMask, currentBaseName, currentExtension, counterValue, options.CounterDigits)
                .TrimStart('.');

            nextBaseName = ApplySearchReplace(nextBaseName, options.SearchFor, options.ReplaceWith);
            nextExtension = ApplySearchReplace(nextExtension, options.SearchFor, options.ReplaceWith);

            nextBaseName = ApplyCase(nextBaseName, options.CaseMode);
            nextExtension = ApplyCase(nextExtension, options.CaseMode);

            string proposedName = BuildFileName(nextBaseName, nextExtension);
            var previewItem = new BatchRenamePreviewItem
            {
                SourceItem = item,
                SourceFullPath = sourceFullPath,
                CurrentName = item.Name,
                ProposedName = proposedName,
                TargetFullPath = Path.Combine(currentPath, proposedName)
            };

            previewItem.Error = ValidateName(nextBaseName, nextExtension);
            previewItems.Add(previewItem);

            if (previewItem.Error is null)
            {
                if (!duplicateTracker.TryGetValue(proposedName, out var duplicates))
                {
                    duplicates = [];
                    duplicateTracker[proposedName] = duplicates;
                }

                duplicates.Add(previewItem);
            }
        }

        foreach ((string proposedName, var duplicates) in duplicateTracker)
        {
            if (duplicates.Count > 1)
            {
                foreach (BatchRenamePreviewItem previewItem in duplicates)
                    previewItem.Error = $"Duplicate target name '{proposedName}'.";
            }
        }

        foreach (BatchRenamePreviewItem previewItem in previewItems.Where(item => item.Error is null))
        {
            if ((File.Exists(previewItem.TargetFullPath) || Directory.Exists(previewItem.TargetFullPath))
                && !sourcePaths.Contains(previewItem.TargetFullPath))
            {
                previewItem.Error = $"Target '{previewItem.ProposedName}' already exists.";
            }
        }

        int invalidCount = previewItems.Count(item => item.Error is not null);
        int changedCount = previewItems.Count(item => !string.Equals(item.CurrentName, item.ProposedName, StringComparison.Ordinal));
        string message = invalidCount > 0
            ? $"{invalidCount} invalid entr{(invalidCount == 1 ? "y" : "ies")}. First issue: {previewItems.First(item => item.Error is not null).Error}"
            : changedCount == 0
                ? "Preview ready. No file names would change."
                : $"Preview ready. {changedCount} file{(changedCount == 1 ? "" : "s")} will be renamed.";

        return new BatchRenamePreviewResult
        {
            Items = previewItems,
            Message = message
        };
    }

    public static BatchRenameApplyResult ApplyPreview(BatchRenamePreviewResult preview)
    {
        if (preview.HasErrors)
        {
            return new BatchRenameApplyResult
            {
                Success = false,
                RenamedCount = 0,
                Message = "Batch rename cannot start because the preview contains invalid entries.",
                RollbackSucceeded = true,
                Errors = preview.Items.Where(item => item.Error is not null).Select(item => item.Error!).Distinct().ToList()
            };
        }

        var operations = preview.Items
            .Where(item => !string.Equals(item.SourceFullPath, item.TargetFullPath, StringComparison.Ordinal))
            .Select(item => new PendingRename(item.SourceFullPath, item.TargetFullPath))
            .ToList();

        if (operations.Count == 0)
        {
            return new BatchRenameApplyResult
            {
                Success = true,
                RenamedCount = 0,
                Message = "No files need renaming.",
                RollbackSucceeded = true,
                Errors = []
            };
        }

        var movedToTemp = new List<StagedRename>(operations.Count);
        PendingRename? failedStage = null;
        try
        {
            foreach (PendingRename operation in operations)
            {
                failedStage = operation;
                string tempPath = CreateTempPath(operation.SourceFullPath);
                File.Move(operation.SourceFullPath, tempPath, overwrite: false);
                movedToTemp.Add(new StagedRename(operation.SourceFullPath, tempPath, operation.TargetFullPath));
            }
        }
        catch (Exception ex)
        {
            var rollbackErrors = RollbackTempMoves(movedToTemp);
            return BuildFailureResult(
                failedStage,
                ex,
                rollbackErrors,
                isFinalizing: false);
        }

        var completedTargets = new List<StagedRename>(operations.Count);
        StagedRename? failedFinalize = null;
        try
        {
            foreach (StagedRename stagedRename in movedToTemp)
            {
                failedFinalize = stagedRename;
                File.Move(stagedRename.TempFullPath, stagedRename.TargetFullPath, overwrite: false);
                completedTargets.Add(stagedRename);
            }
        }
        catch (Exception ex)
        {
            var rollbackErrors = RollbackFailedApply(completedTargets, movedToTemp);
            PendingRename failedOperation = failedFinalize is null
                ? new PendingRename("", "")
                : new PendingRename(failedFinalize.SourceFullPath, failedFinalize.TargetFullPath);

            return BuildFailureResult(
                failedOperation,
                ex,
                rollbackErrors,
                isFinalizing: true);
        }

        return new BatchRenameApplyResult
        {
            Success = true,
            RenamedCount = completedTargets.Count,
            Message = $"Batch rename completed. {completedTargets.Count} file{(completedTargets.Count == 1 ? "" : "s")} renamed.",
            RollbackSucceeded = true,
            Errors = []
        };
    }

    private static BatchRenameApplyResult BuildFailureResult(
        PendingRename? failedOperation,
        Exception exception,
        IReadOnlyList<string> rollbackErrors,
        bool isFinalizing)
    {
        string sourceName = failedOperation is null || string.IsNullOrEmpty(failedOperation.SourceFullPath)
            ? "an unknown file"
            : $"'{Path.GetFileName(failedOperation.SourceFullPath)}'";
        string targetName = failedOperation is null || string.IsNullOrEmpty(failedOperation.TargetFullPath)
            ? "its target name"
            : $"'{Path.GetFileName(failedOperation.TargetFullPath)}'";
        string message = isFinalizing
            ? $"Batch rename failed while renaming {sourceName} to {targetName}: {exception.Message}"
            : $"Batch rename failed while staging {sourceName}: {exception.Message}";

        bool rollbackSucceeded = rollbackErrors.Count == 0;
        message += rollbackSucceeded
            ? " All earlier rename steps were rolled back, so no partial rename was left behind."
            : " Rollback also failed for some files, so manual recovery may be required.";

        var errors = new List<string>
        {
            isFinalizing
                ? $"Final rename failed for {sourceName} -> {targetName}: {exception.Message}"
                : $"Staging rename failed for {sourceName}: {exception.Message}"
        };
        errors.AddRange(rollbackErrors);

        return new BatchRenameApplyResult
        {
            Success = false,
            RenamedCount = 0,
            Message = message,
            RollbackSucceeded = rollbackSucceeded,
            Errors = errors
        };
    }

    private static string ExpandMask(string mask, string currentBaseName, string currentExtension, int counterValue, int counterDigits)
    {
        string counterText = counterValue.ToString($"D{counterDigits}");
        return mask
            .Replace("[N]", currentBaseName, StringComparison.Ordinal)
            .Replace("[E]", currentExtension, StringComparison.Ordinal)
            .Replace("[C]", counterText, StringComparison.Ordinal);
    }

    private static string ApplySearchReplace(string input, string searchFor, string replaceWith)
    {
        if (string.IsNullOrEmpty(searchFor))
            return input;

        return input.Replace(searchFor, replaceWith, StringComparison.Ordinal);
    }

    private static string ApplyCase(string input, BatchRenameCaseMode caseMode) =>
        caseMode switch
        {
            BatchRenameCaseMode.Upper => input.ToUpperInvariant(),
            BatchRenameCaseMode.Lower => input.ToLowerInvariant(),
            BatchRenameCaseMode.TitleCase => WordRegex.Replace(
                input.ToLowerInvariant(),
                match => char.ToUpperInvariant(match.Value[0]) + match.Value[1..]),
            BatchRenameCaseMode.SentenceCase => ApplySentenceCase(input),
            _ => input,
        };

    private static string ApplySentenceCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        string lower = input.ToLowerInvariant();
        for (int i = 0; i < lower.Length; i++)
        {
            if (!char.IsLetterOrDigit(lower[i]))
                continue;

            return string.Concat(char.ToUpperInvariant(lower[i]), lower[(i + 1)..]);
        }

        return lower;
    }

    private static string? ValidateName(string baseName, string extension)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return "File name cannot be empty.";

        if (baseName is "." or "..")
            return "File name cannot be '.' or '..'.";

        if (baseName.IndexOfAny(InvalidFileNameChars) >= 0)
            return "File name contains invalid characters.";

        if (baseName.Contains(Path.DirectorySeparatorChar) || baseName.Contains(Path.AltDirectorySeparatorChar))
            return "File name cannot contain path separators.";

        if (string.IsNullOrEmpty(extension))
            return null;

        if (extension.Contains('.'))
            return "Extension cannot contain '.'.";

        if (extension.IndexOfAny(InvalidFileNameChars) >= 0)
            return "Extension contains invalid characters.";

        if (extension.Contains(Path.DirectorySeparatorChar) || extension.Contains(Path.AltDirectorySeparatorChar))
            return "Extension cannot contain path separators.";

        return null;
    }

    private static string BuildFileName(string baseName, string extension) =>
        string.IsNullOrEmpty(extension) ? baseName : $"{baseName}.{extension}";

    private static string CreateTempPath(string sourceFullPath)
    {
        string directory = Path.GetDirectoryName(sourceFullPath)
            ?? throw new InvalidOperationException("Source path has no parent directory.");

        while (true)
        {
            string candidate = Path.Combine(directory, $".elcommander-batchrename-{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static List<string> RollbackTempMoves(IEnumerable<StagedRename> stagedRenames)
    {
        var errors = new List<string>();
        foreach (StagedRename stagedRename in stagedRenames.Reverse())
        {
            try
            {
                if (File.Exists(stagedRename.TempFullPath))
                    File.Move(stagedRename.TempFullPath, stagedRename.SourceFullPath, overwrite: false);
            }
            catch (Exception ex)
            {
                errors.Add($"Rollback failed for '{Path.GetFileName(stagedRename.SourceFullPath)}': {ex.Message}");
            }
        }

        return errors;
    }

    private static List<string> RollbackFailedApply(
        IReadOnlyList<StagedRename> completedTargets,
        IReadOnlyList<StagedRename> stagedRenames)
    {
        var errors = new List<string>();
        var completedLookup = new HashSet<string>(completedTargets.Select(item => item.SourceFullPath), PathComparer);

        foreach (StagedRename stagedRename in completedTargets.Reverse())
        {
            try
            {
                if (File.Exists(stagedRename.TargetFullPath))
                    File.Move(stagedRename.TargetFullPath, stagedRename.SourceFullPath, overwrite: false);
            }
            catch (Exception ex)
            {
                errors.Add($"Rollback failed for '{Path.GetFileName(stagedRename.SourceFullPath)}': {ex.Message}");
            }
        }

        foreach (StagedRename stagedRename in stagedRenames.Reverse())
        {
            if (completedLookup.Contains(stagedRename.SourceFullPath))
                continue;

            try
            {
                if (File.Exists(stagedRename.TempFullPath))
                    File.Move(stagedRename.TempFullPath, stagedRename.SourceFullPath, overwrite: false);
            }
            catch (Exception ex)
            {
                errors.Add($"Rollback failed for '{Path.GetFileName(stagedRename.SourceFullPath)}': {ex.Message}");
            }
        }

        return errors;
    }

    private sealed record PendingRename(string SourceFullPath, string TargetFullPath);
    private sealed record StagedRename(string SourceFullPath, string TempFullPath, string TargetFullPath);
}
