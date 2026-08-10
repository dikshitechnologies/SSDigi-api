namespace CHITSCHEME.Global
{
    /// <summary>
    /// Central place for all scheduled push notification templates.
    /// Trigger codes map to the use-case table.
    /// </summary>
    public static class NotificationTemplates
    {
        // ── Trigger codes ────────────────────────────────────────────
        public const string TRIGGER_FIRST_LOGIN           = "FIRST_LOGIN";
        public const string TRIGGER_FIRST_LOGIN_NO_SCHEME = "FIRST_LOGIN_NO_SCHEME";
        public const string TRIGGER_LOGIN_NO_SCHEME       = "LOGIN_NO_SCHEME";
        public const string TRIGGER_SCHEME_NOT_REGISTERED = "SCHEME_NOT_REGISTERED";
        public const string TRIGGER_INACTIVE_7_DAYS       = "INACTIVE_7_DAYS";

        // ── Templates ────────────────────────────────────────────────
        public static (string Title, string Body) Get(string trigger) => trigger switch
        {
            TRIGGER_FIRST_LOGIN => (
                "Welcome 🎉",
                "Welcome! Explore our latest collections, daily rates, and exclusive savings schemes."),

            TRIGGER_FIRST_LOGIN_NO_SCHEME => (
                "Start Saving",
                "Start your Gold or Silver savings today and enjoy exclusive maturity benefits."),

            TRIGGER_LOGIN_NO_SCHEME => (
                "Don't Miss Out",
                "You explored our app today. Join a savings scheme and begin your jewellery journey!"),

            TRIGGER_SCHEME_NOT_REGISTERED => (
                "Monthly Savings",
                "Save a little every month and own your dream jewellery sooner. Join now!"),

            TRIGGER_INACTIVE_7_DAYS => (
                "We Miss You",
                "It's been a while! Check out our latest collections, offers, and gold rates."),

            _ => ("SSDigi", "Check out our latest offers!")
        };
    }
}
