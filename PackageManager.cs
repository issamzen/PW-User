using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ProfessionalPowerCopyCatalogModern
{
    internal sealed class PackageDownloadProgress
    {
        public long BytesReceived { get; set; }
        public long? TotalBytes { get; set; }

        public int Percent
        {
            get
            {
                if (!TotalBytes.HasValue || TotalBytes.Value <= 0) return 0;
                return (int)Math.Min(100, BytesReceived * 100L / TotalBytes.Value);
            }
        }
    }

    internal sealed class PackageTransferResult
    {
        public long BytesDownloaded { get; set; }
        public string ExpectedSha256 { get; set; }
        public string ActualSha256 { get; set; }
        public double Seconds { get; set; }
        public bool IsEncrypted { get; set; }
        public string PackageKey { get; set; }

        public double Megabytes => BytesDownloaded / 1024d / 1024d;
        public double MegabytesPerSecond => Seconds <= 0 ? 0 : Megabytes / Seconds;
    }

    internal sealed class PowerCopyPackageManifest
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string PowerCopyName { get; set; }
        public string CatPart { get; set; }
        public string CheckScript { get; set; }
        public string CheckFunction { get; set; }
        public string CreatedUtc { get; set; }
    }

    internal sealed class PreparedPackage
    {
        public string RootDirectory { get; set; }
        public string CatPartPath { get; set; }
        public string ScriptPath { get; set; }
        public string CheckFunction { get; set; }
        public string PowerCopyName { get; set; }
        public bool FromCache { get; set; }
        public double PreparationSeconds { get; set; }
        public PackageTransferResult Transfer { get; set; }
    }

    internal sealed class PackageManager
    {
        private const long MaximumExpandedBytes = 2L * 1024L * 1024L * 1024L;
        private const int MaximumEntries = 100;

        private readonly string _packageRoot;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public PackageManager()
        {
            _packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Estichara",
                "MoldAutomationCatalog",
                "Packages");
        }

        public bool IsCached(CatalogItem item)
        {
            if (item == null) return false;
            try
            {
                PreparedPackage ignored;
                return TryLoadCached(item, out ignored);
            }
            catch
            {
                return false;
            }
        }

        public void Clear(CatalogItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;
            string idDirectory = Path.Combine(_packageRoot, SafeSegment(item.Id));
            if (Directory.Exists(idDirectory)) Directory.Delete(idDirectory, true);
        }

        public async Task<PreparedPackage> PrepareAsync(
            CatalogItem item,
            LicenseApiClient api,
            string leaseToken,
            IProgress<PackageDownloadProgress> progress)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (api == null) throw new ArgumentNullException(nameof(api));

            var totalTimer = Stopwatch.StartNew();
            PreparedPackage cached;
            if (TryLoadCached(item, out cached))
            {
                totalTimer.Stop();
                cached.FromCache = true;
                cached.PreparationSeconds = totalTimer.Elapsed.TotalSeconds;
                return cached;
            }

            Directory.CreateDirectory(_packageRoot);
            string downloadDirectory = Path.Combine(_packageRoot, ".downloads");
            Directory.CreateDirectory(downloadDirectory);
            string downloadPath = Path.Combine(
                downloadDirectory,
                SafeSegment(item.Id) + "_" + Guid.NewGuid().ToString("N") + ".pcpkg");

            string stagingDirectory = Path.Combine(
                _packageRoot,
                ".staging_" + SafeSegment(item.Id) + "_" + Guid.NewGuid().ToString("N"));

            try
            {
                PackageTransferResult transfer = await api.DownloadPackageAsync(
                    item.Id,
                    leaseToken,
                    downloadPath,
                    progress);

                if (string.IsNullOrWhiteSpace(transfer.ExpectedSha256))
                    throw new InvalidOperationException("package_hash_missing");

                if (!string.Equals(
                    transfer.ExpectedSha256,
                    transfer.ActualSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("package_integrity_error");
                }

                // Decide whether this package is encrypted. An encrypted container is
                // recognised by the "PCPK" magic; the key came from the server headers.
                string extractionSource = downloadPath;
                if (transfer.IsEncrypted || string.IsNullOrEmpty(transfer.PackageKey) == false)
                {
                    string plain = downloadPath + ".decrypted";
                    byte[] container = File.ReadAllBytes(downloadPath);
                    if (!PackageCrypto.IsEncryptedContainer(container))
                        throw new InvalidOperationException("package_decrypt_error");
                    if (string.IsNullOrWhiteSpace(transfer.PackageKey))
                        throw new InvalidOperationException("package_decrypt_error");
                    byte[] decrypted;
                    try
                    {
                        decrypted = PackageCrypto.Decrypt(container, transfer.PackageKey);
                    }
                    catch
                    {
                        throw new InvalidOperationException("package_decrypt_error");
                    }
                    File.WriteAllBytes(plain, decrypted);
                    extractionSource = plain;
                }

                await Task.Run(() => ExtractSafely(extractionSource, stagingDirectory));
                if (!string.Equals(extractionSource, downloadPath, StringComparison.Ordinal))
                    TryDeleteFile(extractionSource);
                PreparedPackage prepared = LoadAndValidate(stagingDirectory, item);

                string finalDirectory = GetVersionDirectory(item);
                string idDirectory = Path.GetDirectoryName(finalDirectory);
                Directory.CreateDirectory(idDirectory);
                if (Directory.Exists(finalDirectory)) Directory.Delete(finalDirectory, true);
                Directory.Move(stagingDirectory, finalDirectory);

                prepared = LoadAndValidate(finalDirectory, item);
                totalTimer.Stop();
                prepared.FromCache = false;
                prepared.PreparationSeconds = totalTimer.Elapsed.TotalSeconds;
                prepared.Transfer = transfer;
                return prepared;
            }
            finally
            {
                TryDeleteFile(downloadPath);
                TryDeleteDirectory(stagingDirectory);
            }
        }

        private bool TryLoadCached(CatalogItem item, out PreparedPackage prepared)
        {
            string directory = GetVersionDirectory(item);
            if (!Directory.Exists(directory))
            {
                prepared = null;
                return false;
            }

            try
            {
                prepared = LoadAndValidate(directory, item);
                return true;
            }
            catch
            {
                TryDeleteDirectory(directory);
                prepared = null;
                return false;
            }
        }

        private PreparedPackage LoadAndValidate(string rootDirectory, CatalogItem item)
        {
            string manifestPath = Path.Combine(rootDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("Package manifest.json is missing.");

            PowerCopyPackageManifest manifest =
                _json.Deserialize<PowerCopyPackageManifest>(File.ReadAllText(manifestPath));

            if (manifest == null) throw new InvalidDataException("Package manifest is invalid.");
            if (!string.Equals(manifest.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package template ID does not match the catalog.");
            if (!string.Equals(manifest.Version, item.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package version does not match the catalog.");
            if (string.IsNullOrWhiteSpace(manifest.PowerCopyName))
                throw new InvalidDataException("PowerCopyName is missing from the package manifest.");
            if (string.IsNullOrWhiteSpace(manifest.CheckFunction))
                throw new InvalidDataException("CheckFunction is missing from the package manifest.");

            string catPartPath = ResolvePackageFile(rootDirectory, manifest.CatPart);
            string scriptPath = ResolvePackageFile(rootDirectory, manifest.CheckScript);
            if (!File.Exists(catPartPath)) throw new FileNotFoundException("Packaged CATPart is missing.", catPartPath);
            if (!File.Exists(scriptPath)) throw new FileNotFoundException("Packaged CATScript is missing.", scriptPath);

            return new PreparedPackage
            {
                RootDirectory = rootDirectory,
                CatPartPath = catPartPath,
                ScriptPath = scriptPath,
                CheckFunction = manifest.CheckFunction,
                PowerCopyName = manifest.PowerCopyName
            };
        }

        private string GetVersionDirectory(CatalogItem item)
        {
            return Path.Combine(
                _packageRoot,
                SafeSegment(item.Id),
                SafeSegment(item.Version));
        }

        private static string ResolvePackageFile(string rootDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("Package manifest contains an invalid file path.");

            string root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package manifest path escapes the package directory.");
            return candidate;
        }

        private static void ExtractSafely(string packagePath, string stagingDirectory)
        {
            Directory.CreateDirectory(stagingDirectory);
            string root = Path.GetFullPath(stagingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using (ZipArchive archive = ZipFile.OpenRead(packagePath))
            {
                if (archive.Entries.Count > MaximumEntries)
                    throw new InvalidDataException("Package contains too many files.");

                long expandedBytes = archive.Entries.Sum(entry => entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                    throw new InvalidDataException("Expanded package is too large.");

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destination = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Unsafe path found inside package.");

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    entry.ExtractToFile(destination, true);
                }
            }
        }

        internal static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string SafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
