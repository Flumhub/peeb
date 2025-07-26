using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class RenameReactionCommand : ICommandHandler
    {
        public string Command => "renamereaction";
        public string Description => "Rename an existing reaction";
        public string Usage => "renamereaction [old_name] [new_name]";

        private readonly Dictionary<string, string> _reactions;
        private readonly Dictionary<string, string> _aliases;
        private readonly FileService _fileService;

        public RenameReactionCommand(Dictionary<string, string> reactions, Dictionary<string, string> aliases, FileService fileService)
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

            string oldName = args[1].ToLower();
            string newName = args[2].ToLower();

            if (!_reactions.ContainsKey(oldName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{oldName}' not found!");
                return true;
            }

            if (_reactions.ContainsKey(newName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{newName}' already exists!");
                return true;
            }

            try
            {
                string fileName = _reactions[oldName];
                _reactions.Remove(oldName);
                _reactions[newName] = fileName;

                // Update aliases that point to the old name
                var aliasesToUpdate = _aliases.Where(kvp => kvp.Value == oldName).Select(kvp => kvp.Key).ToList();
                foreach (var alias in aliasesToUpdate)
                {
                    _aliases[alias] = newName;
                }

                await _fileService.SaveJsonAsync("reactions.json", _reactions);
                await _fileService.SaveJsonAsync("aliases.json", _aliases);

                await message.Channel.SendMessageAsync($"✅ Reaction renamed from '{oldName}' to '{newName}'!");
                Console.WriteLine($"[DEBUG] Renamed reaction: {oldName} -> {newName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to rename reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to rename reaction!");
                return true;
            }
        }
    }
}