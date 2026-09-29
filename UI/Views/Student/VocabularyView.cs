using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Student;

/// <summary>The personal dictionary: add words, search, mark as learned.</summary>
public class VocabularyView : UserControl
{
    private readonly TextBox _word = Ui.Box();
    private readonly TextBox _translation = Ui.Box();
    private readonly TextBox _example = Ui.Box();
    private readonly TextBox _search = Ui.Box();
    private readonly ComboBox _filter = new() { Width = 160, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _stats = Ui.Text("", "Muted");
    private readonly StackPanel _list = new();

    public VocabularyView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(Ui.Text("Мой словарь", "H1"));
        root.Children.Add(_stats);

        var form = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < 3; i++)
        {
            form.ColumnDefinitions.Add(new ColumnDefinition());
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        }
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(form, Labeled("Слово", _word), 0);
        Place(form, Labeled("Перевод", _translation), 2);
        Place(form, Labeled("Пример (необязательно)", _example), 4);
        var add = Ui.Button("Добавить", Add, "PrimaryButton");
        add.VerticalAlignment = VerticalAlignment.Bottom;
        Place(form, add, 6);
        root.Children.Add(Ui.Card(form));

        _filter.Items.Add(new ComboItem("all", "Все слова"));
        _filter.Items.Add(new ComboItem("due", "К повторению"));
        _filter.Items.Add(new ComboItem("learning", "Учу"));
        _filter.Items.Add(new ComboItem("learned", "Выученные"));
        _filter.SelectedIndex = 0;
        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_filter, Dock.Right);
        searchRow.Children.Add(_filter);
        searchRow.Children.Add(_search);
        _search.ToolTip = "Поиск по слову или переводу";
        root.Children.Add(Ui.Text("Поиск", "FieldLabel"));
        root.Children.Add(searchRow);
        root.Children.Add(_list);

        _search.TextChanged += (_, _) => Reload();
        _filter.SelectionChanged += (_, _) => Reload();
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Reload();
    }

    private static StackPanel Labeled(string label, UIElement input)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(label, "FieldLabel"));
        panel.Children.Add(input);
        return panel;
    }

    private static void Place(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private void Add()
    {
        VocabularyService.Add(Session.User.Id, _word.Text, _translation.Text, _example.Text);
        _word.Clear();
        _translation.Clear();
        _example.Clear();
        _word.Focus();
        Reload();
    }

    private void Reload()
    {
        var today = Clock.Today;
        var stats = VocabularyService.StatsFor(Session.User.Id, today);
        _stats.Text = $"Всего {stats.Total} · новых {stats.New} · учу {stats.Learning} · выучено {stats.Learned} · к повторению сегодня {stats.DueToday} · повторено сегодня {stats.ReviewedToday}";

        var query = _search.Text.Trim().ToLowerInvariant();
        var filter = (string)((ComboItem)_filter.SelectedItem).Value;
        var entries = VocabularyService.EntriesOf(Session.User.Id)
            .Where(v => query.Length == 0 || v.Word.ToLowerInvariant().Contains(query) || v.Translation.ToLowerInvariant().Contains(query))
            .Where(v => filter switch
            {
                "due" => v.IsDue(today),
                "learning" => !v.IsLearned,
                "learned" => v.IsLearned,
                _ => true,
            }).ToList();

        _list.Children.Clear();
        if (entries.Count == 0) _list.Children.Add(Ui.Text("Слов нет.", "Muted"));
        foreach (var v in entries)
        {
            var info = new StackPanel();
            info.Children.Add(Ui.Text($"{v.Word} — {v.Translation}", bold: true));
            if (v.Example.Length > 0) info.Children.Add(Ui.Text(v.Example, "Muted"));
            var next = v.IsLearned ? "выучено" : v.NextReviewOn is { } n ? (n <= today ? "повторить сегодня" : $"повтор {n:dd.MM}") : "";
            info.Children.Add(Ui.Text($"{VocabularyService.Label(v.Status)} · {next} · повторений {v.ReviewCount}", "Muted", size: 12));

            var buttons = Ui.Row(
                Ui.Button(v.IsLearned ? "Вернуть в изучение" : "Уже знаю", () => { VocabularyService.SetLearned(v.Id, !v.IsLearned); Reload(); }),
                Ui.Button("Изменить", () => Edit(v)),
                Ui.IconButton(Icons.Delete, () =>
                {
                    if (!Ui.Confirm($"Удалить «{v.Word}» из словаря?")) return;
                    VocabularyService.Delete(v.Id);
                    Reload();
                }, "Удалить", danger: true));
            buttons.VerticalAlignment = VerticalAlignment.Top;
            var row = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(info);
            var card = Ui.Card(row);
            card.Padding = new Thickness(12);
            card.Margin = new Thickness(0, 0, 0, 8);
            _list.Children.Add(card);
        }
    }

    private void Edit(VocabularyEntry entry)
    {
        var form = new FormDialog("Слово", this);
        var word = form.AddText("Слово", entry.Word);
        var translation = form.AddText("Перевод", entry.Translation);
        var example = form.AddText("Пример", entry.Example);
        form.OnSubmit = () => VocabularyService.Update(entry.Id, word.Text, translation.Text, example.Text);
        if (form.Run()) Reload();
    }
}
