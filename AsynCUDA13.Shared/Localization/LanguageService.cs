using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace AsynCUDA13.Shared.Localization
{
    public class LanguageService
    {
        private readonly IStringLocalizer<SharedResources> _localizer;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public string Runtime { get; set; } = "CPU"; // "cuda" or "opencl", default is "CPU"

        public LanguageService(IStringLocalizer<SharedResources> localizer, IHttpContextAccessor httpContextAccessor, string runtime = "cuda")
        {
            this._localizer = localizer;
            this._httpContextAccessor = httpContextAccessor;
            this.Runtime = runtime;
        }

        public string this[string key]
        {
            get
            {
                var LocalizedString = this._localizer[key];
                return this.Runtime.Equals("cuda", StringComparison.OrdinalIgnoreCase) ? LocalizedString.Value : this.Runtime.Equals("opencl", StringComparison.OrdinalIgnoreCase) ? LocalizedString.Value.Replace("cuda", "OpenCL", StringComparison.OrdinalIgnoreCase) : LocalizedString.Value.Replace("cuda", this.Runtime, StringComparison.OrdinalIgnoreCase);
            }
        }

        public LocalizedString GetLocalizedString(string key)
        {
            return this._localizer[key];
        }

        public CultureInfo GetCurrentCulture()
        {
            var httpContext = this._httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                var requestCultureFeature = httpContext.Features.Get<RequestCultureFeature>();
                if (requestCultureFeature?.RequestCulture?.Culture != null)
                {
                    return requestCultureFeature.RequestCulture.Culture;
                }
            }

            return CultureInfo.CurrentCulture;
        }

        public void SetCulture(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void SetLanguage(string languageCode)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(languageCode);
                this.SetCulture(culture);
            }
            catch (CultureNotFoundException)
            {
                // Ungültiger Sprachcode, Kultur nicht setzen
            }
        }

        public Boolean IsGerman()
        {
            var culture = this.GetCurrentCulture();
            return culture.TwoLetterISOLanguageName == "de";
        }
    }
}