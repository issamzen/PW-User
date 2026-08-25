using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ProfessionalPowerCopyCatalogModern
{
    internal sealed class LicenseApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public string AccessToken { get; private set; }
        public ApiUser CurrentUser { get; private set; }
        public IReadOnlyList<string> Entitlements { get; private set; } = new List<string>();

        public LicenseApiClient(string baseUrl)
        {
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal)) baseUrl += "/";
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl, UriKind.Absolute),
                // CATPart packages can be much larger than JSON API responses.
                Timeout = TimeSpan.FromMinutes(10)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MoldAutomationCatalog/1.0");
        }

        public async Task<LoginResponse> LoginAsync(
            string email,
            string password,
            string deviceId,
            string deviceName)
        {
            var response = await PostAsync<LoginResponse>("login.php", new
            {
                email,
                password,
                device_id = deviceId,
                device_name = deviceName
            }, includeAuthentication: false).ConfigureAwait(false);

            AccessToken = response.access_token;
            CurrentUser = response.user;
            Entitlements = response.entitlements ?? new List<string>();
            ApplyAuthentication();
            return response;
        }

        public async Task<ServerCatalogResponse> GetCatalogAsync()
        {
            return await GetAsync<ServerCatalogResponse>("catalog.php").ConfigureAwait(false);
        }

        public async Task<LeaseResponse> AcquireLeaseAsync(
            string templateId,
            string deviceId,
            string deviceName)
        {
            return await PostAsync<LeaseResponse>("lease.php", new
            {
                template_id = templateId,
                device_id = deviceId,
                device_name = deviceName
            }).ConfigureAwait(false);
        }

        public async Task<PackageTransferResult> DownloadPackageAsync(
            string templateId,
            string leaseToken,
            string destinationPath,
            IProgress<PackageDownloadProgress> progress)
        {
            EnsureAuthenticated();
            if (string.IsNullOrWhiteSpace(templateId))
                throw new ArgumentException("Template ID is required.", nameof(templateId));
            if (string.IsNullOrWhiteSpace(leaseToken))
                throw new ArgumentException("License lease is required.", nameof(leaseToken));

            string endpoint = "package.php?id=" + Uri.EscapeDataString(templateId);
            using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
            {
                request.Headers.Add("X-License-Lease", leaseToken);
                var timer = Stopwatch.StartNew();

                using (HttpResponseMessage response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException(ReadErrorMessage(response, errorBody));
                    }

                    string expectedHash = null;
                    IEnumerable<string> hashHeaders;
                    if (response.Headers.TryGetValues("X-Package-SHA256", out hashHeaders))
                        expectedHash = hashHeaders.FirstOrDefault();

                    bool isEncrypted = false;
                    string packageKey = null;
                    IEnumerable<string> valHeaders;
                    if (response.Headers.TryGetValues("X-Package-Encrypted", out valHeaders))
                        isEncrypted = valHeaders.FirstOrDefault() == "1";
                    if (response.Headers.TryGetValues("X-Package-Key", out valHeaders))
                        packageKey = valHeaders.FirstOrDefault();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    string parent = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

                    long received = 0;
                    byte[] buffer = new byte[81920];
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(
                        destinationPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        buffer.Length,
                        true))
                    {
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                            received += read;
                            progress?.Report(new PackageDownloadProgress
                            {
                                BytesReceived = received,
                                TotalBytes = totalBytes
                            });
                        }
                    }

                    timer.Stop();
                    return new PackageTransferResult
                    {
                        BytesDownloaded = received,
                        ExpectedSha256 = expectedHash,
                        ActualSha256 = PackageManager.ComputeSha256(destinationPath),
                        Seconds = timer.Elapsed.TotalSeconds,
                        IsEncrypted = isEncrypted,
                        PackageKey = packageKey
                    };
                }
            }
        }

        public async Task HeartbeatAsync(string leaseToken)
        {
            await PostAsync<Dictionary<string, object>>("heartbeat.php", new
            {
                lease_token = leaseToken
            }).ConfigureAwait(false);
        }

        public async Task ReleaseAsync(string leaseToken)
        {
            if (string.IsNullOrWhiteSpace(leaseToken)) return;
            await PostAsync<Dictionary<string, object>>("release.php", new
            {
                lease_token = leaseToken
            }).ConfigureAwait(false);
        }

        public async Task<AdminGrantResponse> GrantLicenseAsync(
            string email,
            string displayName,
            string temporaryPassword,
            string licenseName,
            int seats,
            int days,
            string entitlement)
        {
            return await PostAsync<AdminGrantResponse>("admin_grant_license.php", new
            {
                email,
                display_name = displayName,
                temporary_password = temporaryPassword,
                license_name = licenseName,
                seat_count = seats,
                days,
                entitlement
            }).ConfigureAwait(false);
        }

        public async Task<AdminLicenseListResponse> GetAdminLicensesAsync()
        {
            return await GetAsync<AdminLicenseListResponse>("admin_licenses.php").ConfigureAwait(false);
        }

        public void LogoutLocal()
        {
            AccessToken = null;
            CurrentUser = null;
            Entitlements = new List<string>();
            _http.DefaultRequestHeaders.Authorization = null;
        }

        private void ApplyAuthentication()
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        public async Task<MeResponse> GetMeAsync()
        {
            // Returns the user's active license(s) and their expiry, for the "time left" display.
            return await GetAsync<MeResponse>("me.php").ConfigureAwait(false);
        }

        private async Task<T> GetAsync<T>(string endpoint)
        {
            EnsureAuthenticated();
            using (HttpResponseMessage response = await _http.GetAsync(endpoint).ConfigureAwait(false))
            {
                return await ReadResponse<T>(response).ConfigureAwait(false);
            }
        }

        private async Task<T> PostAsync<T>(
            string endpoint,
            object payload,
            bool includeAuthentication = true)
        {
            if (includeAuthentication) EnsureAuthenticated();
            string json = _json.Serialize(payload);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await _http.PostAsync(endpoint, content).ConfigureAwait(false))
            {
                return await ReadResponse<T>(response).ConfigureAwait(false);
            }
        }

        private async Task<T> ReadResponse<T>(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ReadErrorMessage(response, body));

            return _json.Deserialize<T>(body);
        }

        private string ReadErrorMessage(HttpResponseMessage response, string body)
        {
            string message = "Server error " + (int)response.StatusCode;
            try
            {
                var error = _json.Deserialize<Dictionary<string, object>>(body);
                object value;
                if (error != null && error.TryGetValue("error", out value))
                    message = value == null ? message : value.ToString();
            }
            catch { }
            return message;
        }

        private void EnsureAuthenticated()
        {
            if (string.IsNullOrWhiteSpace(AccessToken))
                throw new InvalidOperationException("Login is required.");
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
