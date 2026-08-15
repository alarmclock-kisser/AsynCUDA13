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

        public LanguageService(IStringLocalizer<SharedResources> localizer, IHttpContextAccessor httpContextAccessor)
        {
            _localizer = localizer;
            _httpContextAccessor = httpContextAccessor;
        }

        public string this[string key]
        {
            get
            {
                var localizedString = _localizer[key];
                return localizedString.Value;
            }
        }

        public LocalizedString GetLocalizedString(string key)
        {
            return _localizer[key];
        }

        public CultureInfo GetCurrentCulture()
        {
            var httpContext = _httpContextAccessor.HttpContext;
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
                SetCulture(culture);
            }
            catch (CultureNotFoundException)
            {
                // Ungültiger Sprachcode, Kultur nicht setzen
            }
        }

        public bool IsGerman()
        {
            var culture = GetCurrentCulture();
            return culture.TwoLetterISOLanguageName == "de";
        }
    }
}