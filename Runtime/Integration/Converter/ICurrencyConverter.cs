using System.Collections;

namespace Com.Hapiga.Scheherazade.Common.Integration.Converter
{
    public enum CurrencyConverterStatus
    {
        NotInitialized,
        Initializing,
        Initialized,
        Failed
    }

    public static class CurrencyCode
    {
        public const string AED = "aed"; // United Arab Emirates
        public const string AFN = "afn"; // Afghanistan
        public const string ALL = "all"; // Albania
        public const string AMD = "amd"; // Armenia
        public const string ANG = "ang"; // Curaçao / Sint Maarten (Netherlands Caribbean)
        public const string AOA = "aoa"; // Angola
        public const string ARS = "ars"; // Argentina
        public const string AUD = "aud"; // Australia
        public const string AWG = "awg"; // Aruba
        public const string AZN = "azn"; // Azerbaijan
        public const string BAM = "bam"; // Bosnia and Herzegovina
        public const string BBD = "bbd"; // Barbados
        public const string BDT = "bdt"; // Bangladesh
        public const string BGN = "bgn"; // Bulgaria
        public const string BHD = "bhd"; // Bahrain
        public const string BIF = "bif"; // Burundi
        public const string BMD = "bmd"; // Bermuda
        public const string BND = "bnd"; // Brunei Darussalam
        public const string BOB = "bob"; // Bolivia
        public const string BOV = "bov"; // Bolivia (Mvdol)
        public const string BRL = "brl"; // Brazil
        public const string BSD = "bsd"; // Bahamas
        public const string BTN = "btn"; // Bhutan
        public const string BWP = "bwp"; // Botswana
        public const string BYN = "byn"; // Belarus
        public const string BZD = "bzd"; // Belize
        public const string CAD = "cad"; // Canada
        public const string CDF = "cdf"; // Democratic Republic of the Congo
        public const string CHE = "che"; // WIR Euro (Switzerland - special)
        public const string CHF = "chf"; // Switzerland
        public const string CHW = "chw"; // WIR Franc (Switzerland - special)
        public const string CLF = "clf"; // Chile (Unidad de Fomento)
        public const string CLP = "clp"; // Chile
        public const string CNY = "cny"; // China
        public const string COP = "cop"; // Colombia
        public const string COU = "cou"; // Colombia (Real Value Unit)
        public const string CRC = "crc"; // Costa Rica
        public const string CUC = "cuc"; // Cuba (convertible peso - historical)
        public const string CUP = "cup"; // Cuba
        public const string CVE = "cve"; // Cabo Verde
        public const string CZK = "czk"; // Czech Republic
        public const string DJF = "djf"; // Djibouti
        public const string DKK = "dkk"; // Denmark
        public const string DOP = "dop"; // Dominican Republic
        public const string DZD = "dzd"; // Algeria
        public const string EGP = "egp"; // Egypt
        public const string ERN = "ern"; // Eritrea
        public const string ETB = "etb"; // Ethiopia
        public const string EUR = "eur"; // Euro (Eurozone)
        public const string FJD = "fjd"; // Fiji
        public const string FKP = "fkp"; // Falkland Islands (Malvinas)
        public const string GBP = "gbp"; // United Kingdom
        public const string GEL = "gel"; // Georgia
        public const string GGP = "ggp"; // Guernsey
        public const string GHS = "ghs"; // Ghana
        public const string GIP = "gip"; // Gibraltar
        public const string GMD = "gmd"; // Gambia
        public const string GNF = "gnf"; // Guinea
        public const string GTQ = "gtq"; // Guatemala
        public const string GYD = "gyd"; // Guyana
        public const string HKD = "hkd"; // Hong Kong
        public const string HNL = "hnl"; // Honduras
        public const string HRK = "hrk"; // Croatia
        public const string HTG = "htg"; // Haiti
        public const string HUF = "huf"; // Hungary
        public const string IDR = "idr"; // Indonesia
        public const string ILS = "ils"; // Israel
        public const string IMP = "imp"; // Isle of Man
        public const string INR = "inr"; // India
        public const string IQD = "iqd"; // Iraq
        public const string IRR = "irr"; // Iran
        public const string ISK = "isk"; // Iceland
        public const string JEP = "jep"; // Jersey
        public const string JMD = "jmd"; // Jamaica
        public const string JOD = "jod"; // Jordan
        public const string JPY = "jpy"; // Japan
        public const string KES = "kes"; // Kenya
        public const string KGS = "kgs"; // Kyrgyzstan
        public const string KHR = "khr"; // Cambodia
        public const string KMF = "kmf"; // Comoros
        public const string KPW = "kpw"; // North Korea (DPRK)
        public const string KRW = "krw"; // South Korea (Republic of Korea)
        public const string KWD = "kwd"; // Kuwait
        public const string KYD = "kyd"; // Cayman Islands
        public const string KZT = "kzt"; // Kazakhstan
        public const string LAK = "lak"; // Lao People's Democratic Republic
        public const string LBP = "lbp"; // Lebanon
        public const string LKR = "lkr"; // Sri Lanka
        public const string LRD = "lrd"; // Liberia
        public const string LSL = "lsl"; // Lesotho
        public const string LYD = "lyd"; // Libya
        public const string MAD = "mad"; // Morocco
        public const string MDL = "mdl"; // Moldova
        public const string MGA = "mga"; // Madagascar
        public const string MKD = "mkd"; // North Macedonia
        public const string MMK = "mmk"; // Myanmar
        public const string MNT = "mnt"; // Mongolia
        public const string MOP = "mop"; // Macau
        public const string MRU = "mru"; // Mauritania
        public const string MUR = "mur"; // Mauritius
        public const string MVR = "mvr"; // Maldives
        public const string MWK = "mwk"; // Malawi
        public const string MXN = "mxn"; // Mexico
        public const string MXV = "mxv"; // Mexico (Mexican investment unit)
        public const string MYR = "myr"; // Malaysia
        public const string MZN = "mzn"; // Mozambique
        public const string NAD = "nad"; // Namibia
        public const string NGN = "ngn"; // Nigeria
        public const string NIO = "nio"; // Nicaragua
        public const string NOK = "nok"; // Norway
        public const string NPR = "npr"; // Nepal
        public const string NZD = "nzd"; // New Zealand
        public const string OMR = "omr"; // Oman
        public const string PAB = "pab"; // Panama
        public const string PEN = "pen"; // Peru
        public const string PGK = "pgk"; // Papua New Guinea
        public const string PHP = "php"; // Philippines
        public const string PKR = "pkr"; // Pakistan
        public const string PLN = "pln"; // Poland
        public const string PYG = "pyg"; // Paraguay
        public const string QAR = "qar"; // Qatar
        public const string RON = "ron"; // Romania
        public const string RSD = "rsd"; // Serbia
        public const string RUB = "rub"; // Russia
        public const string RWF = "rwf"; // Rwanda
        public const string SAR = "sar"; // Saudi Arabia
        public const string SBD = "sbd"; // Solomon Islands
        public const string SCR = "scr"; // Seychelles
        public const string SDG = "sdg"; // Sudan
        public const string SEK = "sek"; // Sweden
        public const string SGD = "sgd"; // Singapore
        public const string SHP = "shp"; // Saint Helena, Ascension and Tristan da Cunha
        public const string SLL = "sll"; // Sierra Leone
        public const string SOS = "sos"; // Somalia
        public const string SRD = "srd"; // Suriname
        public const string SSP = "ssp"; // South Sudan
        public const string STN = "stn"; // São Tomé and Príncipe
        public const string SVC = "svc"; // El Salvador
        public const string SYP = "syp"; // Syrian Arab Republic
        public const string SZL = "szl"; // Eswatini (Swaziland)
        public const string THB = "thb"; // Thailand
        public const string TJS = "tjs"; // Tajikistan
        public const string TMT = "tmt"; // Turkmenistan
        public const string TND = "tnd"; // Tunisia
        public const string TOP = "top"; // Tonga
        public const string TRY = "try"; // Turkey
        public const string TTD = "ttd"; // Trinidad and Tobago
        public const string TWD = "twd"; // Taiwan
        public const string TZS = "tzs"; // Tanzania
        public const string UAH = "uah"; // Ukraine
        public const string UGX = "ugx"; // Uganda
        public const string USD = "usd"; // United States of America
        public const string USN = "usn"; // United States (Next day) - US Dollar (next day)
        public const string UYI = "uyi"; // Uruguay (indexed units)
        public const string UYU = "uyu"; // Uruguay
        public const string UYW = "uyw"; // Uruguay (Unidad previsional)
        public const string UZS = "uzs"; // Uzbekistan
        public const string VES = "ves"; // Venezuela
        public const string VND = "vnd"; // Vietnam
        public const string VUV = "vuv"; // Vanuatu
        public const string WST = "wst"; // Samoa
        public const string XAF = "xaf"; // Central African CFA franc (Cameroon, CAR, Chad, Congo, Equatorial Guinea, Gabon)
        public const string XAG = "xag"; // Silver (one troy ounce)
        public const string XAU = "xau"; // Gold (one troy ounce)
        public const string XBA = "xba"; // European Composite Unit (Bond markets)
        public const string XBB = "xbb"; // European Monetary Unit (Bond markets)
        public const string XBC = "xbc"; // European Unit of Account (XBC)
        public const string XBD = "xbd"; // European Unit of Account (XBD)
        public const string XCD = "xcd"; // East Caribbean dollar (Antigua and Barbuda, Dominica, Grenada, Saint Lucia, etc.)
        public const string XDR = "xdr"; // IMF Special Drawing Rights
        public const string XOF = "xof"; // West African CFA franc (Benin, Burkina Faso, Guinea-Bissau, Ivory Coast, Mali, Niger, Senegal, Togo)
        public const string XPD = "xpd"; // Palladium (one troy ounce)
        public const string XPF = "xpf"; // CFP franc (French Polynesia, New Caledonia, Wallis & Futuna)
        public const string XPT = "xpt"; // Platinum (one troy ounce)
        public const string XSU = "xsu"; // SUCRE (regional, supranational)
        public const string XTS = "xts"; // Testing code (reserved)
        public const string XUA = "xua"; // ADB Unit of Account
        public const string XXX = "xxx"; // No currency / unknown
        public const string YER = "yer"; // Yemen
        public const string ZAR = "zar"; // South Africa
        public const string ZMW = "zmw"; // Zambia
        public const string ZWL = "zwl"; // Zimbabwe
    }

    public interface ICurrencyConverter
    {
        CurrencyConverterStatus Status { get; }

        void RegisterModule(ICurrencyConverterModule module);

        void Initialize();
        IEnumerator InitializeCoroutine();

        decimal? Convert(string from, string to, decimal amount);
    }
}