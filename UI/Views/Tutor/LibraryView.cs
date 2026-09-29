using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Controls;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>The tutor's personal clip library: uploaded once, attached to any exercise.</summary>
public class LibraryView : UserControl
{
    private readonly StackPanel _list = new();

    public LibraryView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 32) };
        root.Children.Add(Ui.Text("Аудиотека", "H1"));
        root.Children.Add(Ui.Text("Отрезки из фильмов, образцы произношения и т. п. Файл загружается один раз и прикрепляется к любым упражнениям. " +
                                  "Расшифровка показывается ученику после попытки в упражнении «Повторить за оригиналом».", "Muted"));

        var add = new StackPanel();
        add.Children.Add(Ui.Text("Добавить", "H2"));
        add.Children.Add(Ui.Row(Ui.Button("Выбрать файл…", AddFile, "PrimaryButton")));
        add.Children.Add(Ui.Text("или записать с микрофона:", "Muted", margin: new Thickness(0, 8, 0, 0)));
        add.Children.Add(new RecorderPanel(allowFile: false) { Recorded = (path, seconds) => AddRecorded(path, seconds) });
        root.Children.Add(Ui.Card(add));

        root.Children.Add(_list);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Unloaded += (_, _) => AudioPlayer.Stop();
        Reload();
    }

    private void AddFile()
    {
        var dialog = new OpenFileDialog { Filter = MediaService.AudioFilter, Multiselect = true, Title = "Аудиофайлы для аудиотеки" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var file in dialog.FileNames)
            MediaService.Import(Session.User.Id, file, MediaSource.TutorClip, Path.GetFileNameWithoutExtension(file));
        Reload();
    }

    private void AddRecorded(string path, int seconds)
    {
        var asset = MediaService.Import(Session.User.Id, path, MediaSource.TutorClip, $"Запись {DateTime.Now:dd.MM HH:mm}", durationSeconds: seconds);
        Reload();
        Edit(asset);
    }

    private void Reload()
    {
        _list.Children.Clear();
        var clips = MediaService.LibraryOf(Session.User.Id);
        if (clips.Count == 0) _list.Children.Add(Ui.Text("Аудиотека пуста.", "Muted"));
        foreach (var clip in clips)
        {
            var info = new StackPanel();
            info.Children.Add(Ui.Text(clip.DisplayName, bold: true));
            var meta = $"{(clip.DurationSeconds > 0 ? ExerciseText.FormatTimecode(clip.DurationSeconds) + " · " : "")}{clip.SizeBytes / 1024} КБ · {clip.CreatedAt:dd.MM.yyyy}";
            if (clip.Origin.Length > 0) meta += " · " + clip.Origin;
            info.Children.Add(Ui.Text(meta, "Muted", size: 12));
            if (clip.Transcript.Length > 0) info.Children.Add(Ui.Text("«" + clip.Transcript + "»", size: 13));

            var buttons = Ui.Row(
                Ui.IconButton(Icons.Play, () => AudioPlayer.Play(clip), "Прослушать"),
                Ui.IconButton(Icons.Stop, AudioPlayer.Stop, "Стоп"),
                Ui.Button("Изменить", () => Edit(clip)),
                Ui.IconButton(Icons.Delete, () =>
                {
                    if (!Ui.Confirm($"Удалить «{clip.DisplayName}»? Упражнения, к которым оно прикреплено, останутся без аудио.")) return;
                    MediaService.Delete(clip.Id);
                    Reload();
                }, "Удалить", danger: true));
            buttons.VerticalAlignment = VerticalAlignment.Top;

            var row = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(info);
            _list.Children.Add(Ui.Card(row));
        }
    }

    private void Edit(MediaAsset clip)
    {
        var form = new FormDialog("Аудио", this);
        var title = form.AddText("Название", clip.Title);
        var transcript = form.AddText("Расшифровка (что звучит)", clip.Transcript, multiline: true);
        var origin = form.AddText("Источник (фильм, сцена…)", clip.Origin);
        form.OnSubmit = () => MediaService.Update(clip.Id, title.Text, transcript.Text, origin.Text);
        if (form.Run()) Reload();
    }
}
