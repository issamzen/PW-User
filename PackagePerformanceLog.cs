using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ProfessionalPowerCopyCatalogModern
{
    internal static class PackagePerformanceLog
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Estichara",
            "MoldAutomationCatalog",
            "Logs");

        public static string FilePath => Path.Combine(LogDirectory, "package-performance.csv");

        public static void Append(CatalogItem item, PreparedPackage package)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                bool createHeader = !File.Exists(FilePath);
                var text = new StringBuilder();
                if (createHeader)
                {
                    text.AppendLine("timestamp_utc,computer,template_id,version,mode,bytes,download_seconds,total_prepare_seconds,megabytes_per_second");
                }

                PackageTransferResult transfer = package.Transfer;
                text.Append(Csv(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(Environment.MachineName)).Append(',')
                    .Append(Csv(item.Id)).Append(',')
                    .Append(Csv(item.Version)).Append(',')
                    .Append(package.FromCache ? "cache" : "download").Append(',')
                    .Append(transfer == null ? "0" : transfer.BytesDownloaded.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(transfer == null ? "0" : transfer.Seconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(package.PreparationSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(transfer == null ? "0" : transfer.MegabytesPerSecond.ToString("F3", CultureInfo.InvariantCulture))
                    .AppendLine();

                File.AppendAllText(FilePath, text.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Performance logging must never block CATIA usage.
            }
        }

        public static void EnsureCreated()
        {
            Directory.CreateDirectory(LogDirectory);
            if (!File.Exists(FilePath))
            {
                File.WriteAllText(
                    FilePath,
                    "timestamp_utc,computer,template_id,version,mode,bytes,download_seconds,total_prepare_seconds,megabytes_per_second\r\n",
                    new UTF8Encoding(false));
            }
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }
    }
}
