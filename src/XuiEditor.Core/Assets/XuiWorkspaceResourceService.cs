using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Assets;

public sealed record XuiReferenceReplacement(
    string FilePath,
    string NodeDisplay,
    string PropertyName,
    string CurrentValue,
    string ReplacementValue,
    string PropertyNodeKey);

public sealed record XuiReferenceFileSnapshot(
    string FilePath,
    string Sha256);

public sealed record XuiReferencePreflight(
    string WorkspaceRoot,
    string CurrentValue,
    string ReplacementValue,
    IReadOnlyList<XuiReferenceReplacement> Replacements,
    IReadOnlyList<XuiReferenceFileSnapshot> Files);

public sealed record XuiReferenceTransactionResult(
    int ChangedFiles,
    int ChangedReferences,
    string BackupDirectory,
    IReadOnlyList<XuiReferenceFileSnapshot> CommittedFiles);

public sealed record XuiVisualDeleteResult(
    string SourceFile,
    string BackupFile);

public sealed class XuiWorkspaceResourceService
{
    private static readonly HashSet<string> ReferenceProperties =
        new(StringComparer.Ordinal)
        {
            "ImagePath",
            "Visual",
            "Font",
            "DefaultFont",
            "BaseImage",
            "BackgroundTexture",
            "MaskTexture",
        };

    private readonly string _workspaceRoot;

    public XuiWorkspaceResourceService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        Directory.CreateDirectory(_workspaceRoot);
    }

    public string WorkspaceRoot => _workspaceRoot;

    public async Task<string> CreateScreenAsync(
        string relativePath,
        double width = 1280,
        double height = 720,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0 ||
            height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Screen dimensions must be positive finite numbers.");
        }

        string path = ResolveNewXuiPath(relativePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path) ?? _workspaceRoot);
        if (File.Exists(path))
        {
            throw new IOException(
                $"Workspace XUI '{path}' already exists.");
        }

        string text = FormattableString.Invariant(
            $"<XuiCanvas version=\"000c\">\r\n  <Properties>\r\n    <Width>{width:0.000000}</Width>\r\n    <Height>{height:0.000000}</Height>\r\n  </Properties>\r\n</XuiCanvas>\r\n");
        await WriteAtomicAsync(path, text, cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    public async Task<string> CreateVisualAsync(
        string relativePath,
        string visualId,
        double width = 40,
        double height = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visualId);
        if (!double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0 ||
            height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Visual dimensions must be positive finite numbers.");
        }

        string path = ResolveNewXuiPath(relativePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path) ?? _workspaceRoot);
        if (File.Exists(path))
        {
            throw new IOException(
                $"Workspace XUI '{path}' already exists.");
        }

        string id = WebUtility.HtmlEncode(visualId.Trim())
            .Replace("&#39;", "&apos;", StringComparison.Ordinal);
        string text = FormattableString.Invariant(
            $"<XuiCanvas version=\"000c\">\r\n  <Properties>\r\n    <Width>1280.000000</Width>\r\n    <Height>720.000000</Height>\r\n  </Properties>\r\n  <XuiVisual>\r\n    <Properties>\r\n      <Id>{id}</Id>\r\n      <Width>{width:0.000000}</Width>\r\n      <Height>{height:0.000000}</Height>\r\n    </Properties>\r\n  </XuiVisual>\r\n</XuiCanvas>\r\n");
        await WriteAtomicAsync(path, text, cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    public string RenameLooseXui(
        string sourcePath,
        string newRelativePath)
    {
        string source = ResolveExistingLooseXui(sourcePath);
        string destination = ResolveNewXuiPath(newRelativePath);
        if (File.Exists(destination))
        {
            throw new IOException(
                $"Workspace XUI '{destination}' already exists.");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(destination) ?? _workspaceRoot);
        File.Move(source, destination);
        return destination;
    }

    public string DeleteLooseXui(string path)
    {
        string source = ResolveExistingLooseXui(path);
        string relative = Path.GetRelativePath(_workspaceRoot, source);
        string trashRoot = Path.Combine(
            _workspaceRoot,
            ".xui-editor-trash",
            DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss-ffff",
                CultureInfo.InvariantCulture));
        string destination = Path.GetFullPath(
            Path.Combine(trashRoot, relative));
        EnsureContained(destination);
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination) ?? trashRoot);
        File.Move(source, destination);
        return destination;
    }

    public async Task<XuiReferencePreflight> PreflightReplacementAsync(
        string currentValue,
        string replacementValue,
        CancellationToken cancellationToken = default) =>
        await BuildPreflightAsync(
                currentValue,
                replacementValue,
                visualDefinitionFile: null,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<XuiReferencePreflight> PreflightVisualRenameAsync(
        string sourcePath,
        string currentId,
        string replacementId,
        CancellationToken cancellationToken = default)
    {
        string source = ResolveExistingLooseXui(sourcePath);
        return await BuildPreflightAsync(
                currentId,
                replacementId,
                source,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<XuiReferenceTransactionResult>
        ApplyReplacementAsync(
            XuiReferencePreflight preflight,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        if (!Path.GetFullPath(preflight.WorkspaceRoot).Equals(
                _workspaceRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The replacement preflight belongs to another workspace.");
        }

        if (preflight.Replacements.Any(replacement =>
                !replacement.CurrentValue.Equals(
                    preflight.CurrentValue,
                    StringComparison.Ordinal) ||
                !replacement.ReplacementValue.Equals(
                    preflight.ReplacementValue,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The replacement preflight contains inconsistent values.");
        }

        Dictionary<string, XuiReferenceFileSnapshot> snapshots =
            preflight.Files.ToDictionary(
                static snapshot => Path.GetFullPath(snapshot.FilePath),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XuiReferenceReplacement[]> replacementsByFile =
            preflight.Replacements
                .GroupBy(
                    static replacement =>
                        Path.GetFullPath(replacement.FilePath),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        if (snapshots.Count != replacementsByFile.Count ||
            snapshots.Keys.Any(file =>
                !replacementsByFile.ContainsKey(file)))
        {
            throw new InvalidOperationException(
                "The replacement preflight has incomplete file snapshots.");
        }

        List<WorkspaceWritePlan> plans = [];
        foreach ((string file, XuiReferenceReplacement[] replacements)
                 in replacementsByFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveExistingLooseXui(file);
            byte[] original = await File.ReadAllBytesAsync(
                source,
                cancellationToken).ConfigureAwait(false);
            XuiReferenceFileSnapshot snapshot = snapshots[source];
            if (!ContentHash(original).Equals(
                    snapshot.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Reference preflight for '{source}' is stale; no transaction was committed.");
            }

            XuiDocument document = DocumentFromBytes(original, source);
            foreach (XuiReferenceReplacement replacement in replacements)
            {
                XuiSyntaxNode? property =
                    document.SyntaxTree.FindByKey(
                        replacement.PropertyNodeKey);
                if (property is null ||
                    !property.Name.Equals(
                        replacement.PropertyName,
                        StringComparison.Ordinal) ||
                    !property.GetDecodedValue(document.Text).Equals(
                        replacement.CurrentValue,
                        StringComparison.Ordinal) ||
                    !IsAllowedReplacementProperty(property))
                {
                    throw new InvalidOperationException(
                        $"Reference preflight for '{source}' is stale or invalid; no transaction was committed.");
                }

                document.Execute(
                    XuiCommandFactory.SetElementValue(
                        document,
                        property,
                        replacement.ReplacementValue));
            }

            plans.Add(new WorkspaceWritePlan(
                source,
                original,
                document.Format.Encode(document.Text),
                snapshot.Sha256));
        }

        string backupRoot = Path.Combine(
            _workspaceRoot,
            ".xui-editor-backups",
            DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture) +
            $"-{Guid.NewGuid():N}");
        List<WorkspaceWritePlan> committed = [];
        try
        {
            foreach (WorkspaceWritePlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(
                    _workspaceRoot,
                    plan.FilePath);
                string backup = Path.GetFullPath(
                    Path.Combine(backupRoot, relative));
                EnsureContained(backup);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(backup) ?? backupRoot);
                await File.WriteAllBytesAsync(
                    backup,
                    plan.OriginalBytes,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (WorkspaceWritePlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] current = await File.ReadAllBytesAsync(
                    plan.FilePath,
                    cancellationToken).ConfigureAwait(false);
                if (!ContentHash(current).Equals(
                        plan.OriginalHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Workspace XUI '{plan.FilePath}' changed after preflight; committed files will be rolled back.");
                }

                await WriteAtomicBytesAsync(
                    plan.FilePath,
                    plan.ReplacementBytes,
                    cancellationToken).ConfigureAwait(false);
                committed.Add(plan);
            }
        }
        catch (Exception transactionFailure)
        {
            List<Exception> rollbackFailures = [];
            foreach (WorkspaceWritePlan plan in
                     committed.AsEnumerable().Reverse())
            {
                try
                {
                    await WriteAtomicBytesAsync(
                        plan.FilePath,
                        plan.OriginalBytes,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures.Add(rollbackFailure);
                }
            }

            if (rollbackFailures.Count > 0)
            {
                throw new IOException(
                    "The reference transaction failed and at least one committed file could not be rolled back. Restore it from the reported backup directory.",
                    new AggregateException(
                        [transactionFailure, .. rollbackFailures]));
            }

            throw;
        }

        return new XuiReferenceTransactionResult(
            committed.Count,
            preflight.Replacements.Count,
            backupRoot,
            plans.Select(static plan =>
                    new XuiReferenceFileSnapshot(
                        plan.FilePath,
                        ContentHash(plan.ReplacementBytes)))
                .ToArray());
    }

    public async Task<int> UndoReplacementAsync(
        XuiReferenceTransactionResult transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        string backupRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(transaction.BackupDirectory));
        EnsureContained(backupRoot);
        string backupRelative = Path.GetRelativePath(
            _workspaceRoot,
            backupRoot);
        if (!IsBackupRelative(backupRelative) ||
            !Directory.Exists(backupRoot))
        {
            throw new InvalidOperationException(
                "The reference transaction backup directory is unavailable or outside the workspace backup area.");
        }

        List<WorkspaceWritePlan> plans = [];
        foreach (XuiReferenceFileSnapshot snapshot in
                 transaction.CommittedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveExistingLooseXui(snapshot.FilePath);
            byte[] current = await File.ReadAllBytesAsync(
                source,
                cancellationToken).ConfigureAwait(false);
            if (!ContentHash(current).Equals(
                    snapshot.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Workspace XUI '{source}' changed after the reference transaction; undo was not started.");
            }

            string backup = Path.GetFullPath(Path.Combine(
                backupRoot,
                Path.GetRelativePath(_workspaceRoot, source)));
            EnsureContained(backup);
            if (!PathIsInside(backupRoot, backup) ||
                !File.Exists(backup))
            {
                throw new InvalidOperationException(
                    $"The transaction backup for '{source}' is missing.");
            }

            byte[] original = await File.ReadAllBytesAsync(
                backup,
                cancellationToken).ConfigureAwait(false);
            plans.Add(new WorkspaceWritePlan(
                source,
                current,
                original,
                snapshot.Sha256));
        }

        List<WorkspaceWritePlan> committed = [];
        try
        {
            foreach (WorkspaceWritePlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] current = await File.ReadAllBytesAsync(
                    plan.FilePath,
                    cancellationToken).ConfigureAwait(false);
                if (!ContentHash(current).Equals(
                        plan.OriginalHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Workspace XUI '{plan.FilePath}' changed while undo was being prepared.");
                }

                await WriteAtomicBytesAsync(
                    plan.FilePath,
                    plan.ReplacementBytes,
                    cancellationToken).ConfigureAwait(false);
                committed.Add(plan);
            }
        }
        catch
        {
            foreach (WorkspaceWritePlan plan in
                     committed.AsEnumerable().Reverse())
            {
                await WriteAtomicBytesAsync(
                    plan.FilePath,
                    plan.OriginalBytes,
                    CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        return committed.Count;
    }

    public async Task<XuiVisualDeleteResult> DeleteLooseVisualAsync(
        string sourcePath,
        string visualId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visualId);
        string source = ResolveExistingLooseXui(sourcePath);
        XuiReferencePreflight references =
            await BuildPreflightAsync(
                    visualId,
                    "__xui_editor_deleted_visual__",
                    visualDefinitionFile: null,
                    cancellationToken)
                .ConfigureAwait(false);
        if (references.Replacements.Count > 0)
        {
            throw new InvalidOperationException(
                $"Visual '{visualId}' still has {references.Replacements.Count} workspace reference(s). Rebind or clear them before deleting it.");
        }

        byte[] original = await File.ReadAllBytesAsync(
            source,
            cancellationToken).ConfigureAwait(false);
        XuiDocument document = DocumentFromBytes(original, source);
        XuiSyntaxNode[] matches = document.Root
            .DescendantsAndSelf()
            .Where(node =>
                node.Name == "XuiVisual" &&
                string.Equals(
                    XuiModelReader.GetId(node, document.Text),
                    visualId,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                matches.Length == 0
                    ? $"Visual '{visualId}' is no longer present in '{source}'."
                    : $"Visual '{visualId}' is ambiguous in '{source}'.");
        }

        document.Execute(
            XuiCommandFactory.RemoveElement(document, matches[0]));
        string backup = CreateBackupPath(source);
        Directory.CreateDirectory(
            Path.GetDirectoryName(backup) ?? _workspaceRoot);
        await File.WriteAllBytesAsync(
            backup,
            original,
            cancellationToken).ConfigureAwait(false);
        await WriteAtomicBytesAsync(
            source,
            document.Format.Encode(document.Text),
            cancellationToken).ConfigureAwait(false);
        return new XuiVisualDeleteResult(source, backup);
    }

    private async Task<XuiReferencePreflight> BuildPreflightAsync(
        string currentValue,
        string replacementValue,
        string? visualDefinitionFile,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementValue);
        if (currentValue.Equals(
                replacementValue,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The replacement must differ from the current value.");
        }

        string? definitionFile = visualDefinitionFile is null
            ? null
            : ResolveExistingLooseXui(visualDefinitionFile);
        List<XuiReferenceReplacement> replacements = [];
        Dictionary<string, XuiReferenceFileSnapshot> snapshots =
            new(StringComparer.OrdinalIgnoreCase);
        int definitionCount = 0;
        bool replacementDefinitionExists = false;
        foreach (string file in EnumerateWorkspaceXui())
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = await File.ReadAllBytesAsync(
                file,
                cancellationToken).ConfigureAwait(false);
            XuiDocument document = DocumentFromBytes(bytes, file);
            bool fileChanged = false;
            foreach (XuiSyntaxNode node in document.Root
                         .DescendantsAndSelf()
                         .Where(static node =>
                             node.Kind == XuiSyntaxKind.Element &&
                             !XuiModelReader.IsStructural(node)))
            {
                IReadOnlyList<XuiPropertyEntry> properties =
                    XuiModelReader.GetProperties(node, document.Text);
                foreach (XuiPropertyEntry property in properties.Where(
                             property =>
                                 ReferenceProperties.Contains(property.Name) &&
                                 property.Value.Equals(
                                     currentValue,
                                     StringComparison.Ordinal)))
                {
                    replacements.Add(CreateReplacement(
                        file,
                        node,
                        property,
                        document.Text,
                        currentValue,
                        replacementValue));
                    fileChanged = true;
                }

                if (definitionFile is null ||
                    node.Name != "XuiVisual")
                {
                    continue;
                }

                string? id = XuiModelReader.GetId(node, document.Text);
                if (string.Equals(
                        id,
                        replacementValue,
                        StringComparison.Ordinal))
                {
                    replacementDefinitionExists = true;
                }

                if (!file.Equals(
                        definitionFile,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        id,
                        currentValue,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                XuiPropertyEntry? idProperty =
                    properties.LastOrDefault(static property =>
                        property.Name == "Id");
                if (idProperty is null)
                {
                    continue;
                }

                replacements.Add(CreateReplacement(
                    file,
                    node,
                    idProperty,
                    document.Text,
                    currentValue,
                    replacementValue));
                definitionCount++;
                fileChanged = true;
            }

            if (fileChanged)
            {
                snapshots[file] = new XuiReferenceFileSnapshot(
                    file,
                    ContentHash(bytes));
            }
        }

        if (definitionFile is not null)
        {
            if (definitionCount != 1)
            {
                throw new InvalidOperationException(
                    definitionCount == 0
                        ? $"Visual '{currentValue}' is no longer present in '{definitionFile}'."
                        : $"Visual '{currentValue}' is ambiguous in '{definitionFile}'.");
            }

            if (replacementDefinitionExists)
            {
                throw new InvalidOperationException(
                    $"A visual named '{replacementValue}' already exists in the workspace.");
            }
        }

        XuiReferenceReplacement[] orderedReplacements = replacements
            .OrderBy(static replacement =>
                replacement.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static replacement =>
                replacement.PropertyNodeKey,
                StringComparer.Ordinal)
            .ToArray();
        XuiReferenceFileSnapshot[] affectedSnapshots =
            orderedReplacements
                .Select(static replacement => replacement.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(file => snapshots[file])
                .ToArray();
        return new XuiReferencePreflight(
            _workspaceRoot,
            currentValue,
            replacementValue,
            orderedReplacements,
            affectedSnapshots);
    }

    private IEnumerable<string> EnumerateWorkspaceXui()
    {
        const int maximumFiles = 20_000;
        int count = 0;
        Stack<string> pending = new();
        pending.Push(_workspaceRoot);
        while (pending.TryPop(out string? directory))
        {
            string[] files;
            string[] subdirectories;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.xui")
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                subdirectories = Directory.EnumerateDirectories(directory)
                    .OrderByDescending(
                        static path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string file in files)
            {
                if (++count > maximumFiles)
                {
                    yield break;
                }

                yield return Path.GetFullPath(file);
            }

            foreach (string subdirectory in subdirectories)
            {
                string relative = Path.GetRelativePath(
                    _workspaceRoot,
                    subdirectory);
                if (IsMaintenanceRelative(relative))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(subdirectory) &
                         FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(subdirectory);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private string ResolveExistingLooseXui(string path)
    {
        string fullPath = Path.GetFullPath(path);
        EnsureContained(fullPath);
        EnsureNotMaintenancePath(fullPath);
        EnsureNoReparseDirectories(fullPath);
        if (!fullPath.EndsWith(
                ".xui",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Only an existing loose workspace XUI can be modified.",
                fullPath);
        }

        if ((File.GetAttributes(fullPath) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Reparse-point workspace files are not modified.");
        }

        return fullPath;
    }

    private string ResolveNewXuiPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                "Use a path relative to the configured workspace.");
        }

        string withExtension = relativePath.EndsWith(
            ".xui",
            StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : relativePath + ".xui";
        string path = Path.GetFullPath(
            Path.Combine(_workspaceRoot, withExtension));
        EnsureContained(path);
        EnsureNotMaintenancePath(path);
        EnsureNoReparseDirectories(path);
        return path;
    }

    private void EnsureContained(string path)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, path);
        if (relative == ".." ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                "The path escapes the configured workspace.");
        }
    }

    private void EnsureNotMaintenancePath(string path)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, path);
        if (IsMaintenanceRelative(relative))
        {
            throw new InvalidOperationException(
                "Editor recovery and backup files are not live workspace resources.");
        }
    }

    private void EnsureNoReparseDirectories(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is null)
        {
            return;
        }

        string relative = Path.GetRelativePath(_workspaceRoot, parent);
        if (relative == ".")
        {
            return;
        }

        string current = _workspaceRoot;
        foreach (string segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) &
                 FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Workspace resource operations do not traverse reparse-point directories.");
            }
        }
    }

    private static bool IsMaintenanceRelative(string relative)
    {
        string first = FirstRelativeSegment(relative);
        return first.Equals(
                   ".xui-editor-trash",
                   StringComparison.OrdinalIgnoreCase) ||
               IsBackupRelative(relative);
    }

    private static bool IsBackupRelative(string relative) =>
        FirstRelativeSegment(relative).Equals(
            ".xui-editor-backups",
            StringComparison.OrdinalIgnoreCase);

    private static string FirstRelativeSegment(string relative) =>
        relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
        string.Empty;

    private static bool PathIsInside(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." &&
               !relative.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static XuiReferenceReplacement CreateReplacement(
        string file,
        XuiSyntaxNode node,
        XuiPropertyEntry property,
        string source,
        string currentValue,
        string replacementValue) =>
        new(
            file,
            XuiModelReader.GetId(node, source) ?? node.Name,
            property.Name,
            currentValue,
            replacementValue,
            property.Element.Key);

    private static bool IsAllowedReplacementProperty(
        XuiSyntaxNode property)
    {
        if (ReferenceProperties.Contains(property.Name))
        {
            return true;
        }

        return property.Name == "Id" &&
               property.Parent?.Name == "Properties" &&
               property.Parent.Parent?.Name == "XuiVisual";
    }

    private static XuiDocument DocumentFromBytes(
        byte[] bytes,
        string path) =>
        XuiDocument.FromBytes(
            bytes,
            new XuiDocumentSource(
                Path.GetFileName(path),
                path,
                null,
                IsReadOnly: false));

    private static string ContentHash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private string CreateBackupPath(string source)
    {
        string backupRoot = Path.Combine(
            _workspaceRoot,
            ".xui-editor-backups",
            DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture) +
            $"-{Guid.NewGuid():N}");
        string path = Path.GetFullPath(Path.Combine(
            backupRoot,
            Path.GetRelativePath(_workspaceRoot, source)));
        EnsureContained(path);
        return path;
    }

    private static async Task WriteAtomicAsync(
        string path,
        string text,
        CancellationToken cancellationToken) =>
        await WriteAtomicBytesAsync(
                path,
                new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false)
                    .GetBytes(text),
                cancellationToken)
            .ConfigureAwait(false);

    private static async Task WriteAtomicBytesAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                content,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record WorkspaceWritePlan(
        string FilePath,
        byte[] OriginalBytes,
        byte[] ReplacementBytes,
        string OriginalHash);
}
