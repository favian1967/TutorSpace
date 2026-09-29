using System.Windows.Controls;
using TutorSpace.Data;

namespace TutorSpace.UI;

/// <summary>Icon and colour for each exercise kind, so the same kind looks the same everywhere.</summary>
public static class KindStyle
{
    public static (string Icon, string Background, string Foreground) Of(ExerciseKind kind) => kind switch
    {
        ExerciseKind.GapFill => (Icons.Text, "PrimarySoft", "Primary"),
        ExerciseKind.OpenAnswer => (Icons.List, "PrimarySoft", "Primary"),
        ExerciseKind.Theses => (Icons.Video, "DangerSoft", "Danger"),
        ExerciseKind.Speaking => (Icons.Audio, "SuccessSoft", "Success"),
        _ => (Icons.Microphone, "VioletSoft", "Violet"),
    };

    public static Border Badge(ExerciseKind kind, double size = 36)
    {
        var (icon, background, foreground) = Of(kind);
        return Ui.IconBadge(icon, Ui.Res(background), Ui.Res(foreground), size);
    }
}
