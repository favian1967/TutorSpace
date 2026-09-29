using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>The tutor's roster as a picker on top, the selected student's workspace below.</summary>
public class StudentsView : UserControl
{
    private readonly ComboBox _picker = new() { Width = 300, Padding = new Thickness(8, 5, 30, 5), MaxDropDownHeight = 420 };
    private readonly ContentControl _detail = new();
    private readonly CheckBox _showInactive = new() { Content = "Показывать отключённых", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _toggle;

    public StudentsView()
    {
        var root = new DockPanel { Margin = new Thickness(32, 24, 32, 0) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var actions = Ui.Row(
            Ui.Button("Прикрепить", AttachExisting, tooltip: "Прикрепить уже существующий аккаунт ученика", icon: Icons.Link),
            Ui.Button("Новый ученик", AddStudent, "PrimaryButton", icon: Icons.Add));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(Ui.Text("Ученики", "H1", margin: new Thickness(0)));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _toggle = Ui.Button("Отключить", ToggleActive, "DangerButton", "Отключить ученика (данные сохранятся) или вернуть");
        var bar = Ui.Row(
            _picker,
            Ui.Button("Сбросить пароль", ResetPassword, icon: Icons.Lock),
            _toggle,
            _showInactive);
        foreach (FrameworkElement child in bar.Children) child.Margin = new Thickness(0, 0, 8, 0);
        bar.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        root.Children.Add(_detail);
        Content = root;

        System.Windows.Automation.AutomationProperties.SetName(_picker, "Ученик");
        _picker.SelectionChanged += (_, _) => ShowSelected();
        _showInactive.Click += (_, _) => Reload();
        Reload();
    }

    private Enrollment? Selected => (_picker.SelectedItem as ComboItem)?.Value as Enrollment;

    private void Reload(int? selectId = null)
    {
        selectId ??= Selected?.Id;
        _picker.Items.Clear();
        foreach (var e in AuthService.StudentsOf(Session.User.Id, _showInactive.IsChecked == true))
        {
            var item = new ComboItem(e, e.Student.DisplayName);
            _picker.Items.Add(item);
            if (e.Id == selectId) _picker.SelectedItem = item;
        }
        if (_picker.SelectedItem == null && _picker.Items.Count > 0) _picker.SelectedIndex = 0;
        if (_picker.Items.Count == 0)
            _detail.Content = Ui.Card(Ui.Text("Учеников пока нет. Нажмите «Новый ученик», чтобы создать аккаунт.", "Muted"));
    }

    private void ShowSelected()
    {
        if (Selected is not { } e) return;
        _toggle.Content = e.IsActive ? "Отключить" : "Вернуть";
        _detail.Content = new StudentDetailView(e);
    }

    private void AddStudent()
    {
        var form = new FormDialog("Новый ученик", this, "Создать");
        form.AddNote("Ученик будет входить в программу на этом же компьютере под своим логином.");
        var name = form.AddText("Имя ученика");
        var login = form.AddText("Логин");
        var password = form.AddPassword("Пароль");
        User? created = null;
        form.OnSubmit = () => created = AuthService.CreateStudent(Session.User.Id, login.Text, password.Password, name.Text);
        if (!form.Run() || created == null) return;
        Reload();
        foreach (ComboItem item in _picker.Items)
            if (((Enrollment)item.Value).StudentId == created.Id) _picker.SelectedItem = item;
    }

    private void AttachExisting()
    {
        var form = new FormDialog("Прикрепить ученика", this, "Прикрепить");
        form.AddNote("Если у ученика уже есть аккаунт (например, от другого преподавателя), введите его логин.");
        var login = form.AddText("Логин ученика");
        form.OnSubmit = () => AuthService.EnrollExisting(Session.User.Id, login.Text);
        if (form.Run()) Reload();
    }

    private void ResetPassword()
    {
        if (Selected is not { } e) return;
        var form = new FormDialog($"Новый пароль для {e.Student.DisplayName}", this);
        var password = form.AddPassword("Новый пароль");
        form.OnSubmit = () => AuthService.ResetStudentPassword(e.StudentId, password.Password);
        if (form.Run()) Ui.Info("Пароль изменён.");
    }

    private void ToggleActive()
    {
        if (Selected is not { } e) return;
        if (e.IsActive && !Ui.Confirm($"Отключить ученика {e.Student.DisplayName}? Данные сохранятся, его можно будет вернуть.")) return;
        AuthService.SetEnrollmentActive(e.Id, !e.IsActive);
        Reload(e.Id);
    }
}
