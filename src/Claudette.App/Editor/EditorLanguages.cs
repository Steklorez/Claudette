using TextMateSharp.Grammars;

namespace Claudette.App.Editor;

/// <summary>A file's language: the grammar that colors it, and the name the editor's chip shows.</summary>
/// <param name="Scope">The TextMate grammar's scope, or null for none (plain text).</param>
public sealed record EditorLanguage(string Name, string? Scope)
{
    public static EditorLanguage PlainText { get; } = new("Plain text", null);
}

/// <summary>
/// Which language a file is (DESIGN.md §18, "Project files", "The chip"): by its extension, as the diff view picks its
/// grammar, or for a file without one, by its first line's <c>#!</c>.
/// </summary>
public static class EditorLanguages
{
    private static readonly Lazy<RegistryOptions> Options = new(() => new RegistryOptions(ThemeName.DarkPlus));

    /// <summary>A <c>#!</c> program's language, by the extension its files usually have.</summary>
    private static readonly Dictionary<string, string> Interpreters = new(StringComparer.Ordinal)
    {
        ["sh"] = ".sh", ["bash"] = ".sh", ["zsh"] = ".sh", ["dash"] = ".sh", ["ksh"] = ".sh",
        ["python"] = ".py", ["python3"] = ".py", ["python2"] = ".py",
        ["node"] = ".js", ["deno"] = ".ts", ["bun"] = ".js",
        ["ruby"] = ".rb", ["perl"] = ".pl", ["php"] = ".php", ["pwsh"] = ".ps1", ["lua"] = ".lua",
    };

    /// <summary>The language of a file at <paramref name="path"/> whose first line is <paramref name="firstLine"/>.</summary>
    public static EditorLanguage For(string path, string? firstLine)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0 && firstLine is not null && Interpreter(firstLine) is { } program && Interpreters.TryGetValue(program, out var usual))
        {
            extension = usual;
        }
        return extension.Length == 0 ? EditorLanguage.PlainText : ForExtension(extension);
    }

    private static EditorLanguage ForExtension(string extension)
    {
        try
        {
            var options = Options.Value;
            if (options.GetScopeByExtension(extension) is not { Length: > 0 } scope)
            {
                return EditorLanguage.PlainText;
            }
            var language = options.GetLanguageByExtension(extension);
            var name = language?.Aliases is { Count: > 0 } aliases ? aliases[0] : language?.Id ?? extension.TrimStart('.');
            return new EditorLanguage(name, scope);
        }
        catch (Exception)
        {
            // A grammar index that can't be read leaves the file plain rather than failing to open it.
            return EditorLanguage.PlainText;
        }
    }

    /// <summary>The program a <c>#!</c> line runs: <c>bash</c> from <c>#!/bin/bash</c> or <c>#!/usr/bin/env bash</c>.</summary>
    private static string? Interpreter(string firstLine)
    {
        if (!firstLine.StartsWith("#!", StringComparison.Ordinal))
        {
            return null;
        }
        var words = firstLine[2..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }
        var program = Path.GetFileName(words[0]);
        if (program == "env")
        {
            program = words.Skip(1).FirstOrDefault(w => !w.StartsWith('-')) ?? "";
        }
        return program;
    }
}
