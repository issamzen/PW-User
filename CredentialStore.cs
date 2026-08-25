using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ProfessionalPowerCopyCatalogModern
{
    // Stores "remember me" sign-in credentials protected with Windows DPAPI so only
    // this Windows user can decrypt them. Located under %LOCALAPPDATA%.
    public static class CredentialStore
    {
        public class Saved
        {
            public string email { get; set; }
            public string password { get; set; }
        }

        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("MoldAutomationCatalog.RememberMe.v1");

        private static string GetPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Estichara", "MoldAutomationCatalog", "remembered.bin");

        public static void Save(string email, string password)
        {
            try
            {
                var json = new JavaScriptSerializer().Serialize(new Saved { email = email, password = password });
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] crypt = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                string dir = Path.GetDirectoryName(GetPath());
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(GetPath(), crypt);
            }
            catch { }
        }

        public static void Clear()
        {
            try { if (File.Exists(GetPath())) File.Delete(GetPath()); } catch { }
        }

        public static Saved Load()
        {
            try
            {
                if (!File.Exists(GetPath())) return null;
                byte[] crypt = File.ReadAllBytes(GetPath());
                byte[] plain = ProtectedData.Unprotect(crypt, Entropy, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plain);
                var s = new JavaScriptSerializer().Deserialize<Saved>(json);
                return (s != null && !string.IsNullOrEmpty(s.email)) ? s : null;
            }
            catch { return null; }
        }
    }
}
