using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class RemoveAliasCommand : ICommandHandler
    {
        public string Command => "removealias";
        public string Description => "Remove an alias";
        public string Usage => "removealias [alias_name]";

        private readonly Dictionary<string, string> _aliases;
        private readonly FileService _fileService;

        public RemoveAliasCommand(Dictionary<string, string> aliases, FileService fileService)
        {
            _aliases = aliases;
            _fileService = fileService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`");
                return true;
            }

            string aliasName = args[1].ToLower();

            if (!_aliases.ContainsKey(aliasName))
            {
                await message.Channel.SendMessageAsync($"Alias '{aliasName}' not found!");
                return true;
            }

            try
            {
                string reactionName = _aliases[aliasName];
                _aliases.Remove(aliasName);
                await _fileService.SaveJsonAsync("aliases.json", _aliases);

                await message.Channel.SendMessageAsync($"✅ Alias '{aliasName}' removed from reaction '{reactionName}'!");
                Console.WriteLine($"[DEBUG] Removed alias: {aliasName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to remove alias: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to remove alias!");
                return true;
            }
        }
    }
}