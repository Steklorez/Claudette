using Claudette.App.Conversation;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>Where an image in a reply has got to.</summary>
public enum ReplyImageState
{
    /// <summary>It waits for a click: a web image, or a file outside the tab's folder.</summary>
    Waiting,
    Loading,
    Shown,
    Failed,

    /// <summary>It never loads: a network path, another scheme, or not in a tab.</summary>
    Refused,
}

/// <summary>
/// An image in a reply's Markdown (DESIGN.md §18, "Images in Claude's replies"), in place of LiveMarkdown's own, which
/// loaded any address by itself. Until it may load, a placeholder says what it is and where it's from; then the image,
/// which opens full size on a click.
/// </summary>
public sealed partial class ReplyImageViewModel(string address, string altText) : ViewModelBase
{
    private ReplyImages? _images;
    private Action<ToolImage>? _open;

    /// <summary>The address as the reply has it.</summary>
    public string Address { get; } = address;

    /// <summary>The alt text as the reply has it.</summary>
    public string AltText { get; } = altText;

    /// <summary>What it's called: its alt text, else <c>Image</c>.</summary>
    public string Title { get; } = altText.Trim() is { Length: > 0 } alt ? alt : "Image";

    /// <summary>Where it points, once it's in a tab.</summary>
    public ReplyImageAddress? Source { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(CanLoad), nameof(Caption))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    public partial ReplyImageState State { get; private set; } = ReplyImageState.Waiting;

    [ObservableProperty]
    public partial MessageImage? Image { get; private set; }

    /// <summary>Why it didn't load, or never will.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    public partial string? Problem { get; private set; }

    public bool IsShown => State == ReplyImageState.Shown;

    public bool CanLoad => Source is not null && State is ReplyImageState.Waiting or ReplyImageState.Failed;

    /// <summary>The placeholder's second line: where it's from, or why it isn't showing.</summary>
    public string Caption => State switch
    {
        ReplyImageState.Waiting when Source is { Kind: ReplyImageKind.Web } web => $"Image from {web.Origin}, which loads only when you ask.",
        ReplyImageState.Waiting when Source is { } file => $"Image at {file.Origin}, outside the tab's folder.",
        ReplyImageState.Waiting => "",
        ReplyImageState.Loading => "Loading…",
        ReplyImageState.Failed => Problem ?? "Couldn't load it.",
        _ => Problem ?? "",
    };

    /// <summary><b>Load image</b> for a web image, <b>Show image</b> for a file.</summary>
    public string LoadText => Source?.Kind == ReplyImageKind.Web ? "Load image" : "Show image";

    /// <summary>The button's accessible name, with the address it loads.</summary>
    public string LoadName => $"{LoadText} from {Address}";

    /// <summary>
    /// It's shown in a tab, or not in one (null): where it points decides whether it loads now, waits for a click, or
    /// never loads. Only the first call counts.
    /// </summary>
    public void Attach(ReplyImages? images, Action<ToolImage>? open)
    {
        if (Source is not null || State != ReplyImageState.Waiting)
        {
            return;
        }
        if (images is null)
        {
            Refuse("Images in replies show in a tab's conversation.");
            return;
        }
        _images = images;
        _open = open;
        Source = images.Classify(Address);
        // The button asked before there was a source to load from.
        OnPropertyChanged(nameof(LoadText));
        OnPropertyChanged(nameof(LoadName));
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(Caption));
        LoadCommand.NotifyCanExecuteChanged();
        if (Source.Kind == ReplyImageKind.Refused)
        {
            Refuse(Source.Reason ?? "It doesn't load.");
        }
        else if (images.Loaded(Address) is { } image)
        {
            Show(image);
        }
        else if (Source.LoadsAtOnce || images.IsAllowed(Address))
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        if (_images is null || Source is null)
        {
            return;
        }
        State = ReplyImageState.Loading;
        try
        {
            Show(await _images.LoadAsync(Source));
        }
        catch (ReplyImageException ex)
        {
            Problem = ex.Message;
            State = ReplyImageState.Failed;
        }
    }

    /// <summary>A click on the image: full size in a window of its own, as an image from a tool's result is.</summary>
    [RelayCommand]
    private void Open()
    {
        if (Image is not { } image || Source is null)
        {
            return;
        }
        var name = Source.Kind switch
        {
            ReplyImageKind.Web => Path.GetFileName(Source.Url!.AbsolutePath),
            ReplyImageKind.InFolder or ReplyImageKind.LocalFile => Path.GetFileName(Source.Path),
            _ => null,
        };
        var filePath = Source.Kind is ReplyImageKind.InFolder or ReplyImageKind.LocalFile ? Source.Path : null;
        var from = Source.Kind == ReplyImageKind.Web ? Source.Origin : "In a reply";
        _open?.Invoke(new ToolImage(image, from, Title, ToolImages.SaveName(name, image.MediaType), filePath, null, Source.Url));
    }

    private void Show(MessageImage image)
    {
        Image = image;
        Problem = null;
        State = ReplyImageState.Shown;
    }

    private void Refuse(string reason)
    {
        Problem = reason;
        State = ReplyImageState.Refused;
    }
}
