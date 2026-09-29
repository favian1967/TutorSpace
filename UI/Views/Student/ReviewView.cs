using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Student;

/// <summary>Flashcards for words that are due, graded with four SM-2 answers.</summary>
public class ReviewView : UserControl
{
    private readonly Queue<VocabularyEntry> _queue;
    private readonly StackPanel _root = new() { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Stretch };
    private int _reviewed;

    public ReviewView()
    {
        _queue = new Queue<VocabularyEntry>(VocabularyService.DueEntries(Session.User.Id, Clock.Today));
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ShowNext();
    }

    private void Header()
    {
        _root.Children.Clear();
        _root.Children.Add(Ui.Text("Повторение слов", "H1"));
        var total = _reviewed + _queue.Count;
        _root.Children.Add(Ui.Text($"Повторено: {_reviewed} · осталось: {_queue.Count}", "Muted", margin: new Thickness(0, -4, 0, 10)));
        _root.Children.Add(new ProgressBar { Maximum = Math.Max(1, total), Value = _reviewed, Margin = new Thickness(0, 0, 0, 20) });
    }

    private void ShowNext()
    {
        Header();
        if (_queue.Count == 0)
        {
            var stats = VocabularyService.StatsFor(Session.User.Id, Clock.Today);
            var done = new StackPanel();
            done.Children.Add(Ui.Text(_reviewed > 0 ? "Все слова на сегодня повторены!" : "На сегодня повторять нечего.", "H2"));
            done.Children.Add(Ui.Text($"В словаре {stats.Total}, выучено {stats.Learned}. Новые слова добавляй в «Мой словарь».", "Muted"));
            _root.Children.Add(Ui.Card(done));
            return;
        }

        var entry = _queue.Peek();
        var card = FlashCard(entry, false);
        var reveal = Ui.Button("Показать перевод", () => ShowAnswer(entry), "PrimaryButton");
        reveal.HorizontalAlignment = HorizontalAlignment.Center;
        reveal.MinWidth = 220;
        reveal.MinHeight = 40;
        reveal.Margin = new Thickness(0, 24, 0, 0);
        card.Children.Add(reveal);
        _root.Children.Add(Wrap(card));
        reveal.Focus();
    }

    private void ShowAnswer(VocabularyEntry entry)
    {
        Header();
        var card = FlashCard(entry, true);
        var ask = Ui.Text("Как вспомнилось?", "FieldLabel", margin: new Thickness(0, 24, 0, 8));
        ask.HorizontalAlignment = HorizontalAlignment.Center;
        card.Children.Add(ask);

        var buttons = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var grade in Enum.GetValues<ReviewGrade>())
        {
            var days = VocabularyService.NextInterval(entry, grade);
            var label = $"{VocabularyService.Label(grade)}\n{VocabularyService.IntervalLabel(days)}";
            var button = Ui.Button(label, () => Grade(entry, grade), grade == ReviewGrade.Good ? "PrimaryButton" : null);
            button.MinWidth = 110;
            buttons.Children.Add(button);
        }
        card.Children.Add(buttons);
        _root.Children.Add(Wrap(card));
    }

    private static StackPanel FlashCard(VocabularyEntry entry, bool answer)
    {
        var card = new StackPanel { Margin = new Thickness(0, 24, 0, 8) };
        var word = Ui.Text(entry.Word, size: 36, bold: true);
        word.HorizontalAlignment = HorizontalAlignment.Center;
        card.Children.Add(word);
        if (!answer) return card;
        var translation = Ui.Text(entry.Translation, size: 20, color: Ui.Res("Primary"), margin: new Thickness(0, 8, 0, 0));
        translation.HorizontalAlignment = HorizontalAlignment.Center;
        card.Children.Add(translation);
        if (entry.Example.Length > 0)
        {
            var example = Ui.Text(entry.Example, "Muted", margin: new Thickness(0, 10, 0, 0));
            example.TextAlignment = TextAlignment.Center;
            card.Children.Add(example);
        }
        return card;
    }

    private static Border Wrap(UIElement card)
    {
        var border = Ui.Card(card);
        border.Padding = new Thickness(32, 28, 32, 28);
        return border;
    }

    private void Grade(VocabularyEntry entry, ReviewGrade grade)
    {
        VocabularyService.RecordReview(entry.Id, grade);
        _queue.Dequeue();
        _reviewed++;
        // «Снова» brings the word back at the end of today's session.
        if (grade == ReviewGrade.Again)
        {
            using var db = new AppDbContext();
            if (db.Vocabulary.Find(entry.Id) is { } fresh) _queue.Enqueue(fresh);
        }
        ShowNext();
    }
}
