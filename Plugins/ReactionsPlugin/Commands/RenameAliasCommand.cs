using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class RenameAliasCommand : ICommandHandler
    {
        public string Command => "renamealias";
        public string Description => "Rename an alias";
        public string Usage => "renamealias [old_alias] [new_alias]";

        private readonly Dictionary<string, string> _aliases;
        private readonly FileService _fileService;

        public RenameAliasCommand(Dictionary<string, string> aliases, FileService fileService)
        {
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

            string oldAlias = args[1].ToLower();
            string newAlias = args[2].ToLower();

            if (!_aliases.ContainsKey(oldAlias))
            {
                await message.Channel.SendMessageAsync($"Alias '{oldAlias}' not found!");
                return true;
            }

            if (_aliases.ContainsKey(newAlias))
            {
                await message.Channel.SendMessageAsync($"Alias '{newAlias}' already exists!");
                return true;
            }

            try
            {
                string reactionName = _aliases[oldAlias];
                _aliases.Remove(oldAlias);
                _aliases[newAlias] = reactionName;
                await _fileService.SaveJsonAsync("aliases.json", _aliases);

                await message.Channel.SendMessageAsync($"✅ Alias renamed from '{oldAlias}' to '{newAlias}'!");
                Console.WriteLine($"[DEBUG] Renamed alias: {oldAlias} -> {newAlias}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to rename alias: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to rename alias!");
                return true;
            }
        }
    }
}