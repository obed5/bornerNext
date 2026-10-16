using System.Windows;
using System.Windows.Threading;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace BorderNext.UI;

public partial class MainWindow : Window
{
    private readonly AuthService _auth;
    private readonly ICurrentUser _currentUser;
    private readonly SessionScopes _scopes;
    private readonly DashboardService _dashboard;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _dashboardTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    public MainWindow(AuthService auth, ICurrentUser currentUser, SessionScopes scopes, DashboardService dashboard)
    {
        _auth = auth;
        _currentUser = currentUser;
        _scopes = scopes;
        _dashboard = dashboard;
        InitializeComponent();

        UserStatus.Text = Strings.Format("Shell_Status_User", _currentUser.UserName ?? "-");
        RoleStatus.Text = Strings.Format("Shell_Status_Role", _currentUser.RoleCode ?? "-");
        PcStatus.Text = Strings.Format("Shell_Status_PC", _currentUser.PcNumber ?? _currentUser.MachineName);

        _clockTimer.Tick += (_, _) => ClockStatus.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        _clockTimer.Start();
        ClockStatus.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        // Tableau de bord : chargé à l'ouverture puis rafraîchi toutes les minutes
        // (héritage : tableau de bord du poste, Actualiser manuel).
        Loaded += async (_, _) => await RefreshDashboardAsync();
        _dashboardTimer.Tick += async (_, _) => await RefreshDashboardAsync();
        _dashboardTimer.Start();
    }

    private async void RefreshDashboard_Click(object sender, RoutedEventArgs e) => await RefreshDashboardAsync();

    private async Task RefreshDashboardAsync()
    {
        try
        {
            var summary = await _dashboard.GetSummaryAsync();

            DbDateText.Text = summary.Date.ToString("dddd dd MMMM yyyy");
            DbStationText.Text = string.IsNullOrEmpty(summary.StationName)
                ? Strings.Get("DB_NoStation")
                : Strings.Format("DB_Station", summary.StationName);

            DbTransactionsValue.Text = summary.TransactionsToday.ToString();
            DbTransactionsDetail.Text = Strings.Format("DB_EntriesExits", summary.EntriesToday, summary.ExitsToday);

            DbYearAgoValue.Text = summary.TransactionsYearAgo.ToString();
            DbYearAgoDetail.Text = Strings.Format("DB_ComparisonDetail", summary.TransactionsTwoYearsAgo, summary.DeltaYearAgo);

            DbDoubtfulValue.Text = summary.DoubtfulPending.ToString();
            DbDoubtfulDetail.Text = Strings.Format("DB_DoubtfulDetail", summary.DoubtfulResolvedToday);

            DbTripsValue.Text = summary.OpenTrips.ToString();
            DbTripsDetail.Text = Strings.Format("DB_TripsDetail", summary.FlightsToday);

            DbInspectorsValue.Text = summary.ActiveInspectors.ToString();
            DbInspectorsDetail.Text = Strings.Format("DB_InspectorsDetail", summary.ArrivalInspectors, summary.ExitInspectors);

            DbRefusalsValue.Text = summary.RefusalsToday.ToString();
            DbRefusalsDetail.Text = Strings.Format("DB_RefusalsDetail", summary.PenaltiesToday);

            BorderPointGrid.ItemsSource = summary.PerBorderPoint;
            NationalityList.ItemsSource = summary.TopNationalities
                .Select(n => $"{n.NameFr} ({n.Alpha3}) — {n.Count}")
                .ToList();
        }
        catch (Exception)
        {
            // Le tableau de bord ne doit jamais gêner l'exploitation du poste.
        }
    }

    private void Module_Click(object sender, RoutedEventArgs e)
    {
        var module = (sender as System.Windows.Controls.MenuItem)?.Tag as string ?? "?";

        switch (module)
        {
            case "Passport":
            {
                var passport = _scopes.Current.GetRequiredService<PassportWindow>();
                passport.Owner = this;
                passport.Show();
                return;
            }
            case "Visa":
            {
                var visa = _scopes.Current.GetRequiredService<VisaWindow>();
                visa.Owner = this;
                visa.Show();
                return;
            }
            case "Permit":
            {
                var permit = _scopes.Current.GetRequiredService<PermitWindow>();
                permit.Owner = this;
                permit.Show();
                return;
            }
            case "Enforcement":
            {
                var enforcement = _scopes.Current.GetRequiredService<EnforcementWindow>();
                enforcement.Owner = this;
                enforcement.Show();
                return;
            }
            case "BorderOps":
            {
                var borderOps = _scopes.Current.GetRequiredService<BorderOpsWindow>();
                borderOps.Owner = this;
                borderOps.Show();
                return;
            }
            case "Watchlist":
            {
                var watchlist = _scopes.Current.GetRequiredService<WatchlistWindow>();
                watchlist.Owner = this;
                watchlist.Show();
                return;
            }
            case "Admin":
            {
                // Fenêtre d'administration résolue dans la portée de session (services scoped).
                var admin = _scopes.Current.GetRequiredService<AdminWindow>();
                admin.Owner = this;
                admin.ShowDialog();
                return;
            }
        }

        PlaceholderText.Text = Strings.Format("Shell_Module_Placeholder", module);
    }

    /// <summary>F5 : ouverture directe de l'enregistrement passeport (héritage).</summary>
    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F5 && _currentUser.IsAuthenticated)
        {
            Module_Click(new System.Windows.Controls.MenuItem { Tag = "Passport" }, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _auth.LogoutAsync();

        // Nouvelle session : la portée de services précédente (DbContext, services) est libérée.
        var login = _scopes.NewSession().GetRequiredService<LoginWindow>();
        System.Windows.Application.Current.MainWindow = login;
        login.Show();
        _clockTimer.Stop();
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();
}
