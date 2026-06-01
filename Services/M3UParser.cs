using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>
/// Tolerant extended-M3U parser. By contract it NEVER throws on bad input:
/// unrecognised or broken lines are skipped, and a channel is only emitted
/// once it has both a name and a stream URL.
/// </summary>
public static class M3UParser
{
    // key="value" attribute pairs inside an #EXTINF line.
    private static readonly Regex AttrRegex =
        new(@"([A-Za-z0-9_-]+)\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    // #EXTINF:<duration><attrs>,<display-name>
    private static readonly Regex ExtInfRegex =
        new(@"^#EXTINF:\s*(?<dur>-?\d+(\.\d+)?)?(?<attrs>[^,]*),(?<name>.*)$",
            RegexOptions.Compiled);

    public static Playlist Parse(string content, string? source = null)
        => Parse(SplitLines(content), source);

    public static Playlist Parse(IEnumerable<string> lines, string? source = null)
    {
        var playlist = new Playlist { Source = source };
        Channel? pending = null;
        string? extGrp = null;

        foreach (var raw in lines)
        {
            if (raw is null) continue;
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // Header — ignore.
            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                continue;

            // Channel metadata line — starts a new pending channel.
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pending = ParseExtInf(line);
                extGrp = null;
                continue;
            }

            // Optional group directive that overrides group-title.
            if (line.StartsWith("#EXTGRP", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf(':');
                if (idx >= 0 && idx < line.Length - 1)
                    extGrp = line[(idx + 1)..].Trim();
                continue;
            }

            // Any other directive/comment we don't consume.
            if (line.StartsWith("#"))
                continue;

            // A non-comment line is the URL for the current pending channel.
            if (pending is null)
                continue; // URL without a preceding #EXTINF -> malformed, skip.

            pending.StreamUrl = line;
            if (!string.IsNullOrWhiteSpace(extGrp))
                pending.Group = extGrp!;

            if (!string.IsNullOrWhiteSpace(pending.StreamUrl) &&
                !string.IsNullOrWhiteSpace(pending.Name))
            {
                playlist.Channels.Add(pending);
            }

            pending = null;
            extGrp = null;
        }

        return playlist;
    }

    private static Channel ParseExtInf(string line)
    {
        var channel = new Channel();

        var m = ExtInfRegex.Match(line);
        string attrsPart;

        if (m.Success)
        {
            channel.Name = m.Groups["name"].Value.Trim();
            attrsPart = m.Groups["attrs"].Value;
        }
        else
        {
            // Fallback for #EXTINF lines that don't match cleanly:
            // take everything after the first comma as the display name.
            var comma = line.IndexOf(',');
            channel.Name = comma >= 0 ? line[(comma + 1)..].Trim() : "Unknown";
            attrsPart = comma >= 0 ? line[..comma] : line;
        }

        foreach (Match attr in AttrRegex.Matches(attrsPart))
        {
            var key = attr.Groups[1].Value.ToLowerInvariant();
            var val = attr.Groups[2].Value.Trim();
            switch (key)
            {
                case "tvg-id":      channel.TvgId = val; break;
                case "tvg-logo":    channel.LogoUrl = string.IsNullOrWhiteSpace(val) ? null : val; break;
                case "group-title": if (!string.IsNullOrWhiteSpace(val)) channel.Group = val; break;
                case "tvg-name":    if (string.IsNullOrWhiteSpace(channel.Name)) channel.Name = val; break;
            }
        }

        if (string.IsNullOrWhiteSpace(channel.Name))
            channel.Name = "Unknown";

        return channel;
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        using var reader = new StringReader(content);
        string? l;
        while ((l = reader.ReadLine()) is not null)
            yield return l;
    }
}
