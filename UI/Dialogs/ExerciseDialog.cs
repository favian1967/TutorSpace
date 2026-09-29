using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Dialogs;

/// <summary>The exercise constructor: kind-specific fields plus a live gap-fill preview.</summary>
public class ExerciseDialog : Window
{
    private readonly Exercise _exercise;
    private readonly ComboBox _kind = new();
    private readonly TextBox _instruction = Ui.Box();
    private readonly TextBox _prompt = Ui.Box(multiline: true, minHeight: 90);
    private readonly TextBlock _promptHelp = Ui.Text("", "Muted", size: 12);
    private readonly TextBlock _preview = Ui.Text("", color: null);
    private readonly ComboBox _audio = new();

    private readonly StackPanel _gapPanel = new();
    private readonly StackPanel _openPanel = new();
    private readonly StackPanel _thesesPanel = new();
    private readonly StackPanel _speakingPanel = new();
    private readonly StackPanel _attemptsPanel = new();

    private readonly TextBox _minWords = Ui.Box();
    private readonly TextBox _sample = Ui.Box(multiline: true);
    private readonly TextBox _segStart = Ui.Box();
    private readonly TextBox _segEnd = Ui.Box();
    private readonly TextBox _minTheses = Ui.Box();
    private readonly TextBox _maxTheses = Ui.Box();
    private readonly TextBox _reference = Ui.Box(multiline: true, minHeight: 90);
    private readonly TextBox _transcript = Ui.Box(multiline: true);
    private readonly TextBox _attempts = Ui.Box();

    public ExerciseDialog(Exercise exercise, DependencyObject owner)
    {
        _exercise = exercise;
        Title = exercise.Id == 0 ? "Новое упражнение" : "Упражнение";
        Width = 640;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.SetOwner(this, owner);
        Style = (Style)FindResource(typeof(Window));

        foreach (var kind in Enum.GetValues<ExerciseKind>())
            _kind.Items.Add(new ComboItem(kind, PlannerService.Label(kind)));
        _audio.Items.Add(new ComboItem(0, "(без аудио)"));
        foreach (var clip in MediaService.LibraryOf(Session.User.Id))
            _audio.Items.Add(new ComboItem(clip.Id, clip.DisplayName));

        var form = new StackPanel { Margin = new Thickness(20) };
        form.Children.Add(Field("Тип упражнения", _kind));
        form.Children.Add(Field("Короткая инструкция (необязательно)", _instruction));
        form.Children.Add(Field("Текст упражнения", _prompt));
        form.Children.Add(_promptHelp);

        _gapPanel.Children.Add(Ui.Text("Как увидит ученик:", "FieldLabel"));
        _gapPanel.Children.Add(new Border { Child = _preview, Padding = new Thickness(10), Background = Ui.Res("AppBackground"), CornerRadius = new CornerRadius(6) });
        form.Children.Add(_gapPanel);

        _openPanel.Children.Add(Field("Минимум слов в ответе (0 — без ограничения)", _minWords));
        _openPanel.Children.Add(Field("Пример ответа (ученик увидит после своей попытки)", _sample));
        form.Children.Add(_openPanel);

        var seg = new Grid();
        seg.ColumnDefinitions.Add(new ColumnDefinition());
        seg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        seg.ColumnDefinitions.Add(new ColumnDefinition());
        seg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        seg.ColumnDefinitions.Add(new ColumnDefinition());
        seg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        seg.ColumnDefinitions.Add(new ColumnDefinition());
        AddAt(seg, Field("Отрезок: начало (мм:сс)", _segStart), 0);
        AddAt(seg, Field("конец (мм:сс)", _segEnd), 2);
        AddAt(seg, Field("Мин. тезисов", _minTheses), 4);
        AddAt(seg, Field("Макс. тезисов", _maxTheses), 6);
        _thesesPanel.Children.Add(seg);
        _thesesPanel.Children.Add(Field("Ваши тезисы — ключ (по одному на строку; ученик увидит после ответа)", _reference));
        form.Children.Add(_thesesPanel);

        _speakingPanel.Children.Add(Field("Текст оригинала (если у аудио нет расшифровки)", _transcript));
        form.Children.Add(_speakingPanel);

        form.Children.Add(Field("Аудио из аудиотеки", _audio));
        _attemptsPanel.Children.Add(Field("Сколько попыток (0 — без ограничения; для голосовых по умолчанию 3)", _attempts));
        form.Children.Add(_attemptsPanel);

        var save = new Button { Content = "Сохранить", IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        save.Click += (_, _) => { if (Ui.Try(Save)) DialogResult = true; };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var buttons = Ui.Row(save, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 8, 20, 16);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;

        _kind.SelectionChanged += (_, _) => UpdateKind();
        _prompt.TextChanged += (_, _) => UpdatePreview();
        Fill();
    }

    private static StackPanel Field(string label, UIElement input)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(label, "FieldLabel"));
        panel.Children.Add(input);
        return panel;
    }

    private static void AddAt(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private ExerciseKind Kind => (ExerciseKind)((ComboItem)_kind.SelectedItem).Value;

    private void Fill()
    {
        var c = _exercise.Config;
        foreach (ComboItem item in _kind.Items)
            if ((ExerciseKind)item.Value == _exercise.Kind) _kind.SelectedItem = item;
        _instruction.Text = _exercise.Instruction;
        _prompt.Text = _exercise.Prompt;
        _minWords.Text = c.MinWords.ToString();
        _sample.Text = c.SampleAnswer;
        _segStart.Text = c.SegmentStart;
        _segEnd.Text = c.SegmentEnd;
        _minTheses.Text = c.MinTheses.ToString();
        _maxTheses.Text = c.MaxTheses.ToString();
        _reference.Text = string.Join(Environment.NewLine, c.Reference);
        _transcript.Text = c.Transcript;
        _attempts.Text = c.AttemptsAllowed?.ToString() ?? "";
        _audio.SelectedIndex = 0;
        foreach (ComboItem item in _audio.Items)
            if ((int)item.Value == (_exercise.AudioId ?? 0)) _audio.SelectedItem = item;
        UpdateKind();
    }

    private void UpdateKind()
    {
        var kind = Kind;
        _gapPanel.Visibility = kind == ExerciseKind.GapFill ? Visibility.Visible : Visibility.Collapsed;
        _openPanel.Visibility = kind == ExerciseKind.OpenAnswer ? Visibility.Visible : Visibility.Collapsed;
        _thesesPanel.Visibility = kind == ExerciseKind.Theses ? Visibility.Visible : Visibility.Collapsed;
        _speakingPanel.Visibility = kind == ExerciseKind.Speaking ? Visibility.Visible : Visibility.Collapsed;
        _promptHelp.Text = kind switch
        {
            ExerciseKind.GapFill => "Пропуски — в двойных фигурных скобках: He {{went|did go::go}} to school. Через | — допустимые варианты, после :: — подсказка. Проверяется автоматически.",
            ExerciseKind.OpenAnswer => "Вопрос, на который ученик отвечает текстом. Можно подставить слова из его словаря: <<word>>, <<translation>>, <<example>>.",
            ExerciseKind.Theses => "Что нужно сделать. Ученик пишет тезисы, затем видит ваш ключ и сам отмечает совпадения.",
            ExerciseKind.Speaking => "Прикрепите аудио из аудиотеки — ученик слушает и записывает свою попытку (обычно до 3 раз).",
            _ => "Тема монолога. Ученик записывает голосовое сообщение.",
        };
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (Kind != ExerciseKind.GapFill) return;
        var (template, blanks) = ExerciseText.ParseGaps(_prompt.Text);
        _preview.Text = blanks.Count == 0
            ? "Пока нет ни одного {{пропуска}}."
            : template + "\n\n" + string.Join("\n", blanks.Select((b, i) =>
                $"{i + 1}) {string.Join(" / ", b.Answers)}{(b.Hint.Length > 0 ? $"  (подсказка: {b.Hint})" : "")}"));
    }

    private void Save()
    {
        var kind = Kind;
        var config = _exercise.Config;
        config.MinWords = kind == ExerciseKind.OpenAnswer ? FormDialog.ParseInt(_minWords, "Минимум слов", 0, 500) : config.MinWords;
        config.SampleAnswer = _sample.Text.Trim();
        if (kind == ExerciseKind.Theses)
        {
            foreach (var box in new[] { _segStart, _segEnd })
                if (box.Text.Trim().Length > 0 && ExerciseText.ParseTimecode(box.Text) == null)
                    throw new UserError($"Не понял таймкод «{box.Text}». Формат: 13:23 или 1:02:11.");
            config.MinTheses = FormDialog.ParseInt(_minTheses, "Мин. тезисов", 0, 20);
            config.MaxTheses = FormDialog.ParseInt(_maxTheses, "Макс. тезисов", 0, 20);
            if (config.MaxTheses > 0 && config.MinTheses > config.MaxTheses) throw new UserError("Минимум тезисов больше максимума.");
        }
        config.SegmentStart = _segStart.Text.Trim();
        config.SegmentEnd = _segEnd.Text.Trim();
        config.Reference = _reference.Text.Replace("\r", "").Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '•').Trim()).Where(l => l.Length > 0).ToList();
        config.Transcript = _transcript.Text.Trim();
        config.AttemptsAllowed = _attempts.Text.Trim().Length == 0 ? null : FormDialog.ParseInt(_attempts, "Попыток", 0, 50);

        var audioId = (int)((ComboItem)_audio.SelectedItem).Value;
        ExerciseService.Save(new Exercise
        {
            Id = _exercise.Id,
            AssignmentId = _exercise.AssignmentId,
            Kind = kind,
            Instruction = _instruction.Text,
            Prompt = _prompt.Text,
            AudioId = audioId == 0 ? null : audioId,
            Config = config,
        });
    }
}
