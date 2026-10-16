using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.UI.Resources;
using Microsoft.Win32;

namespace BorderNext.UI;

/// <summary>
/// Échanges frontaliers (héritage : écrans de messages APIS, ImportExportManager et
/// contrôles des systèmes partenaires) : import et export des listes de passagers
/// EDIFACT PAXLST, consultation des messages reçus et de leurs passagers, file des
/// passagers douteux avec décision du superviseur, contrôles externes (SIMIEC,
/// Belize, système aérien), journal des échanges et configuration des connecteurs.
/// </summary>
public partial class ExchangesWindow : Window
{
    private readonly ApisMessageService _apis;
    private readonly ExternalCheckService _checks;
    private readonly ExchangeJournal _journal;
    private readonly SettingsService _settings;
    private readonly ReferenceDataService _referenceData;
    private readonly FileRetentionService _retention;

    private bool _loadingAutoCheck;
    private ApisDoubtfulPerson? _doubt;
    private ExchangeTask? _connector;

    private List<IdVm> _statusItems = new();
    private List<IdVm> _actionItems = new();
    private readonly List<OutcomeVm> _checkResults = new();

    public ExchangesWindow(ApisMessageService apis, ExternalCheckService checks, ExchangeJournal journal,
        SettingsService settings, ReferenceDataService referenceData, FileRetentionService retention)
    {
        _apis = apis;
        _checks = checks;
        _journal = journal;
        _settings = settings;
        _referenceData = referenceData;
        _retention = retention;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync() => await GuardAsync(async () =>
    {
        _statusItems = new List<IdVm>
        {
            new(null, "—"),
            new(ApisMessageStatus.Received, Strings.Get("EX_MsgStatus_0")),
            new(ApisMessageStatus.Reconciled, Strings.Get("EX_MsgStatus_1")),
            new(ApisMessageStatus.Alerted, Strings.Get("EX_MsgStatus_2")),
            new(ApisMessageStatus.Exported, Strings.Get("EX_MsgStatus_3")),
        };
        MessageStatusCombo.ItemsSource = _statusItems;
        MessageStatusCombo.SelectedIndex = 0;

        _actionItems = (await _referenceData.GetItemsAsync("ACTION"))
            .Select(i => new IdVm(i.Id, $"{i.Code} — {i.Fr}")).ToList();
        var decisions = new List<object> { new IdVm(null, "—") };
        decisions.AddRange(_actionItems);
        DecisionActionCombo.ItemsSource = decisions;
        DecisionActionCombo.SelectedIndex = 0;

        JournalChannelCombo.ItemsSource = new List<IdVm>
        {
            new(null, "—"),
            new(0, ExchangeChannels.Edifact),
            new(1, ExchangeChannels.Apis),
            new(2, ExchangeChannels.Simiec),
            new(3, ExchangeChannels.Belize),
            new(4, ExchangeChannels.Air),
        };
        JournalChannelCombo.SelectedIndex = 0;

        JournalDirectionCombo.ItemsSource = new List<IdVm>
        {
            new(null, "—"),
            new(0, Strings.Get("EX_Direction_IMPORT")),
            new(1, Strings.Get("EX_Direction_EXPORT")),
            new(2, Strings.Get("EX_Direction_CHECK")),
        };
        JournalDirectionCombo.SelectedIndex = 0;

        ModeCombo.ItemsSource = new List<IdVm>
        {
            new(0, Strings.Get("EX_Mode_Mock")),
            new(1, Strings.Get("EX_Mode_Live")),
        };

        var autoCheck = await _settings.GetIntAsync("CheckInDoubtablePersons");
        _loadingAutoCheck = true;
        AutoCheckBox.IsChecked = autoCheck == 1;
        _loadingAutoCheck = false;

        // Affichage de secours : les boutons des connecteurs suivent les définitions actives.
        await RefreshConnectorsAsync();
        await RefreshMessagesAsync();
        await RefreshDoubtfulAsync();
        await SearchJournalAsync();
    });

    // -----------------------------------------------------------------------
    // Messages APIS
    // -----------------------------------------------------------------------

    private async Task RefreshMessagesAsync()
    {
        var from = MessageFromPicker.SelectedDate;
        DateTime? to = MessageToPicker.SelectedDate is { } end ? end.AddDays(1) : null;
        var status = (MessageStatusCombo.SelectedItem as IdVm)?.Id;

        var messages = await _apis.SearchMessagesAsync(from, to, MessageFlightBox.Text, status);
        MessagesGrid.ItemsSource = messages.Select(m => new MessageVm(m)).ToList();
        MessageDetailText.Text = Strings.Format("EX_MessagesFound", messages.Count);
        PassengersGrid.ItemsSource = null;
    }

    private async void SearchMessages_Click(object sender, RoutedEventArgs e)
        => await GuardAsync(RefreshMessagesAsync);

    private async void Message_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MessagesGrid.SelectedItem is not MessageVm vm) return;
        await GuardAsync(async () =>
        {
            var passengers = await _apis.GetPassengersAsync(vm.Id);
            PassengersGrid.ItemsSource = passengers.Select(p => new PassengerVm(p)).ToList();
            var links = await _apis.GetMessageAsync(vm.Id);
            MessageDetailText.Text = Strings.Format("EX_MessageDetail", vm.FlightLabel,
                links?.Files.Count ?? 0, links?.Files.FirstOrDefault()?.FilePath ?? "—");
        });
    }

    private async void ImportPaxlst_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Get("EX_ImportDialog"),
            Filter = Strings.Get("EX_ImportFilter"),
        };
        if (dialog.ShowDialog(this) != true) return;

        var result = await _apis.ImportPaxlstFileAsync(dialog.FileName);
        await RefreshMessagesAsync();
        await RefreshDoubtfulAsync();

        var message = Strings.Format("EX_ImportDone", result.MessageCount, result.PassengerCount,
            result.CrewCount, result.Matched, result.Doubtful, result.Unmatched);
        if (result.Warnings.Count > 0)
            message += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
        MessageBox.Show(this, message, Strings.Get("EX_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        StatusText.Text = message.Replace(Environment.NewLine, " ");
    });

    private async void ExportPaxlst_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var ids = MessagesGrid.ItemsSource is IEnumerable<MessageVm> items
            ? items.Select(m => m.Id).ToList()
            : new List<int>();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, Strings.Get("EX_Error_NoMessageSelected"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = await _apis.ExportMessagesPaxlstAsync(ids);
        StatusText.Text = Strings.Format("EX_ExportDone", result.FilePath);
        MessageBox.Show(this, Strings.Format("EX_ExportDone", result.FilePath), Strings.Get("EX_Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void ExportXml_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var from = MessageFromPicker.SelectedDate ?? DateTime.Today.AddMonths(-1);
        var to = (MessageToPicker.SelectedDate ?? DateTime.Today).AddDays(1);

        var result = await _apis.ExportMessagesXmlAsync(from, to);
        await RefreshMessagesAsync();
        await SearchJournalAsync();
        StatusText.Text = Strings.Format("EX_ExportDone", result.FilePath);
        MessageBox.Show(this, Strings.Format("EX_ExportDone", result.FilePath), Strings.Get("EX_Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void GenerateFromMovements_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var from = MessageFromPicker.SelectedDate ?? DateTime.Today.AddMonths(-1);
        var to = (MessageToPicker.SelectedDate ?? DateTime.Today).AddDays(1);

        var result = await _apis.GeneratePaxlstFromMovementsAsync(from, to);
        await SearchJournalAsync();
        MessageBox.Show(this, Strings.Format("EX_GenerateDone", result.MessageCount, result.TravellerCount, result.FilePath),
            Strings.Get("EX_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    });

    // -----------------------------------------------------------------------
    // Passagers douteux
    // -----------------------------------------------------------------------

    private async Task RefreshDoubtfulAsync()
    {
        var pendingOnly = ShowProcessedCheck.IsChecked != true;
        var doubts = await _apis.GetDoubtfulAsync(pendingOnly);
        DoubtfulGrid.ItemsSource = doubts.Select(d => new DoubtfulVm(d)).ToList();
    }

    private async void RefreshDoubtful_Click(object sender, RoutedEventArgs e) => await GuardAsync(RefreshDoubtfulAsync);

    private async void DoubtfulFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await GuardAsync(RefreshDoubtfulAsync);
    }

    private async void AutoCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingAutoCheck || !IsLoaded) return;
        await GuardAsync(async () =>
        {
            await _settings.SetIntAsync("CheckInDoubtablePersons", AutoCheckBox.IsChecked == true ? 1 : 0);
            StatusText.Text = Strings.Get(AutoCheckBox.IsChecked == true ? "WL_AutoCheck_On" : "WL_AutoCheck_Off");
        });
    }

    private void Doubtful_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _doubt = (DoubtfulGrid.SelectedItem as DoubtfulVm)?.Entity;
        DecisionNoteBox.Text = _doubt?.SupervisorsNote ?? string.Empty;
    }

    private async void ProcessDoubtful_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_doubt is null)
        {
            MessageBox.Show(this, Strings.Get("EX_Error_SelectDoubt"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var action = (DecisionActionCombo.SelectedItem as IdVm)?.Id;
        await _apis.ProcessDoubtfulAsync(_doubt.Id, action, DecisionNoteBox.Text);
        await RefreshDoubtfulAsync();
        StatusText.Text = Strings.Get("EX_DoubtProcessed");
    });

    // -----------------------------------------------------------------------
    // Contrôles externes
    // -----------------------------------------------------------------------

    private ExternalCheckRequest BuildCheckRequest() => new(
        DocumentNumber: CheckDocumentBox.Text,
        DocumentType: CheckDocumentTypeBox.Text,
        Surname: CheckSurnameBox.Text,
        GivenName: CheckGivenNameBox.Text,
        DateOfBirth: CheckBirthPicker.SelectedDate,
        DocumentExpiry: CheckExpiryPicker.SelectedDate);

    private async Task RunChecksAsync(Func<Task<IReadOnlyList<ExternalCheckOutcome>>> run)
    {
        var outcomes = await run();
        // Historique de session : chaque contrôle s'ajoute en tête de la liste.
        foreach (var outcome in outcomes.Reverse())
            _checkResults.Insert(0, new OutcomeVm(outcome));

        CheckResultsGrid.ItemsSource = null;
        CheckResultsGrid.ItemsSource = _checkResults;
        await SearchJournalAsync();
        StatusText.Text = Strings.Format("EX_ChecksDone", outcomes.Count);
    }

    private async void CheckAll_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        await RunChecksAsync(async () =>
        {
            var request = BuildCheckRequest();
            var outcomes = new List<ExternalCheckOutcome>();
            foreach (var task in await _checks.GetConnectorsAsync())
            {
                var settings = ExternalCheckService.SettingsFrom(task);
                if (!settings.IsActive) continue;
                outcomes.Add(await _checks.CheckAsync(task.Channel, request));
            }

            return outcomes;
        });
    });

    private async void CheckSimiec_Click(object sender, RoutedEventArgs e)
        => await GuardAsync(() => RunChecksAsync(() => SingleCheckAsync(ExchangeChannels.Simiec)));

    private async void CheckBelize_Click(object sender, RoutedEventArgs e)
        => await GuardAsync(() => RunChecksAsync(() => SingleCheckAsync(ExchangeChannels.Belize)));

    private async void CheckAir_Click(object sender, RoutedEventArgs e)
        => await GuardAsync(() => RunChecksAsync(() => SingleCheckAsync(ExchangeChannels.Air)));

    private async Task<IReadOnlyList<ExternalCheckOutcome>> SingleCheckAsync(string channel)
        => new[] { await _checks.CheckAsync(channel, BuildCheckRequest()) };

    // -----------------------------------------------------------------------
    // Journal
    // -----------------------------------------------------------------------

    private async Task SearchJournalAsync()
    {
        var channel = (JournalChannelCombo.SelectedItem as IdVm)?.Display;
        var direction = (JournalDirectionCombo.SelectedItem as IdVm)?.Id switch
        {
            0 => ExchangeDirections.Import,
            1 => ExchangeDirections.Export,
            2 => ExchangeDirections.Check,
            _ => null,
        };

        var from = JournalFromPicker.SelectedDate;
        DateTime? to = JournalToPicker.SelectedDate is { } end ? end.AddDays(1) : null;

        var runs = await _journal.SearchRunsAsync(
            channel == "—" ? null : channel, direction, from, to);
        RunsGrid.ItemsSource = runs.Select(r => new RunVm(r)).ToList();
        ErrorsGrid.ItemsSource = null;
    }

    private async void SearchJournal_Click(object sender, RoutedEventArgs e) => await GuardAsync(SearchJournalAsync);

    private async void Run_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RunsGrid.SelectedItem is not RunVm vm) return;
        await GuardAsync(async () =>
        {
            var errors = await _journal.GetErrorsAsync(vm.Id);
            ErrorsGrid.ItemsSource = errors.Select(x => new ErrorVm(x)).ToList();
        });
    }

    // -----------------------------------------------------------------------
    // Connecteurs
    // -----------------------------------------------------------------------

    private async Task RefreshConnectorsAsync()
    {
        var connectors = await _checks.GetConnectorsAsync();
        ConnectorsGrid.ItemsSource = connectors.Select(c => new ConnectorVm(c)).ToList();
        await RefreshRetentionAsync();
    }

    private void Connector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _connector = (ConnectorsGrid.SelectedItem as ConnectorVm)?.Entity;
        if (_connector is null) return;

        var settings = ExternalCheckService.SettingsFrom(_connector);
        EndpointBox.Text = _connector.Endpoint ?? string.Empty;
        ModeCombo.SelectedIndex = settings.IsMock ? 0 : 1;
        ActiveCheck.IsChecked = _connector.IsActive;
        SecretBox.Clear();
        _ = RefreshSecretStatusAsync();
    }

    private async void SaveConnector_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_connector is null)
        {
            MessageBox.Show(this, Strings.Get("EX_Error_SelectConnector"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = ModeCombo.SelectedIndex == 1 ? ConnectorSettings.ModeLive : ConnectorSettings.ModeMock;
        await _checks.SaveConnectorAsync(_connector.Id, EndpointBox.Text, mode, ActiveCheck.IsChecked == true);
        await RefreshConnectorsAsync();
        StatusText.Text = Strings.Get("EX_ConnectorSaved");
    });

    /// <summary>Enregistre le jeton du partenaire (chiffré par le protecteur du poste).</summary>
    private async void SaveSecret_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_connector is null)
        {
            MessageBox.Show(this, Strings.Get("EX_Error_SelectConnector"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(SecretBox.Password))
        {
            MessageBox.Show(this, Strings.Get("EX_Connector_SecretEmpty"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _settings.SetSecretAsync(ConnectorSettings.SecretSettingKey(_connector.Channel), SecretBox.Password);
        SecretBox.Clear();
        await RefreshSecretStatusAsync();
        StatusText.Text = Strings.Get("EX_Connector_SecretSaved");
    });

    private async void ClearSecret_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_connector is null) return;
        await _settings.SetSecretAsync(ConnectorSettings.SecretSettingKey(_connector.Channel), string.Empty);
        SecretBox.Clear();
        await RefreshSecretStatusAsync();
        StatusText.Text = Strings.Get("EX_Connector_SecretCleared");
    });

    private async void PurgePreview_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var reports = await _retention.PurgeAllAsync(dryRun: true);
        StatusText.Text = PurgeSummary(reports, preview: true);
    });

    private async void PurgeFiles_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var reports = await _retention.PurgeAllAsync();
        StatusText.Text = PurgeSummary(reports, preview: false);
    });

    private static string PurgeSummary(IReadOnlyDictionary<string, PurgeReport> reports, bool preview)
    {
        if (reports.Count == 0) return Strings.Get("EX_PurgeDisabled");
        var files = reports.Values.Sum(r => r.FileCount);
        var kilobytes = reports.Values.Sum(r => r.BytesFreed) / 1024;
        return Strings.Format(preview ? "EX_PurgePreviewResult" : "EX_PurgeResult", files, kilobytes);
    }

    /// <summary>État du jeton : « défini » ou « non défini » — la valeur n'est jamais réaffichée.</summary>
    private async Task RefreshSecretStatusAsync()
    {
        if (_connector is null)
        {
            SecretStatusText.Text = string.Empty;
            return;
        }

        var hasSecret = await _settings.HasSecretAsync(ConnectorSettings.SecretSettingKey(_connector.Channel));
        SecretStatusText.Text = Strings.Get(hasSecret ? "EX_Connector_SecretSet" : "EX_Connector_SecretUnset");
    }

    private async Task RefreshRetentionAsync()
    {
        var days = await _retention.GetRetentionDaysAsync();
        RetentionText.Text = Strings.Get(days == 0 ? "EX_PurgeUnlimited" : "EX_PurgeRetention");
    }

    // -----------------------------------------------------------------------
    // Infrastructure d'écran
    // -----------------------------------------------------------------------

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DomainValidationException ex)
        {
            MessageBox.Show(this, Strings.Get(ex.ErrorKey), Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record IdVm(int? Id, string Display) { public override string ToString() => Display; }

    internal sealed class MessageVm
    {
        private readonly ApisMessage _message;

        public MessageVm(ApisMessage message) => _message = message;

        public int Id => _message.Id;
        public string FlightLabel => _message.FlightLabel;
        public string? DepartureLocation => _message.DepartureLocation;
        public string? ArrivalLocation => _message.ArrivalLocation;
        public int PassengerCount => _message.PassengerCount;
        public string? SenderId => _message.SenderId;
        public int FileCount => _message.Files.Count;
        public string CreatedDateFr => _message.CreatedDate.ToString("dd/MM/yyyy HH:mm");
        public string ArrivalDateFr => _message.ArrivalDate?.ToString("dd/MM/yyyy HH:mm") ?? "—";
        public string StatusFr => Strings.Get($"EX_MsgStatus_{_message.Status}");
    }

    internal sealed class PassengerVm
    {
        private readonly ApisPassenger _passenger;

        public PassengerVm(ApisPassenger passenger) => _passenger = passenger;

        public string TypeFr => Strings.Get(_passenger.IsPassenger ? "EX_Type_Pax" : "EX_Type_Crew");
        public string? Surname => _passenger.Surname;
        public string? GivenName => _passenger.GivenName;
        public string? DocumentNumber => _passenger.DocumentNumber;
        public string BirthDateFr => _passenger.DateOfBirth?.ToString("dd/MM/yyyy") ?? "—";
        public string? Citizenship => _passenger.Citizenship;
        public string? Gender => _passenger.Gender;
        public string? CheckingReason => _passenger.CheckingReason;
        public string StatusFr => Strings.Get($"EX_PaxStatus_{_passenger.Status}");

        public string MatchedFr => _passenger.MatchedDocumentId is { } documentId
            ? Strings.Format("EX_MatchedDocument", documentId,
                _passenger.MatchedMovementId is { } movementId ? movementId.ToString() : "—")
            : Strings.Get("EX_NotMatched");
    }

    internal sealed class DoubtfulVm
    {
        public DoubtfulVm(ApisDoubtfulPerson entity) => Entity = entity;

        public ApisDoubtfulPerson Entity { get; }
        public string PassengerName => Entity.ApisPassenger?.FullName ?? "—";
        public string? DocumentNumber => Entity.ApisPassenger?.DocumentNumber;
        public string FlightLabel => Entity.ApisPassenger?.ApisMessage?.FlightLabel ?? "—";
        public string? ReasonFr => Entity.CheckingReason;
        public string? Supervisor => Entity.Supervisor;
        public string? SupervisorsNote => Entity.SupervisorsNote;
        public string CreateDateFr => Entity.CreateDate.ToString("dd/MM/yyyy HH:mm");
        public string StatusFr => Strings.Get(Entity.ActionStatus == 0 ? "EX_DoubtPending" : "EX_DoubtProcessed");
    }

    internal sealed class OutcomeVm
    {
        public OutcomeVm(ExternalCheckOutcome outcome)
        {
            ConnectorName = outcome.ConnectorName;
            VerdictFr = Strings.Get($"EX_Verdict_{outcome.Verdict}");
            Reference = outcome.Reference;
            Summary = outcome.Summary;
            ModeFr = Strings.Get(outcome.IsLive ? "EX_Mode_LiveShort" : "EX_Mode_MockShort");
            TimeFr = $"{outcome.DurationMs} ms";
        }

        public string ConnectorName { get; }
        public string VerdictFr { get; }
        public string? Reference { get; }
        public string Summary { get; }
        public string ModeFr { get; }
        public string TimeFr { get; }
    }

    internal sealed class RunVm
    {
        public RunVm(ExchangeRun run)
        {
            Id = run.Id;
            Reference = run.Reference;
            Summary = run.Summary;
            StartedFr = run.StartedAt.ToString("dd/MM/yyyy HH:mm:ss");
            ChannelFr = run.Channel;
            DirectionFr = Strings.Get($"EX_Direction_{run.Direction}");
            StatusFr = Strings.Get($"EX_RunStatus_{run.Status}");
            CountsFr = $"{run.SuccessCount}/{run.ItemCount} (+{run.ErrorCount})";
        }

        public int Id { get; }
        public string? Reference { get; }
        public string? Summary { get; }
        public string StartedFr { get; }
        public string ChannelFr { get; }
        public string DirectionFr { get; }
        public string StatusFr { get; }
        public string CountsFr { get; }
    }

    internal sealed class ErrorVm
    {
        public ErrorVm(ExchangeError error)
        {
            Step = error.Step;
            ItemReference = error.ItemReference;
            Message = error.Message;
            OccurredFr = error.OccurredAt.ToString("HH:mm:ss");
        }

        public string? Step { get; }
        public string? ItemReference { get; }
        public string? Message { get; }
        public string OccurredFr { get; }
    }

    internal sealed class ConnectorVm
    {
        public ConnectorVm(ExchangeTask entity)
        {
            Entity = entity;
            Channel = entity.Channel;
            Name = entity.Name;
            Endpoint = entity.Endpoint ?? "—";
            ModeFr = ExternalCheckService.SettingsFrom(entity).IsMock
                ? Strings.Get("EX_Mode_Mock")
                : Strings.Get("EX_Mode_Live");
        }

        public ExchangeTask Entity { get; }
        public string Channel { get; }
        public string Name { get; }
        public string Endpoint { get; }
        public string ModeFr { get; }
        public bool IsActive => Entity.IsActive;
    }
}
