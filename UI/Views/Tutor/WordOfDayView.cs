using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>A word per day, shown to all of the tutor's students.</summary>
public class WordOfDayView : UserControl
{
    private readonly DatePicker _date = new() { FirstDayOfWeek = DayOfWeek.Monday, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _word = Ui.Box();
    private readonly TextBox _translation = Ui.Box();
    private readonly TextBox _example = Ui.Box();
    private readonly StackPanel _list = new();

    public WordOfDayView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(Ui.Text("Слово дня", "H1"));
        root.Children.Add(Ui.Text("Ученики видят самое свежее слово (на сегодня или раньше) на главном экране. Одно слово на дату — повторное сохранение заменяет его.", "Muted"));

        _date.SelectedDate = Clock.Today.ToDateTime(TimeOnly.MinValue);
        var form = new StackPanel();
        form.Children.Add(Ui.Text("Дата", "FieldLabel"));
        form.Children.Add(_date);
        form.Children.Add(Ui.Text("Слово", "FieldLabel"));
        form.Children.Add(_word);
        form.Children.Add(Ui.Text("Перевод", "FieldLabel"));
        form.Children.Add(_translation);
        form.Children.Add(Ui.Text("Пример", "FieldLabel"));
        form.Children.Add(_example);
        form.Children.Add(Ui.Row(Ui.Button("Опубликовать", Save, "PrimaryButton")));
        root.Children.Add(Ui.Card(form));
        root.Children.Add(_list);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Reload();
    }

    private void Save()
    {
        if (_date.SelectedDate is not { } date) throw new UserError("Выберите дату.");
        VocabularyService.SetWordOfDay(Session.User.Id, DateOnly.FromDateTime(date), _word.Text, _translation.Text, _example.Text);
        _word.Clear();
        _translation.Clear();
        _example.Clear();
        Reload();
    }

    private void Reload()
    {
        _list.Children.Clear();
        foreach (var w in VocabularyService.WordsOfDayOf(Session.User.Id))
        {
            var info = new StackPanel();
            info.Children.Add(Ui.Text($"{w.Date:dd.MM.yyyy}{(w.Date == Clock.Today ? " (сегодня)" : "")}", "Muted", size: 12));
            info.Children.Add(Ui.Text($"{w.Word} — {w.Translation}", bold: true));
            if (w.Example.Length > 0) info.Children.Add(Ui.Text(w.Example, "Muted"));
            var buttons = Ui.Row(
                Ui.Button("Изменить", () =>
                {
                    _date.SelectedDate = w.Date.ToDateTime(TimeOnly.MinValue);
                    _word.Text = w.Word;
                    _translation.Text = w.Translation;
                    _example.Text = w.Example;
                }),
                Ui.IconButton(Icons.Delete, () => { VocabularyService.DeleteWordOfDay(w.Id); Reload(); }, "Удалить", danger: true));
            buttons.VerticalAlignment = VerticalAlignment.Top;
            var row = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(info);
            _list.Children.Add(Ui.Card(row));
        }
    }
}
