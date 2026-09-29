using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Dialogs;

/// <summary>
/// A small form built from code: add fields, set <see cref="OnSubmit"/>, call <see cref="Run"/>.
/// The dialog stays open while the submit action throws, showing the error.
/// </summary>
public class FormDialog : Window
{
    private readonly StackPanel _fields = new();
    private readonly Button _ok;

    public Action? OnSubmit { get; set; }

    public FormDialog(string title, DependencyObject? owner, string okText = "Сохранить", double width = 440)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Ui.SetOwner(this, owner);
        Style = (Style)FindResource(typeof(Window));

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Ui.Text(title, "H2"));
        root.Children.Add(_fields);

        _ok = new Button { Content = okText, IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        _ok.Click += (_, _) =>
        {
            if (Ui.Try(() => OnSubmit?.Invoke())) DialogResult = true;
        };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var buttons = Ui.Row(_ok, cancel);
        buttons.Margin = new Thickness(0, 18, 0, 0);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 760 };
    }

    public bool Run() => ShowDialog() == true;

    public void AddNote(string text) => _fields.Children.Add(Ui.Text(text, "Muted", margin: new Thickness(0, 4, 0, 4)));

    private void Label(string label) => _fields.Children.Add(Ui.Text(label, "FieldLabel"));

    public TextBox AddText(string label, string value = "", bool multiline = false, double minHeight = 70)
    {
        Label(label);
        var box = Ui.Box(value, multiline, minHeight);
        _fields.Children.Add(box);
        if (_fields.Children.Count == 2) Loaded += (_, _) => box.Focus();
        return box;
    }

    public PasswordBox AddPassword(string label)
    {
        Label(label);
        var box = new PasswordBox();
        _fields.Children.Add(box);
        return box;
    }

    public ComboBox AddCombo<T>(string label, IEnumerable<(T Value, string Text)> items, T? selected = default)
    {
        Label(label);
        var combo = new ComboBox { DisplayMemberPath = "Text", SelectedValuePath = "Value" };
        foreach (var (value, text) in items)
            combo.Items.Add(new ComboItem(value!, text));
        combo.SelectedIndex = 0;
        if (selected != null)
            foreach (ComboItem item in combo.Items)
                if (Equals(item.Value, selected)) combo.SelectedItem = item;
        _fields.Children.Add(combo);
        return combo;
    }

    public DatePicker AddDate(string label, DateOnly? value)
    {
        Label(label);
        var picker = new DatePicker { SelectedDate = value?.ToDateTime(TimeOnly.MinValue), FirstDayOfWeek = DayOfWeek.Monday };
        _fields.Children.Add(picker);
        return picker;
    }

    public CheckBox AddCheck(string text, bool value)
    {
        var check = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 10, 0, 0) };
        _fields.Children.Add(check);
        return check;
    }

    public void AddElement(UIElement element) => _fields.Children.Add(element);

    public static T Selected<T>(ComboBox combo) => (T)((ComboItem)combo.SelectedItem).Value;

    public static int ParseInt(TextBox box, string name, int min = 0, int max = int.MaxValue)
    {
        if (!int.TryParse(box.Text.Trim(), out var n) || n < min || n > max)
            throw new UserError($"«{name}» — нужно число от {min} до {max}.");
        return n;
    }
}

public record ComboItem(object Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Choose a student and a Monday — used for copying weeks, templates and the sandbox.</summary>
public static class TargetPicker
{
    public record Target(int EnrollmentId, DateOnly Monday);

    public static Target? Pick(DependencyObject? owner, string title, int? enrollmentId = null, DateOnly? monday = null)
    {
        var students = AuthService.StudentsOf(Session.User.Id);
        if (students.Count == 0) throw new UserError("Сначала добавьте ученика.");

        var form = new FormDialog(title, owner, "Далее");
        var studentCombo = form.AddCombo("Ученик", students.Select(s => (s.Id, s.Student.DisplayName)), enrollmentId ?? students[0].Id);
        var date = form.AddDate("Неделя (любой день — возьмётся её понедельник)", monday ?? Clock.MondayOf(Clock.Today).AddDays(7));
        Target? result = null;
        form.OnSubmit = () =>
        {
            if (date.SelectedDate is not { } picked) throw new UserError("Выберите дату.");
            result = new Target(FormDialog.Selected<int>(studentCombo), Clock.MondayOf(DateOnly.FromDateTime(picked)));
        };
        return form.Run() ? result : null;
    }

    /// <summary>Runs <paramref name="write"/>; if the week is busy, asks before replacing it.</summary>
    public static bool WriteWithConfirm(Target target, Action<bool> write)
    {
        var replace = false;
        if (TemplateService.HasPlan(target.EnrollmentId, target.Monday))
        {
            if (!Ui.Confirm($"На неделю {PlannerService.WeekLabel(target.Monday)} у ученика уже есть задания.\nЗаменить их?"))
                return false;
            replace = true;
        }
        return Ui.Try(() => write(replace));
    }
}
