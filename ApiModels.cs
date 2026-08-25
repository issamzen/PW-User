using System.Collections.Generic;

namespace ProfessionalPowerCopyCatalogModern
{
    public sealed class ApiUser
    {
        public int id { get; set; }
        public string email { get; set; }
        public string display_name { get; set; }
        public string role { get; set; }
        public string organization { get; set; }
    }

    public sealed class MeLicense
    {
        public int id { get; set; }
        public string license_name { get; set; }
        public int seat_count { get; set; }
        public string expires_at { get; set; }
        public string entitlement_code { get; set; }
    }

    public sealed class MeResponse
    {
        public ApiMeUser user { get; set; }
        public List<MeLicense> licenses { get; set; }
    }

    public sealed class ApiMeUser
    {
        public int id { get; set; }
        public string email { get; set; }
        public string display_name { get; set; }
        public int? organization_id { get; set; }
    }

    public sealed class LoginResponse
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public string expires_at { get; set; }
        public ApiUser user { get; set; }
        public List<string> entitlements { get; set; }
    }

    public sealed class ServerCatalogResponse
    {
        public string CatalogVersion { get; set; }
        public string UpdatedAt { get; set; }
        public List<CatalogItem> Items { get; set; }
    }

    public sealed class LeaseLicenseInfo
    {
        public string name { get; set; }
        public int seat_count { get; set; }
        public string license_expires_at { get; set; }
    }

    public sealed class LeaseResponse
    {
        public string lease_token { get; set; }
        public string lease_expires_at { get; set; }
        public string template_id { get; set; }
        public LeaseLicenseInfo license { get; set; }
    }

    public sealed class AdminGrantResponse
    {
        public bool ok { get; set; }
        public int user_id { get; set; }
        public int license_id { get; set; }
        public string email { get; set; }
        public int seat_count { get; set; }
        public int days { get; set; }
        public string entitlement { get; set; }
    }

    public sealed class AdminLicenseItem
    {
        public int user_id { get; set; }
        public string email { get; set; }
        public string display_name { get; set; }
        public string user_status { get; set; }
        public int? license_id { get; set; }
        public string license_name { get; set; }
        public string license_status { get; set; }
        public int? seat_count { get; set; }
        public string starts_at { get; set; }
        public string expires_at { get; set; }
        public string entitlements { get; set; }
        public int active_seats { get; set; }
    }

    public sealed class AdminLicenseListResponse
    {
        public List<AdminLicenseItem> items { get; set; }
    }
}
