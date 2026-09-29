using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>Paste a written-out week (e.g. from a chat with an AI) and turn it into assignments.</summary>
public class SandboxView : UserControl
{
    private readonly TextBox _input = Ui.Box(multiline: true);
    private readonly StackPanel _preview = new();
    private WeekPayload? _parsed;

    public SandboxView()
    {
        _input.FontFamily = new FontFamily("Consolas");
        _input.AcceptsTab = true;

        var grid = new Grid { Margin = new Thickness(32, 28, 32, 24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var header = new StackPanel();
        header.Children.Add(Ui.Text("Песочница", "H1"));
        header.Children.Add(Ui.Text("«#» — день (ПН, ВТ…), «##» — задание, «###» — упражнение (пропуски, вопрос, тезисы 13:23-14:44, произношение, монолог). " +
                                    "Поля: ссылка, время, миссия, описание, повтор (дней); в упражнении: текст, подсказка, образец. Разбор ничего не сохраняет — сначала посмотрите результат.", "Muted"));
        header.Children.Add(Ui.Wrap(
            Ui.Button("Разобрать", Parse, "PrimaryButton"),
            Ui.Button("Вставить пример", () => { _input.Text = WeekImporter.Example; Parse(); }),
            Ui.Button("Очистить", () => { _input.Clear(); _parsed = null; _preview.Children.Clear(); })));
        header.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetColumnSpan(header, 3);
        grid.Children.Add(header);

        Grid.SetRow(_input, 1);
        grid.Children.Add(_input);

        var previewScroll = new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(previewScroll, 1);
        Grid.SetColumn(previewScroll, 2);
        grid.Children.Add(previewScroll);

        _preview.Children.Add(Ui.Text("Здесь появится разобранная неделя.", "Muted"));
        Content = grid;
    }

    private void Parse()
    {
        _parsed = WeekImporter.Parse(_input.Text);
        _preview.Children.Clear();

        if (_parsed.Warnings.Count > 0)
        {
            var warnings = new StackPanel();
            warnings.Children.Add(Ui.Text("Предупреждения", bold: true, color: Ui.Res("Warning")));
            foreach (var w in _parsed.Warnings) warnings.Children.Add(Ui.Text("• " + w));
            _preview.Children.Add(Ui.Card(warnings, Ui.Res("WarningSoft")));
        }

        if (_parsed.Assignments.Count == 0)
        {
            _preview.Children.Add(Ui.Text("Не нашёл ни одного задания.", "Muted"));
            return;
        }

        _preview.Children.Add(Ui.Wrap(
            Ui.Button("Установить неделю ученику…", Install, "PrimaryButton"),
            Ui.Button("Сохранить как шаблон…", SaveTemplate)));

        foreach (var day in _parsed.Assignments.GroupBy(a => a.DayIndex).OrderBy(g => g.Key))
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Text(PlannerService.DayNames[day.Key], "H2"));
            foreach (var a in day)
            {
                panel.Children.Add(Ui.Text(a.Title, bold: true, margin: new Thickness(0, 6, 0, 0)));
                var meta = new List<string> { $"{a.EstimatedMinutes} мин" };
                if (a.Url.Length > 0) meta.Add(a.Url);
                if (a.Mission.Length > 0) meta.Add("миссия: " + a.Mission);
                foreach (var r in a.Rules) meta.Add($"повтор через {r.Days} дн.");
                panel.Children.Add(Ui.Text(string.Join(" · ", meta), "Muted", size: 12));
                if (a.Description.Length > 0) panel.Children.Add(Ui.Text(a.Description, size: 13));
                foreach (var ex in a.Exercises)
                {
                    var body = ex.Kind == ExerciseKind.GapFill ? ExerciseText.ParseGaps(ex.Prompt).Template : ex.Prompt;
                    var line = $"  • {PlannerService.Label(ex.Kind)}: {body}";
                    if (ex.Config.SegmentStart.Length > 0) line += $" [{ex.Config.SegmentStart}–{ex.Config.SegmentEnd}]";
                    if (ex.Config.Reference.Count > 0) line += $" (ключ: {ex.Config.Reference.Count} тез.)";
                    if (ex.Config.Transcript.Length > 0) line += $" — «{ex.Config.Transcript}»";
                    panel.Children.Add(Ui.Text(line, size: 13));
                }
            }
            _preview.Children.Add(Ui.Card(panel));
        }
    }

    private void Install()
    {
        if (_parsed == null) return;
        var target = TargetPicker.Pick(this, "Установить неделю");
        if (target == null) return;
        if (TargetPicker.WriteWithConfirm(target, replace => TemplateService.Materialize(Session.User.Id, target.EnrollmentId, target.Monday, _parsed, replace)))
            Ui.Info("Неделя установлена. Проверьте её у ученика во вкладке «План недели».");
    }

    private void SaveTemplate()
    {
        if (_parsed == null) return;
        var form = new FormDialog("Сохранить как шаблон", this);
        var name = form.AddText("Название");
        form.OnSubmit = () => TemplateService.SaveTemplate(Session.User.Id, name.Text, "", _parsed);
        if (form.Run()) Ui.Info("Шаблон сохранён.");
    }
}
