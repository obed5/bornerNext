using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Fiche sanitaire du voyageur (héritage : questionnaires, appliqués aux dossiers) :
/// sélection d'un dossier, application du questionnaire avec validité, saisie des
/// réponses (questions principales puis questions de suivi conditionnelles), message
/// de fin, contrôle de complétude et impression de la fiche vierge.
/// </summary>
public partial class HealthQuestionnaireWindow : Window
{
    private readonly QuestionnaireService _questionnaires;
    private readonly DocumentService _documents;
    private readonly ReportService _reports;
    private readonly IReportWriter _writer;

    private DocumentListItem? _document;
    private int? _preselectedDocumentId;
    private int _appliedId;
    private QuestionnaireOutcome? _outcome;
    private readonly List<QuestionEditor> _editors = new();

    public HealthQuestionnaireWindow(QuestionnaireService questionnaires, DocumentService documents,
        ReportService reports, IReportWriter writer)
    {
        _questionnaires = questionnaires;
        _documents = documents;
        _reports = reports;
        _writer = writer;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    /// <summary>Préselection d'un dossier (ouverture depuis le formulaire passeport).</summary>
    public void PreselectDocument(int documentId, string label)
    {
        _preselectedDocumentId = documentId;
        DocumentDetailText.Text = label;
        DocumentBox.Text = label;
    }

    private async Task LoadAllAsync() => await GuardAsync(async () =>
    {
        var questionnaires = await _questionnaires.GetQuestionnairesAsync();
        QuestionnaireCombo.ItemsSource = questionnaires;
        QuestionnaireCombo.SelectedIndex = questionnaires.Count > 0 ? 0 : -1;

        if (_preselectedDocumentId is { } id)
        {
            await LoadDossierAsync(id);
        }
        else
        {
            StatusText.Text = Strings.Get("HE_Status_Search");
        }
    });

    // -----------------------------------------------------------------------
    // Dossier
    // -----------------------------------------------------------------------

    private async void SearchDocument_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var results = await _documents.SearchAsync(number: DocumentBox.Text.Trim().Length > 0 ? DocumentBox.Text.Trim() : null);
        DocumentsGrid.ItemsSource = results.ToList();
        StatusText.Text = Strings.Format("HE_FoundDocuments", results.Count);
    });

    private async void Document_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = DocumentsGrid.SelectedItem as DocumentListItem;
        if (item is null) return;
        _document = item;
        await GuardAsync(() => LoadDossierAsync(item.Id));
    }

    private async Task LoadDossierAsync(int documentId)
    {
        _preselectedDocumentId = documentId;
        DocumentDetailText.Text = Strings.Format("HE_DossierSelected", documentId);
        await RefreshApplicationsAsync();
    }

    private async Task RefreshApplicationsAsync()
    {
        if (_preselectedDocumentId is not { } documentId)
        {
            ApplicationsGrid.ItemsSource = null;
            return;
        }

        var applications = await _questionnaires.GetApplicationsAsync(documentId: documentId);
        ApplicationsGrid.ItemsSource = applications.Select(a => new ApplicationVm(a)).ToList();
        StatusText.Text = Strings.Format("HE_ApplicationsFound", applications.Count);

        // Ouvre automatiquement la dernière application active, s'il y en a une.
        var active = applications.FirstOrDefault(a => !a.Expired);
        if (active is not null)
        {
            ApplicationsGrid.SelectedItem = (ApplicationsGrid.ItemsSource as IEnumerable<ApplicationVm>)
                ?.FirstOrDefault(v => v.Id == active.Id);
        }
        else
        {
            await ShowNoApplicationAsync();
        }
    }

    private async Task ShowNoApplicationAsync()
    {
        _appliedId = 0;
        _outcome = null;
        QuestionsPanel.Children.Clear();
        _editors.Clear();
        HeaderText.Text = Strings.Get("HE_NoApplication");
        ValidityText.Text = string.Empty;
        EndingTitleText.Text = string.Empty;
        EndingMessageText.Text = string.Empty;
        await Task.CompletedTask;
    }

    private async void Application_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = ApplicationsGrid.SelectedItem as ApplicationVm;
        if (vm is null) return;
        await GuardAsync(async () => await LoadOutcomeAsync(vm.Id));
    }

    // -----------------------------------------------------------------------
    // Application et réponses
    // -----------------------------------------------------------------------

    private async void Apply_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_preselectedDocumentId is not { } documentId)
        {
            MessageBox.Show(this, Strings.Get("HE_Status_Search"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (QuestionnaireCombo.SelectedItem is not QuestionnaireListItem questionnaire)
        {
            MessageBox.Show(this, Strings.Get("HE_Status_SelectQuestionnaire"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var appliedId = await _questionnaires.ApplyAsync(
            new QuestionnaireApplicationRequest(questionnaire.Id, documentId, null));
        await RefreshApplicationsAsync();
        await LoadOutcomeAsync(appliedId);
        StatusText.Text = Strings.Get("HE_Applied");
    });

    private async Task LoadOutcomeAsync(int appliedId)
    {
        _appliedId = appliedId;
        _outcome = await _questionnaires.GetOutcomeAsync(appliedId);
        BuildQuestionEditors(_outcome);
    }

    private void BuildQuestionEditors(QuestionnaireOutcome outcome)
    {
        QuestionsPanel.Children.Clear();
        _editors.Clear();

        HeaderText.Text = outcome.Questionnaire;
        ValidityText.Text = outcome.ExpiresAt is { } expiry
            ? Strings.Format(outcome.Expired ? "HE_ValidityExpired" : "HE_Validity", expiry.ToString("dd/MM/yyyy"))
            : Strings.Get("HE_ValidityNone");
        EndingTitleText.Text = outcome.EndingTitle ?? string.Empty;
        EndingMessageText.Text = outcome.EndingMessage ?? string.Empty;

        foreach (var question in outcome.Questions)
        {
            var editor = new QuestionEditor(question);
            _editors.Add(editor);
            QuestionsPanel.Children.Add(editor.Build());
        }
    }

    private async void SaveAnswers_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_appliedId == 0)
        {
            MessageBox.Show(this, Strings.Get("HE_NoApplication"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Seules les questions renseignées sont enregistrées (les autres restent vides).
        foreach (var editor in _editors)
        {
            var (answerId, other) = editor.Read();
            if (answerId is null && string.IsNullOrWhiteSpace(other)) continue;
            await _questionnaires.AnswerAsync(_appliedId, editor.Question.QuestionId, answerId, other);
        }

        // Un suivi peut devenir visible : l'écran est reconstruit depuis l'état réel.
        _outcome = await _questionnaires.GetOutcomeAsync(_appliedId);
        BuildQuestionEditors(_outcome);
        StatusText.Text = Strings.Get("HE_AnswersSaved");
    });

    private async void Validate_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_appliedId == 0)
        {
            MessageBox.Show(this, Strings.Get("HE_NoApplication"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _questionnaires.ValidateAsync(_appliedId);
        _outcome = await _questionnaires.GetOutcomeAsync(_appliedId);
        BuildQuestionEditors(_outcome);
        MessageBox.Show(this,
            _outcome.EndingMessage is null
                ? Strings.Get("HE_Validated")
                : $"{_outcome.EndingTitle}\n\n{_outcome.EndingMessage}",
            Strings.Get("HE_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    });

    // -----------------------------------------------------------------------
    // Impression de la fiche vierge
    // -----------------------------------------------------------------------

    private async void PrintBlank_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var template = await _reports.BuildAsync(ReportCatalog.BlankQuestionnaire, new ReportParameters());
        var localized = ReportLocalizer.Localize(template, Strings.Get);
        var path = Path.Combine(Path.GetTempPath(), $"BorderNext_FicheSanitaire_{DateTime.Now:HHmmss}.pdf");
        await File.WriteAllBytesAsync(path, _writer.RenderPdf(localized));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        StatusText.Text = Strings.Format("RP_PrintStarted", path);
    });

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

    /// <summary>Éditeur d'une question selon son type : oui/non et choix multiple par liste,
    /// texte, date et numérique par saisie libre (le détail va dans la précision).</summary>
    private sealed class QuestionEditor
    {
        private readonly ComboBox? _combo;
        private readonly TextBox? _text;
        private readonly DatePicker? _date;
        private readonly TextBox? _otherBox;

        public QuestionEditor(OpenQuestion question)
        {
            Question = question;
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var title = new TextBlock
            {
                Text = question.IsFollowUp ? $"↳ {question.Title}" : question.Title,
                FontWeight = question.IsFollowUp ? FontWeights.Normal : FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(title);

            switch (question.TypeCode)
            {
                case QuestionTypeCodes.Date:
                    _date = new DatePicker { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                    if (DateTime.TryParse(ReadOther(question), out var existingDate)) _date.SelectedDate = existingDate;
                    panel.Children.Add(_date);
                    break;

                case QuestionTypeCodes.Text:
                case QuestionTypeCodes.Numerical:
                    _text = new TextBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Text = ReadOther(question) };
                    panel.Children.Add(_text);
                    break;

                default:
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    _otherBox = new TextBox
                    {
                        Width = 200, Margin = new Thickness(8, 0, 0, 0),
                        Visibility = Visibility.Collapsed,
                        Text = question.OtherAnswer ?? string.Empty,
                    };
                    _combo = new ComboBox { Width = 260, ItemsSource = question.Answers };
                    _combo.SelectionChanged += (_, _) =>
                    {
                        // Précision « Autres » : saisie libre affichée à la sélection.
                        var selected = (_combo.SelectedItem as AnswerItem)?.Answer;
                        _otherBox!.Visibility = selected?.Contains("Autre", StringComparison.OrdinalIgnoreCase) == true
                            ? Visibility.Visible : Visibility.Collapsed;
                    };
                    _combo.SelectedItem = question.Answers.FirstOrDefault(a => a.Id == question.SelectedAnswerId);
                    if (question.Answers.FirstOrDefault(a => a.Id == question.SelectedAnswerId)?.Answer
                            .Contains("Autre", StringComparison.OrdinalIgnoreCase) == true)
                        _otherBox.Visibility = Visibility.Visible;
                    row.Children.Add(_combo);
                    row.Children.Add(_otherBox);
                    panel.Children.Add(row);
                    break;
            }

            Element = panel;
        }

        public OpenQuestion Question { get; }
        public StackPanel Element { get; }

        private static string ReadOther(OpenQuestion question) => question.OtherAnswer ?? string.Empty;

        public StackPanel Build() => Element;

        /// <summary>Réponse saisie : identifiant de réponse (listes) ou réponse technique (saisie libre).</summary>
        public (int? AnswerId, string? Other) Read()
        {
            if (_combo is not null)
            {
                var answerId = (_combo.SelectedItem as AnswerItem)?.Id;
                var other = _otherBox?.Visibility == Visibility.Visible ? _otherBox.Text : null;
                return (answerId, other);
            }

            if (_date is not null)
            {
                return _date.SelectedDate is { } date
                    ? (Question.Answers.FirstOrDefault()?.Id, date.ToString("dd/MM/yyyy"))
                    : (null, null);
            }

            if (_text is not null)
            {
                var text = _text.Text?.Trim();
                return string.IsNullOrWhiteSpace(text)
                    ? (null, null)
                    : (Question.Answers.FirstOrDefault()?.Id, text);
            }

            return (null, null);
        }
    }

    internal sealed class ApplicationVm
    {
        public ApplicationVm(AppliedQuestionnaireSummary summary)
        {
            Id = summary.Id;
            Questionnaire = summary.Questionnaire;
            AppliedFr = summary.AppliedDate.ToString("dd/MM/yyyy");
            StateFr = Strings.Get(summary.Expired ? "HE_Expired" : "HE_Active");
        }

        public int Id { get; }
        public string Questionnaire { get; }
        public string AppliedFr { get; }
        public string StateFr { get; }
    }
}
