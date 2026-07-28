namespace XuiEditor.Core.Assets;

public sealed record XuiDocumentAssetContext(
    string DocumentDirectory,
    XuiAssetRoot Root)
{
    public static XuiDocumentAssetContext Discover(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        string fullPath = Path.GetFullPath(documentPath);
        string directory = Directory.Exists(fullPath)
            ? Path.TrimEndingDirectorySeparator(fullPath)
            : Path.GetDirectoryName(fullPath) ??
              throw new ArgumentException(
                  "The document path has no containing directory.",
                  nameof(documentPath));

        DirectoryInfo? current = new(directory);
        while (current is not null)
        {
            if (current.Name.Equals(
                    "data",
                    StringComparison.OrdinalIgnoreCase) &&
                IsWithinNamedChild(
                    current.FullName,
                    directory,
                    "menu"))
            {
                return Create(directory, current.FullName);
            }

            if (current.Name.Equals(
                    "PakAssets",
                    StringComparison.OrdinalIgnoreCase) &&
                (IsWithinNamedChild(
                     current.FullName,
                     directory,
                     "XUI") ||
                 Directory.Exists(
                     Path.Combine(current.FullName, "Locale"))))
            {
                return Create(directory, current.FullName);
            }

            if (Directory.Exists(
                    Path.Combine(current.FullName, "Locale")) ||
                Directory.Exists(
                    Path.Combine(
                        current.FullName,
                        "Data",
                        "Locale")))
            {
                return Create(directory, current.FullName);
            }

            current = current.Parent;
        }

        return Create(directory, directory);
    }

    private static bool IsWithinNamedChild(
        string root,
        string candidate,
        string childName)
    {
        string relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return false;
        }

        string firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
            string.Empty;
        return firstSegment.Equals(
            childName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static XuiDocumentAssetContext Create(
        string documentDirectory,
        string root) =>
        new(
            documentDirectory,
            new XuiAssetRoot(
                root,
                XuiAssetRootKind.Workspace,
                IsReadOnly: false));
}
