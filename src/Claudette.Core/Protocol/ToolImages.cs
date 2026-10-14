using System.Globalization;
using System.Text.Json.Nodes;

namespace Claudette.Core.Protocol;

/// <summary>An image's size in pixels.</summary>
public readonly record struct ImageSize(int Width, int Height)
{
    /// <summary>"3000 × 1000".</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width} × {Height}");
}

/// <summary>An image in a tool's result, with what Claude Code says of it (DESIGN.md §18, "Images from Claude's tools").</summary>
/// <param name="Tool">The tool whose result it's in: <c>Read</c>, <c>mcp__playwright__browser_take_screenshot</c>.</param>
/// <param name="Name">
/// What it's called: the file's name for a Read of an image, <c>report.pdf, page 3</c> for a PDF's page,
/// <c>analysis.ipynb, figure 2</c> for a notebook's, else <c>Image</c> (<c>Image 2</c> of several).
/// </param>
/// <param name="FileName">A name to save it as, with an extension that fits its media type.</param>
/// <param name="FilePath">The image file a Read read it from, which can be opened; null for an image from anything else.</param>
/// <param name="ScaledFrom">Its size before Claude Code scaled it down to send it, when it did and says so.</param>
public sealed record ToolImage(MessageImage Image, string Tool, string Name, string FileName, string? FilePath, ImageSize? ScaledFrom);

/// <summary>Names the images in a tool's result, from the call's input and Claude Code's <c>tool_use_result</c>.</summary>
public static class ToolImages
{
    /// <summary>
    /// The images in <paramref name="result"/>, named. A Read says what it read in its <c>tool_use_result</c>'s
    /// <c>type</c>: <c>image</c> (with the sizes Claude Code scaled it between), <c>parts</c> (a PDF's pages, from
    /// <c>firstPage</c>) or <c>notebook</c>. Anything else (an MCP tool's screenshot, Bash output that's an image) is
    /// just an image.
    /// </summary>
    public static IReadOnlyList<ToolImage> From(string toolName, JsonObject input, ToolResultBlock result, JsonNode? toolUseResult)
    {
        var images = result.Images;
        if (images.Count == 0)
        {
            return [];
        }
        var details = toolName == "Read" ? toolUseResult as JsonObject : null;
        var path = input.GetString("file_path");
        var file = path is { Length: > 0 } ? Path.GetFileName(path) : null;
        var stem = file is null ? null : Path.GetFileNameWithoutExtension(file);
        return [.. images.Select((image, i) => (details?.GetString("type"), file) switch
        {
            ("image", { } name) when images.Count == 1 => new ToolImage(image, toolName, name, WithExtension(name, image.MediaType), path, ScaledFrom(details!)),
            ("parts", { } name) => Numbered(image, toolName, $"{name}, page", $"{stem}-page", FirstPage(details!, input) + i),
            ("notebook", { } name) => Numbered(image, toolName, $"{name}, figure", $"{stem}-figure", i + 1),
            _ when images.Count == 1 => new ToolImage(image, toolName, "Image", "image" + Extension(image.MediaType), null, null),
            _ => Numbered(image, toolName, "Image", "image", i + 1),
        })];
    }

    private static ToolImage Numbered(MessageImage image, string tool, string name, string fileStem, int number) =>
        new(image, tool, string.Create(CultureInfo.InvariantCulture, $"{name} {number}"),
            string.Create(CultureInfo.InvariantCulture, $"{fileStem}-{number}{Extension(image.MediaType)}"), null, null);

    /// <summary>The PDF page the first image is: <c>firstPage</c>, else the start of the call's <c>pages</c> ("3-5").</summary>
    private static int FirstPage(JsonObject details, JsonObject input)
    {
        if (details.GetLong("firstPage") is { } first and > 0)
        {
            return (int)first;
        }
        var pages = input.GetString("pages") ?? "";
        var digits = pages.TakeWhile(char.IsAsciiDigit).Count();
        return int.TryParse(pages.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var start) && start > 0 ? start : 1;
    }

    /// <summary>The image's size before Claude Code scaled it down, from <c>file.dimensions</c>; null when it wasn't.</summary>
    private static ImageSize? ScaledFrom(JsonObject details)
    {
        if (details.GetObject("file")?.GetObject("dimensions") is not { } dimensions
            || dimensions.GetLong("originalWidth") is not { } width || dimensions.GetLong("originalHeight") is not { } height)
        {
            return null;
        }
        var shown = (dimensions.GetLong("displayWidth"), dimensions.GetLong("displayHeight"));
        return shown == (width, height) || width <= 0 || height <= 0 ? null : new ImageSize((int)width, (int)height);
    }

    /// <summary>A name to save an image as: <paramref name="name"/> with an extension that fits its type, or <c>image.png</c>.</summary>
    public static string SaveName(string? name, string mediaType) =>
        name is { Length: > 0 } ? WithExtension(name, mediaType) : "image" + Extension(mediaType);

    /// <summary>The file's name, with its extension changed when it doesn't fit the media type (a PNG Claude Code sent as JPEG).</summary>
    private static string WithExtension(string name, string mediaType) =>
        Extensions(mediaType).Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            ? name
            : Path.GetFileNameWithoutExtension(name) + Extension(mediaType);

    private static string Extension(string mediaType) => Extensions(mediaType)[0];

    private static string[] Extensions(string mediaType) => mediaType switch
    {
        "image/jpeg" => [".jpg", ".jpeg"],
        "image/gif" => [".gif"],
        "image/webp" => [".webp"],
        _ => [".png"],
    };
}
