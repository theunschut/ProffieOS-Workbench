using MudBlazor;

namespace ProffieOS.Workbench.Layout;

public partial class MainLayout
{
    private readonly MudTheme _theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Black = "#000000",
            Background = "#000000",
            BackgroundGray = "#0a0a2a",
            Surface = "#101040",
            DrawerBackground = "#0a0a2a",
            AppbarBackground = "#0a0a2a",
            AppbarText = "#add8e6",
            Primary = "#6060aa",
            PrimaryContrastText = "#ffffff",
            Secondary = "#4040aa",
            TextPrimary = "#add8e6",
            TextSecondary = "rgba(173,216,230,0.7)",
            ActionDefault = "#add8e6",
            LinesDefault = "#6060aa",
            TableLines = "#6060aa",
            Divider = "#303070",
            OverlayLight = "rgba(0,0,0,0.6)",
            Error = "#cf6679",
            Warning = "#c8a227",
            Success = "#5a9e6f",
            Info = "#5a8dd9"
        }
    };
}