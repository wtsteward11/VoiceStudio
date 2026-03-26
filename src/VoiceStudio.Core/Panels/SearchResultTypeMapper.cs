using System;

namespace VoiceStudio.Core.Panels;

/// <summary>
/// Maps backend result type strings to SearchResultType enum.
/// Centralizes mapping from result type to panel/action.
/// </summary>
public static class SearchResultTypeMapper
{
    public static SearchResultType FromString(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return SearchResultType.Unknown;

        return type.ToLowerInvariant() switch
        {
            "profile" => SearchResultType.Profile,
            "project" => SearchResultType.Project,
            "audio" => SearchResultType.Audio,
            "project_audio" => SearchResultType.ProjectAudio,
            "marker" => SearchResultType.Marker,
            "script" => SearchResultType.Script,
            _ => SearchResultType.Unknown,
        };
    }

    /// <summary>
    /// Returns the string form for INavigatablePanel.NavigateToItemAsync (backward compat).
    /// </summary>
    public static string ToResultTypeString(SearchResultType type)
    {
        return type switch
        {
            SearchResultType.Profile => "profile",
            SearchResultType.Project => "project",
            SearchResultType.Audio => "audio",
            SearchResultType.ProjectAudio => "project_audio",
            SearchResultType.Marker => "marker",
            SearchResultType.Script => "script",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Result type string passed to <see cref="INavigatablePanel.NavigateToItemAsync"/>.
    /// Known API types map to canonical strings; unlisted backend types pass through lowercased so panels can still branch.
    /// </summary>
    public static string ToPanelResultTypeString(string? rawTypeFromApi)
    {
        var mapped = FromString(rawTypeFromApi);
        if (mapped != SearchResultType.Unknown)
            return ToResultTypeString(mapped);
        if (string.IsNullOrWhiteSpace(rawTypeFromApi))
            return string.Empty;
        return rawTypeFromApi.Trim().ToLowerInvariant();
    }
}
