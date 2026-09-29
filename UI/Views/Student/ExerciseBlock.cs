using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Controls;

namespace TutorSpace.UI.Views.Student;

/// <summary>The student's player for one exercise, with its attempt history.</summary>
public class ExerciseBlock : UserControl
{
    private readonly Exercise _exercise;
    private readonly Assignment _assignment;
    private readonly IReadOnlyList<VocabularyEntry> _words;
    private readonly Action _changed;
    private readonly int _studentId = Session.User.Id;

    public ExerciseBlock(Exercise exercise, Assignment assignment, IReadOnlyList<VocabularyEntry> words, Action changed)
    {
        _exercise = exercise;
        _assignment = assignment;
        _words = words;
        _changed = changed;
        Margin = new Thickness(0, 10, 0, 0);
        Render();
    }

    private void Render()
    {
        var attempts = ExerciseService.AttemptsOf(_exercise.Id, _studentId);
        var last = attempts.LastOrDefault();
        var allowed = _exercise.AttemptsAllowed;
        var canSubmit = allowed == 0 || attempts.Count < allowed;

        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var badge = KindStyle.Badge(_exercise.Kind);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);

        var status = last == null
            ? Ui.Chip("Не начато", Ui.Res("SurfaceMuted"), Ui.Res("TextMuted"))
            : _exercise.IsAutoChecked && !last.IsPassed
                ? Ui.Chip($"{last.Score} из {last.MaxScore}", Ui.Res("DangerSoft"), Ui.Res("Danger"))
                : Ui.Chip(_exercise.IsAutoChecked ? "Всё верно" : "Отправлено", Ui.Res("SuccessSoft"), Ui.Res("Success"));
        status.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(status);

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Text(PlannerService.Label(_exercise.Kind), "H3"));
        var sub = allowed > 0 ? $"Попыток: {attempts.Count} из {allowed}" : "Попыток без ограничения";
        title.Children.Add(Ui.Text(sub, "Muted", size: 12));
        head.Children.Add(title);
        panel.Children.Add(head);
        if (_exercise.Instruction.Length > 0) panel.Children.Add(Ui.Text(_exercise.Instruction, "Muted", margin: new Thickness(0, 4, 0, 0)));

        switch (_exercise.Kind)
        {
            case ExerciseKind.GapFill: BuildGapFill(panel, last, canSubmit); break;
            case ExerciseKind.OpenAnswer: BuildOpenAnswer(panel, last, canSubmit); break;
            case ExerciseKind.Theses: BuildTheses(panel, last, canSubmit); break;
            default: BuildVoice(panel, canSubmit); break;
        }

        if (!canSubmit) panel.Children.Add(Ui.Text("Попытки закончились.", "Muted", margin: new Thickness(0, 6, 0, 0)));

        var reveal = ExerciseService.RevealText(_exercise, last);
        if (reveal.Length > 0)
            panel.Children.Add(new Border
            {
                Background = Ui.Res("PrimarySoft"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0),
                Child = Ui.Text(reveal),
            });

        BuildHistory(panel, attempts);

        Content = new Border
        {
            Child = panel, Padding = new Thickness(16), CornerRadius = new CornerRadius(10),
            Background = Ui.Res("Surface"), BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
        };
    }

    private string PromptText => ExerciseText.Personalize(_exercise.Prompt, _words);

    private void Submit(AttemptAnswers answers, int? audioId = null)
    {
        ExerciseService.SubmitAttempt(_studentId, _exercise.Id, answers, audioId);
        Render();
        _changed();
    }

    // ---------- Kinds ----------

    private void BuildGapFill(StackPanel panel, ExerciseAttempt? last, bool canSubmit)
    {
        var config = _exercise.Config;
        var parts = config.Template.Split(ExerciseText.BlankMark);
        var boxes = new List<TextBox>();
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

        for (var i = 0; i < parts.Length; i++)
        {
            foreach (var word in parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                wrap.Children.Add(new TextBlock { Text = word + " ", VerticalAlignment = VerticalAlignment.Center, FontSize = 15, Margin = new Thickness(0, 4, 0, 4) });
            if (i >= parts.Length - 1) continue;

            var index = boxes.Count;
            var blank = index < config.Blanks.Count ? config.Blanks[index] : new GapBlank();
            var box = new TextBox { Width = 120, Margin = new Thickness(0, 2, 6, 2), FontSize = 15 };
            if (blank.Hint.Length > 0) box.ToolTip = "Подсказка: " + blank.Hint;
            if (last != null)
            {
                box.Text = index < last.Answers.Blanks.Count ? last.Answers.Blanks[index] : "";
                var ok = index < last.Answers.Results.Count && last.Answers.Results[index];
                box.Background = Ui.Res(ok ? "SuccessSoft" : "DangerSoft");
            }
            boxes.Add(box);
            wrap.Children.Add(box);
            if (blank.Hint.Length > 0)
                wrap.Children.Add(new TextBlock { Text = $"({blank.Hint}) ", Foreground = Ui.Res("TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        }
        panel.Children.Add(wrap);

        if (canSubmit)
            panel.Children.Add(Ui.Row(Ui.Button(last == null ? "Проверить" : "Проверить ещё раз",
                () => Submit(new AttemptAnswers { Blanks = boxes.Select(b => b.Text).ToList() }), "PrimaryButton")));
    }

    private void BuildOpenAnswer(StackPanel panel, ExerciseAttempt? last, bool canSubmit)
    {
        panel.Children.Add(Ui.Text(PromptText, size: 15, margin: new Thickness(0, 6, 0, 6)));
        if (_exercise.Config.MinWords > 0) panel.Children.Add(Ui.Text($"Не меньше {Ui.Plural(_exercise.Config.MinWords, "слова", "слов", "слов")}.", "Muted", size: 12));
        if (!canSubmit) return;
        var box = Ui.Box(last?.Answers.Text ?? "", multiline: true);
        panel.Children.Add(box);
        panel.Children.Add(Ui.Row(Ui.Button(last == null ? "Отправить" : "Отправить новую версию",
            () => Submit(new AttemptAnswers { Text = box.Text }), "PrimaryButton")));
    }

    private void BuildTheses(StackPanel panel, ExerciseAttempt? last, bool canSubmit)
    {
        var config = _exercise.Config;
        panel.Children.Add(Ui.Text(PromptText, size: 15, margin: new Thickness(0, 6, 0, 6)));
        if (config.SegmentStart.Length > 0 && _assignment.Url.Length > 0)
        {
            var start = ExerciseText.ParseTimecode(config.SegmentStart);
            panel.Children.Add(Ui.Row(Ui.Button($"Открыть отрезок {config.SegmentStart}–{config.SegmentEnd}",
                () => ResourceResolver.Open(_assignment.Url, start), icon: Icons.Video)));
        }

        if (last != null && last.Answers.Theses.Count > 0)
        {
            // Second step: after reading the key, the student marks their own theses.
            panel.Children.Add(Ui.Text("Сверься с ключом ниже и отметь тезисы, которые совпали по смыслу:", "FieldLabel"));
            var checks = new List<CheckBox>();
            for (var i = 0; i < last.Answers.Theses.Count; i++)
            {
                var check = new CheckBox { Content = last.Answers.Theses[i], IsChecked = i < last.Answers.Marks.Count && last.Answers.Marks[i] };
                checks.Add(check);
                panel.Children.Add(check);
            }
            panel.Children.Add(Ui.Row(Ui.Button("Сохранить отметки", () =>
            {
                ExerciseService.SetThesesMarks(last.Id, checks.Select(c => c.IsChecked == true).ToList());
                Render();
                _changed();
            })));
        }

        if (!canSubmit) return;
        panel.Children.Add(Ui.Text($"Твои тезисы — по одному на строку ({config.MinTheses}–{config.MaxTheses})", "FieldLabel"));
        var box = Ui.Box(multiline: true, minHeight: 90);
        panel.Children.Add(box);
        panel.Children.Add(Ui.Row(Ui.Button(last == null ? "Отправить тезисы" : "Отправить заново", () =>
            Submit(new AttemptAnswers { Theses = box.Text.Replace("\r", "").Split('\n').Select(t => t.Trim().TrimStart('-', '•', '*').Trim()).ToList() }),
            "PrimaryButton")));
    }

    private void BuildVoice(StackPanel panel, bool canSubmit)
    {
        panel.Children.Add(Ui.Text(PromptText, size: 15, margin: new Thickness(0, 6, 0, 6)));
        // Without a clip there is nothing to listen to, so the text to repeat is shown right away.
        if (_exercise.Kind == ExerciseKind.Speaking && _exercise.Audio == null && _exercise.Config.Transcript.Length > 0)
            panel.Children.Add(Ui.Text("Произнеси: «" + _exercise.Config.Transcript + "»", size: 15, bold: true, margin: new Thickness(0, 0, 0, 6)));
        if (_exercise.Audio != null)
            panel.Children.Add(Ui.Row(
                Ui.Button("Послушать оригинал", () => AudioPlayer.Play(_exercise.Audio), icon: Icons.Play),
                Ui.IconButton(Icons.Stop, AudioPlayer.Stop, "Стоп")));
        if (!canSubmit) return;

        panel.Children.Add(new RecorderPanel
        {
            Recorded = (path, seconds) => Ui.Try(() =>
            {
                var asset = MediaService.Import(_studentId, path, MediaSource.StudentRecording,
                    $"{PlannerService.Label(_exercise.Kind)} — {DateTime.Now:dd.MM HH:mm}", durationSeconds: seconds);
                try
                {
                    Submit(new AttemptAnswers(), asset.Id);
                }
                catch
                {
                    MediaService.Delete(asset.Id);
                    throw;
                }
            }),
        });
    }

    private void BuildHistory(StackPanel panel, List<ExerciseAttempt> attempts)
    {
        if (attempts.Count == 0) return;
        panel.Children.Add(Ui.Text("Мои попытки", "FieldLabel"));
        foreach (var attempt in attempts.AsEnumerable().Reverse())
        {
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            var line = Ui.Row(Ui.Text($"№{attempt.AttemptNo} · {attempt.CreatedAt:dd.MM HH:mm}  ", "Muted", size: 12));
            if (_exercise.IsAutoChecked) line.Children.Add(Ui.Text($"{attempt.Score}/{attempt.MaxScore}", size: 12, bold: true));
            else if (_exercise.Kind == ExerciseKind.Theses && attempt.Answers.Marks.Count > 0)
                line.Children.Add(Ui.Text($"совпало {attempt.Score} из {attempt.MaxScore}", size: 12, bold: true));
            if (attempt.Audio != null)
            {
                var play = Ui.IconButton(Icons.Play, () => AudioPlayer.Play(attempt.Audio), "Прослушать мою запись");
                play.Margin = new Thickness(4, 0, 0, 0);
                line.Children.Add(play);
            }
            row.Children.Add(line);
            if (_exercise.Kind == ExerciseKind.OpenAnswer) row.Children.Add(Ui.Text(attempt.Answers.Text, size: 13));
            if (attempt.TutorComment.Length > 0)
                row.Children.Add(Ui.Text("Преподаватель: " + attempt.TutorComment, color: Ui.Res("Primary")));
            panel.Children.Add(row);
        }
    }
}
