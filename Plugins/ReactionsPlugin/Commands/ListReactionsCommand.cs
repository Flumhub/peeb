using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Interfaces;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class ListReactionsCommand : ICommandHandler
    {
        public string Command => "reactions";
        public string Description => "List all saved reactions";
        public string Usage => "reactions";

        private readonly Dictionary<string, string> _reactions;
        private readonly Dictionary<string, string> _aliases;

        public ListReactionsCommand(Dictionary<string, string> reactions, Dictionary<string, string> aliases)
        {
            _reactions = reactions;
            _aliases = aliases;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (_reactions.Count == 0)
            {
                await message.Channel.SendMessageAsync("No saved reactions found!");
                return true;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Saved Reactions")
                .WithDescription($"Found {_reactions.Count} saved reactions:")
                .WithColor(Color.Purple)
                .WithCurrentTimestamp();

            foreach (var reaction in _reactions)
            {
                var aliases = _aliases.Where(kvp => kvp.Value == reaction.Key).Select(kvp => kvp.Key).ToList();
                string aliasText = aliases.Count > 0 ? $" (Aliases: {string.Join(", ", aliases)})" : "";
                embed.AddField($"`{reaction.Key}`", $"File: {reaction.Value}{aliasText}", true);
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
            return true;
        }
    }
}