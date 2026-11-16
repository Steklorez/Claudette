using System.Globalization;
using System.Text;
using Deventry.Core.Protocol;

namespace Deventry.Core.Remote;

/// <summary>
/// A file sent to a tab from a remote client (DESIGN.md §18, "Remote clients", "Attachments from a client"): an image,
/// a text or log file, a PDF, as the service gave it, by its name.
/// </summary>
public sealed record RemoteAttachment(string Name, byte[] Data)
{
    /// <summary>The largest a file can be: what Telegram lets a bot download, and what the composer takes as an image.</summary>
    public const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>The most files a message takes, as the composer takes images.</summary>
    public const int MostPerMessage = 20;
}

/// <summary>
/// What a tab sends of a client's attachments (DESIGN.md §18, "Remote clients", "Attachments from a client"): images in
/// the message, as the composer sends them; a text file in it as a large paste is; anything else Claude Code reads saved
/// in the project's <c>.deventry/attachments</c> and mentioned with <c>@path</c>; and why any was refused.
/// </summary>
public sealed record PreparedAttachments(IReadOnlyList<MessageImage> Images, IReadOnlyList<string> Pastes, IReadOnlyList<string> Mentions, IReadOnlyList<string> Refused)
{
    public static readonly PreparedAttachments None = new([], [], [], []);

    /// <summary>Nothing goes with the message: every file was refused.</summary>
    public bool IsEmpty => Images.Count == 0 && Pastes.Count == 0 && Mentions.Count == 0;
}

/// <summary>
/// A client's attachments made ready for a tab, and the project's <c>.deventry/attachments</c> folder they're saved
/// in: git ignores it by a <c>.gitignore</c> of its own, and what's older than Settings keeps is deleted.
/// </summary>
public static class RemoteAttachments
{
    /// <summary>Deventry's folder in a project, beside <c>deventry.json</c>.</summary>
    public const string DeventryFolder = ".deventry";

    public const string Subfolder = "attachments";

    /// <summary>A text file up to this size goes in the message; a larger one, a long log, is saved for Claude to read what it needs.</summary>
    public const long InlineTextLimit = 100 * 1024;

    /// <summary>How much of a file is read to tell text from binary, as the composer does.</summary>
    private const int SniffBytes = 8192;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Where a project's attachments are saved.</summary>
    public static string FolderOf(string projectFolder) => Path.Combine(projectFolder, DeventryFolder, Subfolder);

    /// <summary>
    /// Makes <paramref name="attachments"/> ready to go with a message in a tab working in <paramref name="projectFolder"/>,
    /// saving what's saved under names that start with <paramref name="now"/>, and deleting the saved files older than
    /// <paramref name="keepFor"/> (null keeps them). Reads and writes files: keep it off the UI thread.
    /// </summary>
    public static PreparedAttachments Prepare(IReadOnlyList<RemoteAttachment> attachments, string projectFolder, DateTimeOffset now, TimeSpan? keepFor)
    {
        var images = new List<MessageImage>();
        var pastes = new List<string>();
        var mentions = new List<string>();
        var refused = new List<string>();
        foreach (var attachment in attachments)
        {
            if (MessageImage.DetectMediaType(attachment.Data) is { } mediaType)
            {
                if (images.Count < RemoteAttachment.MostPerMessage)
                {
                    images.Add(new MessageImage(mediaType, attachment.Data));
                }
                else
                {
                    refused.Add($"{attachment.Name}: a message can have up to {RemoteAttachment.MostPerMessage} images.");
                }
                continue;
            }
            // A PDF is read by Claude Code as a document, however much of it is text.
            var text = IsPdf(attachment.Data) ? null : TextOf(attachment.Data);
            if (text is not null && attachment.Data.LongLength <= InlineTextLimit)
            {
                pastes.Add($"Attached file {attachment.Name}:\n\n{text}");
                continue;
            }
            if (text is not null || IsPdf(attachment.Data))
            {
                mentions.Add(Save(attachment, projectFolder, now));
                continue;
            }
            refused.Add($"{attachment.Name}: Claude can't read it. Images, text files and PDFs can be sent.");
        }
        if (mentions.Count > 0)
        {
            Prune(projectFolder, keepFor, now);
        }
        return new PreparedAttachments(images, pastes, mentions, refused);
    }

    /// <summary>
    /// Deletes a project's saved attachments older than <paramref name="keepFor"/>, by when they were saved; null keeps
    /// them all. One that can't be deleted now is left for next time.
    /// </summary>
    public static void Prune(string projectFolder, TimeSpan? keepFor, DateTimeOffset now)
    {
        var folder = FolderOf(projectFolder);
        if (keepFor is not { } keep || !Directory.Exists(folder))
        {
            return;
        }
        var before = (now - keep).UtcDateTime;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < before)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Open in something, or read-only: next time.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder went, or can't be listed: nothing to do now.
        }
    }

    /// <summary>
    /// Saves a file in the project's attachments folder, git ignoring the folder, and gives its <c>@path</c> from the
    /// project: <c>@.deventry/attachments/20261116-143205-report.pdf</c>.
    /// </summary>
    private static string Save(RemoteAttachment attachment, string projectFolder, DateTimeOffset now)
    {
        var deventry = Path.Combine(projectFolder, DeventryFolder);
        var folder = FolderOf(projectFolder);
        Directory.CreateDirectory(folder);
        // Kept out of git by its own .gitignore, so the project's ignore files are left alone.
        var ignore = Path.Combine(deventry, ".gitignore");
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, "# Deventry's own files for this project, kept out of git.\n*\n");
        }
        var stem = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + SafeName(attachment.Name);
        var name = stem;
        for (var n = 2; File.Exists(Path.Combine(folder, name)); n++)
        {
            name = $"{Path.GetFileNameWithoutExtension(stem)}-{n.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(stem)}";
        }
        File.WriteAllBytes(Path.Combine(folder, name), attachment.Data);
        return $"@{DeventryFolder}/{Subfolder}/{name}";
    }

    /// <summary>A file's name as a mention takes it: no spaces, nothing a file system refuses.</summary>
    internal static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|', '@']).ToHashSet();
        var safe = new string([.. Path.GetFileName(name).Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)]).Trim('-', '.');
        return safe.Length > 0 ? safe : "file";
    }

    /// <summary>The file as text, when it's text: UTF-8 with no zero byte in its start. Null for anything else.</summary>
    private static string? TextOf(byte[] data)
    {
        if (Array.IndexOf(data, (byte)0, 0, Math.Min(data.Length, SniffBytes)) >= 0)
        {
            return null;
        }
        try
        {
            var text = StrictUtf8.GetString(data);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsPdf(byte[] data) => data.Length >= 5 && data.AsSpan(0, 5).SequenceEqual("%PDF-"u8);
}
