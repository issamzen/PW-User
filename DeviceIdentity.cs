using System;
using System.IO;

namespace ProfessionalPowerCopyCatalogModern
{
    internal static class DeviceIdentity
    {
        public static string GetOrCreate()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YourCompany",
                "MoldAutomationCatalog");
            string path = Path.Combine(folder, "device.id");

            Directory.CreateDirectory(folder);

            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (Guid.TryParse(existing, out Guid parsed)) return parsed.ToString();
            }

            string value = Guid.NewGuid().ToString();
            File.WriteAllText(path, value);
            return value;
        }
    }
}
