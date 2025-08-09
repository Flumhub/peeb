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

            // Group reactions to avoid exceeding 25 field limit
            var reactionList = new List<string>();
            
            foreach (var reaction in _reactions)
            {
                var aliases = _aliases.Where(kvp => kvp.Value == reaction.Key).Select(kvp => kvp.Key).ToList();
                string aliasText = aliases.Count > 0 ? $" (aliases: {string.Join(", ", aliases)})" : "";
                reactionList.Add($"`{reaction.Key}`{aliasText}");
            }

            // Split reactions into chunks to fit in fields (max ~1024 chars per field)
            var chunks = new List<string>();
            var currentChunk = new List<string>();
            var currentLength = 0;

            foreach (var item in reactionList)
            {
                // If adding this item would exceed 1000 chars (leaving buffer), start new chunk
                if (currentLength + item.Length + 2 > 1000 && currentChunk.Count > 0)
                {
                    chunks.Add(string.Join("\n", currentChunk));
                    currentChunk.Clear();
                    currentLength = 0;
                }
                currentChunk.Add(item);
                currentLength += item.Length + 1; // +1 for newline
            }
            
            if (currentChunk.Count > 0)
            {
                chunks.Add(string.Join("\n", currentChunk));
            }

            // Add fields (max 25, but we shouldn't need that many)
            for (int i = 0; i < Math.Min(chunks.Count, 25); i++)
            {
                var fieldName = chunks.Count == 1 ? "Reactions" : $"Reactions (Part {i + 1})";
                embed.AddField(fieldName, chunks[i], false);
            }

            if (chunks.Count > 25)
            {
                embed.WithFooter($"Too many reactions to display all. Showing first {25 * (1000/50)} reactions.");
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
            return true;
        }
    }
}