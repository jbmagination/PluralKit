using System.Text.RegularExpressions;

using Myriad.Types;

using PluralKit.Core;

namespace PluralKit.Bot;

public static class ContextArgumentsExt
{
    public static string PopArgument(this Context ctx) =>
        ctx.Parameters.Pop();

    public static string PeekArgument(this Context ctx) =>
        ctx.Parameters.Peek();

    public static string RemainderOrNull(this Context ctx, bool skipFlags = true) =>
        ctx.Parameters.Remainder(skipFlags).Length == 0 ? null : ctx.Parameters.Remainder(skipFlags);

    public static bool HasNext(this Context ctx, bool skipFlags = true) =>
        ctx.RemainderOrNull(skipFlags) != null;

    public static string FullCommand(this Context ctx) =>
        ctx.Parameters.FullCommand;

    /// <summary>
    ///     Checks if the next parameter is equal to one of the given keywords and pops it from the stack. Case-insensitive.
    /// </summary>
    public static bool Match(this Context ctx, ref string used, params string[] potentialMatches)
    {
        var arg = ctx.PeekArgument();
        foreach (var match in potentialMatches)
            if (arg.Equals(match, StringComparison.InvariantCultureIgnoreCase))
            {
                used = ctx.PopArgument();
                return true;
            }

        return false;
    }

    /// <summary>
    /// Checks if the next parameter is equal to one of the given keywords. Case-insensitive.
    /// </summary>
    public static bool Match(this Context ctx, params string[] potentialMatches)
    {
        string used = null; // Unused and unreturned, we just yeet it
        return ctx.Match(ref used, potentialMatches);
    }

    /// <summary>
    ///     Checks if the next parameter (starting from `ptr`) is equal to one of the given keywords, and leaves it on the stack. Case-insensitive.
    /// </summary>
    public static bool PeekMatch(this Context ctx, ref int ptr, string[] potentialMatches)
    {
        var arg = ctx.Parameters.PeekWithPtr(ref ptr);
        foreach (var match in potentialMatches)
            if (arg.Equals(match, StringComparison.InvariantCultureIgnoreCase))
                return true;

        return false;
    }

    /// <summary>
    /// Matches the next *n* parameters against each parameter consecutively.
    /// <br />
    /// Note that this is handled differently than single-parameter Match:
    /// each method parameter is an array of potential matches for the *n*th command string parameter.
    /// </summary>
    public static bool MatchMultiple(this Context ctx, params string[][] potentialParametersMatches)
    {
        int ptr = ctx.Parameters._ptr;

        foreach (var param in potentialParametersMatches)
            if (!ctx.PeekMatch(ref ptr, param)) return false;

        ctx.Parameters._ptr = ptr;

        return true;
    }

    public static bool MatchFlag(this Context ctx, params string[] potentialMatches)
    {
        // Flags are *ALWAYS PARSED LOWERCASE*. This means we skip out on a "ToLower" call here.
        // Can assume the caller array only contains lowercase *and* the set below only contains lowercase

        var flags = ctx.Parameters.Flags();
        return potentialMatches.Any(potentialMatch => flags.Contains(potentialMatch));
    }

    public static async Task<bool> MatchClear(this Context ctx, string toClear = null)
    {
        var matched = ctx.Match("clear", "reset") || ctx.MatchFlag("c", "clear");
        if (matched && toClear != null)
            return await ctx.ConfirmClear(toClear);
        return matched;
    }

    public static bool MatchRaw(this Context ctx) =>
        ctx.Match("r", "raw") || ctx.MatchFlag("r", "raw");

    public static bool MatchToggle(this Context ctx)
    {
        var yesToggles = new[] { "yes", "on", "enable", "enabled", "true" };
        var noToggles = new[] { "no", "off", "disable", "disabled", "false" };

        if (ctx.Match(yesToggles) || ctx.MatchFlag(yesToggles))
            return true;
        else if (ctx.Match(noToggles) || ctx.MatchFlag(noToggles))
            return false;
        else
            throw new PKError("You must pass either \"on\" or \"off\" to this command.");
    }

    public static (ulong? messageId, ulong? channelId) MatchMessage(this Context ctx, bool parseRawMessageId)
    {
        if (ctx.Message.Type == Message.MessageType.Reply && ctx.Message.MessageReference?.MessageId != null)
            return (ctx.Message.MessageReference.MessageId, ctx.Message.MessageReference.ChannelId);

        var word = ctx.PeekArgument();
        if (word == null)
            return (null, null);

        if (parseRawMessageId && ulong.TryParse(word, out var mid))
            return (mid, null);

        var match = Regex.Match(word, "https://(?:\\w+.)?discord(?:app)?.com/channels/\\d+/(\\d+)/(\\d+)");
        if (!match.Success)
            return (null, null);

        var channelId = ulong.Parse(match.Groups[1].Value);
        var messageId = ulong.Parse(match.Groups[2].Value);
        ctx.PopArgument();
        return (messageId, channelId);
    }

    public static async Task<List<PKMember>> ParseMemberList(this Context ctx, SystemId? restrictToSystem)
    {
        var members = new List<PKMember>();

        // Loop through all the given arguments
        while (ctx.HasNext())
        {
            // and attempt to match a member
            var member = await ctx.MatchMember(restrictToSystem);

            if (member == null)
                // if we can't, big error. Every member name must be valid.
                throw new PKError(ctx.CreateNotFoundError("Member", ctx.PopArgument()));

            members.Add(member); // Then add to the final output list
        }

        if (members.Count == 0) throw new PKSyntaxError("You must input at least one member.");

        return members;
    }

    public static async Task<List<PKGroup>> ParseGroupList(this Context ctx, SystemId? restrictToSystem)
    {
        var groups = new List<PKGroup>();

        // Loop through all the given arguments
        while (ctx.HasNext())
        {
            // and attempt to match a group
            var group = await ctx.MatchGroup(restrictToSystem);
            if (group == null)
                // if we can't, big error. Every group name must be valid.
                throw new PKError(ctx.CreateNotFoundError("Group", ctx.PopArgument()));

            // todo: remove this, the database query enforces the restriction
            if (restrictToSystem != null && group.System != restrictToSystem)
                throw Errors.NotOwnGroupError; // TODO: name *which* group?

            groups.Add(group); // Then add to the final output list
        }

        if (groups.Count == 0) throw new PKSyntaxError("You must input at least one group.");

        return groups;
    }
}