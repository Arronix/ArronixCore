namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Defines the path specification for organizing media in a library.
/// Contains templates and rules for folder structure.
/// </summary>
public record LibraryPathSpec(
    string RootPath,
    string FolderTemplate,
    bool CreateSubfolders = true,
    IReadOnlyDictionary<string, string>? CustomTokens = null);
