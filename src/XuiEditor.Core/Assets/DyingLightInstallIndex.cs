using System.IO.Compression;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Assets;

public sealed record DyingLightInstallProfile(
    string InstallPath,
    string Locale = "En")
{
    public string FullPath { get; } =
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallPath));

    public string NormalizedLocale { get; } = NormalizeLocale(Locale);

    public string DataRoot => Path.Combine(FullPath, "DW");

    public static string NormalizeLocale(string? locale)
    {
        string value = string.IsNullOrWhiteSpace(locale)
            ? "En"
            : locale.Trim();
        return value.Length switch
        {
            2 => string.Concat(
                char.ToUpperInvariant(value[0]),
                char.ToLowerInvariant(value[1])),
            _ => "En",
        };
    }
}

public interface IDyingLightInstallIndex : IXuiAssetSource
{
    DyingLightInstallProfile Profile { get; }

    IReadOnlyList<XuiAssetEntry> StockXuiFiles { get; }

    IReadOnlyList<string> AvailableLocales { get; }

    XuiAssetEntry? Find(string virtualPathOrName);
}

public sealed class DyingLightInstallIndex : IDyingLightInstallIndex
{
    private const long MaximumArchiveAssetSize = 512L * 1024 * 1024;
    private readonly object _gate = new();
    private IReadOnlyList<XuiAssetEntry> _entries = [];
    private IReadOnlyList<XuiAssetEntry> _stockXuiFiles = [];
    private IReadOnlyList<string> _availableLocales = [];
    private IReadOnlyList<XuiDiagnostic> _diagnostics = [];
    private bool _isBuilt;
    private Dictionary<string, XuiAssetEntry> _lookup =
        new(StringComparer.OrdinalIgnoreCase);

    public DyingLightInstallIndex(DyingLightInstallProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public DyingLightInstallProfile Profile { get; }

    public string DisplayName =>
        $"Dying Light ({Profile.FullPath})";

    public bool IsReadOnly => true;

    public IReadOnlyList<XuiAssetEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries;
            }
        }
    }

    public IReadOnlyList<XuiAssetEntry> StockXuiFiles
    {
        get
        {
            lock (_gate)
            {
                return _stockXuiFiles;
            }
        }
    }

    public IReadOnlyList<string> AvailableLocales
    {
        get
        {
            lock (_gate)
            {
                return _availableLocales;
            }
        }
    }

    public IReadOnlyList<XuiDiagnostic> Diagnostics
    {
        get
        {
            lock (_gate)
            {
                return _diagnostics;
            }
        }
    }

    public async Task RebuildAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_isBuilt)
            {
                return;
            }
        }

        InstallSnapshot snapshot = await Task.Run(
            () => BuildSnapshot(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _entries = snapshot.Entries;
            _stockXuiFiles = snapshot.StockXuiFiles;
            _availableLocales = snapshot.AvailableLocales;
            _diagnostics = snapshot.Diagnostics;
            _lookup = snapshot.Lookup;
            _isBuilt = true;
        }
    }

    public XuiAssetEntry? Find(string virtualPathOrName)
    {
        if (string.IsNullOrWhiteSpace(virtualPathOrName))
        {
            return null;
        }

        string normalized = virtualPathOrName.Replace('\\', '/').TrimStart('/');
        lock (_gate)
        {
            if (_lookup.TryGetValue(normalized, out XuiAssetEntry? direct))
            {
                return direct;
            }

            return _lookup.GetValueOrDefault(Path.GetFileName(normalized));
        }
    }

    public static bool LooksLikeInstall(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(Path.Combine(fullPath, "DyingLightGame.exe")) &&
                   File.Exists(Path.Combine(fullPath, "DW", "Data0.pak"));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private InstallSnapshot BuildSnapshot(CancellationToken cancellationToken)
    {
        List<XuiAssetEntry> entries = [];
        List<XuiDiagnostic> diagnostics = [];
        if (!LooksLikeInstall(Profile.FullPath))
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-INSTALL001",
                XuiDiagnosticSeverity.Error,
                $"'{Profile.FullPath}' is not a Dying Light installation. Expected DyingLightGame.exe and DW\\Data0.pak."));
            return InstallSnapshot.Empty(diagnostics);
        }

        IReadOnlyList<string> locales = DiscoverLocales(Profile.DataRoot);
        foreach ((string path, string virtualPath, int priority) in
                 EnumerateLooseData(
                     Profile,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IndexLooseFile(
                    path,
                    virtualPath,
                    priority,
                    entries);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-INSTALL004",
                    XuiDiagnosticSeverity.Warning,
                    $"Could not index loose install asset '{path}': {exception.Message}"));
            }
        }

        foreach ((string path, int priority) in EnumeratePakCandidates(
                     Profile,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IndexPak(path, priority, entries, cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-INSTALL002",
                    XuiDiagnosticSeverity.Warning,
                    $"Could not index PAK '{path}': {exception.Message}"));
            }
        }

        foreach ((string path, int priority) in EnumerateMenuRpacks(
                     Profile,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IndexRpack(path, priority, entries);
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-INSTALL003",
                    XuiDiagnosticSeverity.Warning,
                    $"Could not index RP6L menu pack '{path}': {exception.Message}"));
            }
        }

        XuiAssetEntry[] ordered = entries
            .OrderByDescending(static entry => entry.Origin.Priority)
            .ThenBy(static entry => entry.VirtualPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Origin.ContainerPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Dictionary<string, XuiAssetEntry> lookup =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (XuiAssetEntry entry in ordered)
        {
            lookup.TryAdd(entry.VirtualPath, entry);
            lookup.TryAdd(entry.FileName, entry);
        }

        XuiAssetEntry[] stock = ordered
            .Where(static entry =>
                entry.VirtualPath.EndsWith(
                    ".xui",
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                static entry => entry.VirtualPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.VirtualPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        diagnostics.Add(new XuiDiagnostic(
            "XUI-INSTALL000",
            XuiDiagnosticSeverity.Info,
            $"Indexed {stock.Length:N0} stock XUI files and {ordered.Length - stock.Length:N0} supporting assets from the selected Dying Light installation."));
        return new InstallSnapshot(
            ordered,
            stock,
            locales,
            diagnostics,
            lookup);
    }

    private static string[] DiscoverLocales(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
        {
            return ["En"];
        }

        return Directory.EnumerateFiles(dataRoot, "Data??.pak")
            .Select(static path =>
                Path.GetFileNameWithoutExtension(path)["Data".Length..])
            .Where(static locale =>
                locale.Length == 2 &&
                locale.All(char.IsLetter))
            .Select(DyingLightInstallProfile.NormalizeLocale)
            .Append("En")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static locale => locale, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Path, int Priority)>
        EnumeratePakCandidates(
            DyingLightInstallProfile profile,
            CancellationToken cancellationToken)
    {
        DirectoryInfo root = new(profile.FullPath);
        DirectoryInfo[] dataDirectories = root
            .EnumerateDirectories("DW*")
            .Where(static directory =>
                directory.Name.Equals("DW", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.StartsWith(
                    "DW_DLC",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static directory =>
                directory.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int directoryPriority = 10_000;
        foreach (DirectoryInfo directory in dataDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string locale = Path.Combine(
                directory.FullName,
                $"Data{profile.NormalizedLocale}.pak");
            string english = Path.Combine(directory.FullName, "DataEn.pak");
            if (File.Exists(locale))
            {
                yield return (locale, directoryPriority + 200);
            }

            if (!profile.NormalizedLocale.Equals(
                    "En",
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(english))
            {
                yield return (english, directoryPriority + 150);
            }

            string[] dataPaks = directory
                .EnumerateFiles("Data*.pak")
                .Where(static file =>
                    !IsBaseLanguagePack(file.Name))
                .OrderByDescending(
                    static file => file.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static file => file.FullName)
                .ToArray();
            int packPriority = directoryPriority + 100;
            foreach (string dataPak in dataPaks)
            {
                yield return (dataPak, packPriority);
                packPriority--;
            }

            directoryPriority -= 500;
        }
    }

    private static IEnumerable<(
        string Path,
        string VirtualPath,
        int Priority)> EnumerateLooseData(
            DyingLightInstallProfile profile,
            CancellationToken cancellationToken)
    {
        DirectoryInfo root = new(profile.FullPath);
        DirectoryInfo[] dataDirectories = root
            .EnumerateDirectories("DW*")
            .Where(static directory =>
                directory.Name.Equals("DW", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.StartsWith(
                    "DW_DLC",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(
                static directory => directory.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int directoryPriority = 20_000;
        foreach (DirectoryInfo directory in dataDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dataRoot = Path.Combine(directory.FullName, "Data");
            if (!Directory.Exists(dataRoot))
            {
                directoryPriority -= 500;
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(
                         dataRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldIndexPakEntry(path))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(dataRoot, path);
                string virtualPath;
                try
                {
                    virtualPath = XuiAssetEntry.NormalizeVirtualPath(
                        Path.Combine("data", relative));
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                yield return (path, virtualPath, directoryPriority);
            }

            directoryPriority -= 500;
        }
    }

    private static bool IsBaseLanguagePack(string fileName)
    {
        if (fileName.Length != "DataEn.pak".Length ||
            !fileName.StartsWith(
                "Data",
                StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(
                ".pak",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> locale = fileName.AsSpan(4, 2);
        return char.IsLetter(locale[0]) && char.IsLetter(locale[1]);
    }

    private static IEnumerable<(string Path, int Priority)>
        EnumerateMenuRpacks(
            DyingLightInstallProfile profile,
            CancellationToken cancellationToken)
    {
        DirectoryInfo root = new(profile.FullPath);
        DirectoryInfo[] dataDirectories = root
            .EnumerateDirectories("DW*")
            .Where(static directory =>
                directory.Name.Equals("DW", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.StartsWith(
                    "DW_DLC",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static directory =>
                directory.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int directoryPriority = 5_000;
        foreach (DirectoryInfo directory in dataDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string localizedData = Path.Combine(
                directory.FullName,
                $"Data{profile.NormalizedLocale}",
                "Data");
            if (!profile.NormalizedLocale.Equals(
                    "En",
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(localizedData))
            {
                foreach (string path in Directory
                             .EnumerateFiles(
                                 localizedData,
                                 "menu*_PC.rpack")
                             .OrderBy(
                                 static path => path,
                                 StringComparer.OrdinalIgnoreCase))
                {
                    yield return (path, directoryPriority + 50);
                }
            }

            string data = Path.Combine(directory.FullName, "Data");
            if (Directory.Exists(data))
            {
                foreach (string path in Directory
                             .EnumerateFiles(data, "menu*_PC.rpack")
                             .OrderBy(
                                 static path => path,
                                 StringComparer.OrdinalIgnoreCase))
                {
                    yield return (path, directoryPriority);
                }
            }

            directoryPriority -= 100;
        }
    }

    private static void IndexPak(
        string path,
        int priority,
        List<XuiAssetEntry> entries,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry zipEntry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string virtualPath;
            try
            {
                virtualPath = XuiAssetEntry.NormalizeVirtualPath(
                    zipEntry.FullName);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!ShouldIndexPakEntry(virtualPath) ||
                zipEntry.Length < 0 ||
                zipEntry.Length > MaximumArchiveAssetSize)
            {
                continue;
            }

            string entryName = zipEntry.FullName;
            long length = zipEntry.Length;
            XuiAssetOrigin origin = new(
                XuiAssetContainerKind.ZipPak,
                Path.GetFileName(path),
                path,
                entryName,
                IsReadOnly: true,
                priority);
            entries.Add(new XuiAssetEntry(
                virtualPath,
                length,
                origin,
                cancellationToken => ReadZipEntryAsync(
                    path,
                    entryName,
                    length,
                    cancellationToken)));
        }
    }

    private static void IndexLooseFile(
        string path,
        string virtualPath,
        int priority,
        List<XuiAssetEntry> entries)
    {
        FileInfo file = new(path);
        if (!file.Exists ||
            file.Length < 0 ||
            file.Length > MaximumArchiveAssetSize)
        {
            return;
        }

        string fullPath = file.FullName;
        long expectedLength = file.Length;
        XuiAssetOrigin origin = new(
            XuiAssetContainerKind.LooseFile,
            file.Directory?.Name ?? "Data",
            fullPath,
            virtualPath,
            IsReadOnly: true,
            priority);
        entries.Add(new XuiAssetEntry(
            virtualPath,
            expectedLength,
            origin,
            async cancellationToken =>
            {
                FileInfo current = new(fullPath);
                if (!current.Exists ||
                    current.Length != expectedLength)
                {
                    throw new IOException(
                        $"Loose install asset '{fullPath}' changed after the index was built.");
                }

                return await File.ReadAllBytesAsync(
                    fullPath,
                    cancellationToken).ConfigureAwait(false);
            }));
    }

    private static void IndexRpack(
        string path,
        int priority,
        List<XuiAssetEntry> entries)
    {
        Rp6Reader reader = Rp6Reader.Open(path);
        foreach (Rp6ResourceDescriptor resource in reader.Resources)
        {
            if (resource.PayloadType != 32 ||
                string.IsNullOrWhiteSpace(resource.Name))
            {
                continue;
            }

            string safeName = resource.Name
                .Replace('\\', '_')
                .Replace('/', '_');
            if (safeName.Length == 0)
            {
                continue;
            }

            string virtualPath = string.Concat(
                "rpack/",
                Path.GetFileNameWithoutExtension(path),
                "/textures/",
                safeName,
                ".dds");
            XuiAssetOrigin origin = new(
                XuiAssetContainerKind.Rp6Resource,
                Path.GetFileName(path),
                path,
                resource.Name,
                IsReadOnly: true,
                priority);
            entries.Add(new XuiAssetEntry(
                virtualPath,
                length: 0,
                origin,
                async cancellationToken =>
                {
                    byte[] raw = await reader.ReadResourceAsync(
                        resource,
                        cancellationToken).ConfigureAwait(false);
                    return DyingLightDdsBuilder.Build(raw);
                }));
        }
    }

    private static bool ShouldIndexPakEntry(string virtualPath)
    {
        string extension = Path.GetExtension(virtualPath);
        return extension.Equals(".xui", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".def", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".fm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".otf", StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask<byte[]> ReadZipEntryAsync(
        string archivePath,
        string entryName,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new IOException(
                $"PAK entry '{entryName}' no longer exists in '{archivePath}'.");
        if (entry.Length != expectedLength ||
            entry.Length > MaximumArchiveAssetSize)
        {
            throw new IOException(
                $"PAK entry '{entryName}' changed after the install index was built.");
        }

        byte[] result = GC.AllocateUninitializedArray<byte>((int)entry.Length);
        await using Stream stream = entry.Open();
        await stream.ReadExactlyAsync(
            result,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private sealed record InstallSnapshot(
        IReadOnlyList<XuiAssetEntry> Entries,
        IReadOnlyList<XuiAssetEntry> StockXuiFiles,
        IReadOnlyList<string> AvailableLocales,
        IReadOnlyList<XuiDiagnostic> Diagnostics,
        Dictionary<string, XuiAssetEntry> Lookup)
    {
        public static InstallSnapshot Empty(
            IReadOnlyList<XuiDiagnostic> diagnostics) =>
            new(
                [],
                [],
                ["En"],
                diagnostics,
                new Dictionary<string, XuiAssetEntry>(
                    StringComparer.OrdinalIgnoreCase));
    }
}
