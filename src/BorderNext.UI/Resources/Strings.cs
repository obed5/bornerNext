using System.Globalization;
using System.Resources;

namespace BorderNext.UI.Resources;

/// <summary>
/// Accès aux chaînes localisées. La culture invariante (Strings.resx) contient le
/// français (langue de déploiement principale) ; les cultures satellites (.en, .ar, …)
/// surchargent, avec repli automatique sur le français.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Rm = new("BorderNext.UI.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentUICulture, Get(key), args);

    // Propriétés typées (utilisables en XAML via x:Static).
    public static string App_Title => Get(nameof(App_Title));
    public static string Login_Title => Get(nameof(Login_Title));
    public static string Login_Subtitle => Get(nameof(Login_Subtitle));
    public static string Login_UserName => Get(nameof(Login_UserName));
    public static string Login_Password => Get(nameof(Login_Password));
    public static string Login_Button => Get(nameof(Login_Button));
    public static string Login_Language => Get(nameof(Login_Language));
    public static string ChangePassword_Title => Get(nameof(ChangePassword_Title));
    public static string ChangePassword_Info => Get(nameof(ChangePassword_Info));
    public static string ChangePassword_Current => Get(nameof(ChangePassword_Current));
    public static string ChangePassword_New => Get(nameof(ChangePassword_New));
    public static string ChangePassword_Confirm => Get(nameof(ChangePassword_Confirm));
    public static string ChangePassword_Button => Get(nameof(ChangePassword_Button));
    public static string Shell_Title => Get(nameof(Shell_Title));
    public static string Shell_Menu_File => Get(nameof(Shell_Menu_File));
    public static string Shell_Menu_Logout => Get(nameof(Shell_Menu_Logout));
    public static string Shell_Menu_Exit => Get(nameof(Shell_Menu_Exit));
    public static string Shell_Menu_Modules => Get(nameof(Shell_Menu_Modules));
    public static string Shell_Menu_Passport => Get(nameof(Shell_Menu_Passport));
    public static string Shell_Menu_Visa => Get(nameof(Shell_Menu_Visa));
    public static string Shell_Menu_Permit => Get(nameof(Shell_Menu_Permit));
    public static string Shell_Menu_BorderOps => Get(nameof(Shell_Menu_BorderOps));
    public static string Shell_Menu_Watchlist => Get(nameof(Shell_Menu_Watchlist));
    public static string Shell_Menu_Reports => Get(nameof(Shell_Menu_Reports));
    public static string Shell_Menu_Admin => Get(nameof(Shell_Menu_Admin));
    public static string Common_Error => Get(nameof(Common_Error));
}
