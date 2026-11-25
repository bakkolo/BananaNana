using BananaNana.Desktop;
using Xunit;

namespace BananaNana.Desktop.Tests;

// The kdialog and zenity fallback: the arguments it is started with and how its exit code and output are read.
public class FilePickerTests
{
    // Collection expressions need a parameter type to convert to, so the expected lists go through this.
    static void Same(string[] expected, IEnumerable<string> actual) => Assert.Equal(expected, actual);

    static readonly PickRequest Images = new("Choose a picture", "Pictures", ["*.png", "*.jpg"], "/home/u/Pictures");
    static readonly PickRequest Export = new("Export", "Library", ["*.json"], "/home/u/Documents", "BananaNana.library.json");

    [Fact]
    public void Kdialog_open_takes_the_start_folder_and_a_kde_style_filter()
    {
        var args = FilePicker.KdialogArguments(Images, save: false);

        Same(["--title", "Choose a picture", "--getopenfilename", "/home/u/Pictures", "Pictures (*.png *.jpg)"], args);
    }

    [Fact]
    public void Kdialog_save_starts_at_the_folder_and_the_suggested_name()
    {
        var args = FilePicker.KdialogArguments(Export, save: true);

        Same(["--title", "Export", "--getsavefilename", "/home/u/Documents/BananaNana.library.json", "Library (*.json)"], args);
    }

    [Fact]
    public void Zenity_open_and_save_use_its_own_options()
    {
        Same(
            ["--file-selection", "--title=Choose a picture", "--file-filter=Pictures | *.png *.jpg", "--filename=/home/u/Pictures/"],
            FilePicker.ZenityArguments(Images, save: false));
        Same(
            ["--file-selection", "--title=Export", "--file-filter=Library | *.json", "--save", "--confirm-overwrite",
                "--filename=/home/u/Documents/BananaNana.library.json"],
            FilePicker.ZenityArguments(Export, save: true));
    }

    [Fact]
    public void A_path_on_stdout_with_exit_0_is_a_pick()
    {
        var result = FilePicker.Interpret(0, "/home/u/Pictures/shot.png\n", "");

        Assert.Equal(PickStatus.Picked, result.Status);
        Assert.Equal("/home/u/Pictures/shot.png", result.Path);
    }

    [Fact]
    public void Windows_line_endings_and_extra_lines_give_the_first_path_only()
    {
        var result = FilePicker.Interpret(0, "/a b/c.json\r\n/other.json\r\n", "");

        Assert.Equal("/a b/c.json", result.Path);
    }

    [Fact]
    public void Exit_1_with_nothing_on_stderr_is_a_cancel()
    {
        Assert.Equal(PickStatus.Cancelled, FilePicker.Interpret(1, "", "").Status);
    }

    [Fact]
    public void Exit_1_with_an_error_message_is_a_failure_not_a_cancel()
    {
        Assert.Equal(PickStatus.Unavailable, FilePicker.Interpret(1, "", "Access denied").Status);
    }

    [Fact]
    public void Exit_0_with_no_path_and_other_exit_codes_are_failures()
    {
        Assert.Equal(PickStatus.Unavailable, FilePicker.Interpret(0, "  \n", "").Status);
        Assert.Equal(PickStatus.Unavailable, FilePicker.Interpret(5, "", "").Status);
        Assert.Equal(PickStatus.Unavailable, FilePicker.Interpret(null, "", "").Status);
    }

    [Fact]
    public void The_image_patterns_are_the_ones_the_filter_lists()
    {
        Same(["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"], FilePicker.ImagePatterns);
    }
}
