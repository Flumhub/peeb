using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class AddAliasCommand : ICommandHandler
    {
        public string Command => "addalias";
        public string Description => "Add aliases to a reaction";
        public string Usage => "addalias [reaction_name] [alias1] [alias2] ...";

        private readonly Dictionary<string, string> _reactions;
        private readonly Dictionary<string, string> _aliases;
        private readonly FileService _fileService;

        public AddAliasCommand(Dictionary<string, string> reactions, Dictionary<string, string> aliases, FileService fileService)
        {
            _reactions = reactions;
            _aliases = aliases;
            _fileService = fileService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 3)
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`");
                return true;
            }

            string reactionName = args[1].ToLower();

            if (!_reactions.ContainsKey(reactionName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{reactionName}' not found!");
                return true;
            }

            var newAliases = args.Skip(2).Select(a => a.ToLower()).ToList();
            var addedAliases = new List<string>();
            var skippedAliases = new List<string>();

            foreach (var alias in newAliases)
            {
                if (_aliases.ContainsKey(alias) || _reactions.ContainsKey(alias))
                {
                    skippedAliases.Add(alias);
                }
                else
                {
                    _aliases[alias] = reactionName;
                    addedAliases.Add(alias);
                }
            }

            if (addedAliases.Count > 0)
            {
                await _fileService.SaveJsonAsync("aliases.json", _aliases);
                await message.Channel.SendMessageAsync($"✅ Added aliases for '{reactionName}': {string.Join(", ", addedAliases)}");
            }

            if (skippedAliases.Count > 0)
            {
                await message.Channel.SendMessageAsync($"⚠️ Skipped existing aliases: {string.Join(", ", skippedAliases)}");
            }

            Console.WriteLine($"[DEBUG] Added aliases for {reactionName}: {string.Join(", ", addedAliases)}");
            return true;
        }
    }
}