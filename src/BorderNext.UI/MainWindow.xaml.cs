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

            await RenderNationalityMapAsync();
        }
        catch (Exception)
        {
            // Le tableau de bord ne doit jamais gêner l'exploitation du poste.
        }
    }

    /// <summary>Trace le planisphère schématique des nationalités du jour : projection
    /// équirectangulaire, points dimensionnés selon le volume, libellés des principales.
    /// </summary>
    private async Task RenderNationalityMapAsync()
    {
        var points = await _dashboard.GetNationalityMapAsync();
        NationalityMap.Children.Clear();

        var width = NationalityMap.ActualWidth > 0 ? NationalityMap.ActualWidth : 380;
        var height = NationalityMap.ActualHeight > 0 ? NationalityMap.ActualHeight : 200;
        if (width < 40 || height < 40) return;

        // Graticule léger (méridiens tous les 30°, parallèles tous les 30°).
        var gridBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xDD, 0xE2, 0xE8));
        for (var lon = -180; lon <= 180; lon += 30)
        {
            var x = (lon + 180) / 360.0 * width;
            NationalityMap.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = height, Stroke = gridBrush, StrokeThickness = 0.5,
            });
        }
        for (var lat = -90; lat <= 90; lat += 30)
        {
            var y = (90 - lat) / 180.0 * height;
            NationalityMap.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 0.5,
            });
        }

        if (points.Count == 0)
        {
            NationalityMap.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = Strings.Get("DB_MapEmpty"),
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(8),
            });
            return;
        }

        var max = points.Max(p => p.Count);
        var color = System.Windows.Media.Color.FromRgb(0x2E, 0x75, 0xB6);
        foreach (var point in points)
        {
            var x = (double)(point.Longitude + 180) / 360.0 * width;
            var y = (90 - (double)point.Latitude) / 180.0 * height;
            var radius = 4 + 10 * Math.Sqrt((double)point.Count / max);

            var marker = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new System.Windows.Media.SolidColorBrush(color) { Opacity = 0.55 },
                Stroke = new System.Windows.Media.SolidColorBrush(color),
                StrokeThickness = 1,
                ToolTip = $"{point.Name} — {point.Count}",
            };
            System.Windows.Controls.Canvas.SetLeft(marker,
                Math.Max(0, Math.Min(x - radius, width - radius * 2)));
            System.Windows.Controls.Canvas.SetTop(marker,
                Math.Max(0, Math.Min(y - radius, height - radius * 2)));
            NationalityMap.Children.Add(marker);
        }

        // Libellé du pays le plus représenté de la carte.
        var top = points.First();
        var topX = (double)(top.Longitude + 180) / 360.0 * width;
        var topY = (90 - (double)top.Latitude) / 180.0 * height;
        var topRadius = 4 + 10 * Math.Sqrt((double)top.Count / max);

        // Les libellés de pays hérités sont longs : on raccourcit le nom pour que le
        // volume reste lisible, plutôt que de laisser le texte sortir du canevas.
        var name = top.Name;
        var text = $"{name} — {top.Count}";
        while (name.Length > 4 && EstimateLabelWidth(text) > width - 12)
        {
            name = name[..^1];
            text = $"{name}… — {top.Count}";
        }

        var label = new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1F, 0x4E, 0x79)),
        };
        var estimated = EstimateLabelWidth(text);

        // À droite du point, ou à gauche s'il déborderait ; le décalage tient compte du
        // rayon du point pour que le volume ne recouvre pas la fin du libellé.
        var left = topX + topRadius + 6;
        if (left + estimated > width - 4) left = Math.Max(4, topX - topRadius - estimated - 6);
        System.Windows.Controls.Canvas.SetLeft(label, left);
        System.Windows.Controls.Canvas.SetTop(label, Math.Max(topY - 8, 0));
        NationalityMap.Children.Add(label);
    }

    /// <summary>Largeur approchée d'un libellé de carte (mesure, avec marge de sécurité).</summary>
    private static double EstimateLabelWidth(string text)
    {
        try
        {
            var formatted = new System.Windows.Media.FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(
                    new System.Windows.Media.FontFamily("Segoe UI"),
                    System.Windows.FontStyles.Normal,
                    System.Windows.FontWeights.SemiBold,
                    System.Windows.FontStretches.Normal),
                11,
                System.Windows.Media.Brushes.Black,
                96);
            return formatted.Width * 1.06 + 12;
        }
        catch (Exception)
        {
            return text.Length * 7.4 + 40;
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
            case "Exchanges":
            {
                var exchanges = _scopes.Current.GetRequiredService<ExchangesWindow>();
                exchanges.Owner = this;
                exchanges.Show();
                return;
            }
            case "Reports":
            {
                var reports = _scopes.Current.GetRequiredService<ReportsWindow>();
                reports.Owner = this;
                reports.Show();
                return;
            }
            case "Health":
            {
                var health = _scopes.Current.GetRequiredService<HealthQuestionnaireWindow>();
                health.Owner = this;
                health.Show();
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
