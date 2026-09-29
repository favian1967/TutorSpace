using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using TutorSpace.Services;

namespace TutorSpace.UI.Controls;

/// <summary>
/// «Записать» / «Остановить» from the microphone, or «Загрузить файл…».
/// Calls <see cref="Recorded"/> with a file path and its duration in seconds.
/// </summary>
public class RecorderPanel : WrapPanel
{
    private readonly AudioRecorder _recorder = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Button _record;
    private readonly TextBlock _status = Ui.Text("", "Muted", margin: new Thickness(4, 10, 0, 0));
    private readonly int _maxSeconds;

    public Action<string, int>? Recorded { get; set; }

    /// <summary>A WAV longer than this would not fit into the 15 MB upload limit.</summary>
    public const int DefaultMaxSeconds = 300;

    public RecorderPanel(int maxSeconds = DefaultMaxSeconds, bool allowFile = true)
    {
        _maxSeconds = maxSeconds;
        _record = Ui.Button("Записать", Toggle, icon: Icons.Microphone);
        Children.Add(_record);
        if (allowFile) Children.Add(Ui.Button("Загрузить файл…", PickFile));
        Children.Add(_status);

        _timer.Tick += (_, _) =>
        {
            var seconds = (int)_recorder.Elapsed.TotalSeconds;
            _status.Text = $"Идёт запись… {seconds / 60}:{seconds % 60:00}" + (_maxSeconds > 0 ? $" / {_maxSeconds / 60}:{_maxSeconds % 60:00}" : "");
            if (_maxSeconds > 0 && seconds >= _maxSeconds) Ui.Try(Toggle);
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _recorder.Cancel();
        };
    }

    private void Toggle()
    {
        if (!_recorder.IsRecording)
        {
            AudioPlayer.Stop();
            _recorder.Start();
            _record.Content = Ui.ButtonContent("Остановить", Icons.Stop);
            _record.Style = (Style)FindResource("PrimaryButton");
            _timer.Start();
            return;
        }

        _timer.Stop();
        _record.Content = Ui.ButtonContent("Записать", Icons.Microphone);
        _record.Style = (Style)FindResource(typeof(Button));
        _status.Text = "";
        var path = MediaService.NewRecordingPath();
        var seconds = _recorder.Stop(path);
        try
        {
            Recorded?.Invoke(path, seconds);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private void PickFile()
    {
        var dialog = new OpenFileDialog { Filter = MediaService.AudioFilter, Title = "Выберите аудиофайл" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        Recorded?.Invoke(dialog.FileName, AudioPlayer.ProbeDuration(dialog.FileName));
    }
}
