using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class DeleteReactionCommand : ICommandHandler
    {
        public string Command => "deletereaction";
        public string Description => "Delete saved reaction";
        public string Usage => "deletereaction [name]";

        private readonly Dictionary<string, string> _reactions;
        private readonly Dictionary<string, string> _aliases;
        private readonly FileService _fileService;
        private readonly string _reactionsFolder;

        public DeleteReactionCommand(Dictionary<string, string> reactions, Dictionary<string, string> aliases,
            FileService fileService, string reactionsFolder)
        {
            _reactions = reactions;
            _aliases = aliases;
            _fileService = fileService;
            _reactionsFolder = reactionsFolder;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2)
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

            try
            {
                string filePath = Path.Combine(_reactionsFolder, _reactions[reactionName]);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Remove all aliases that point to this reaction
                var aliasesToRemove = _aliases.Where(kvp => kvp.Value == reactionName).Select(kvp => kvp.Key).ToList();
                foreach (var alias in aliasesToRemove)
                {
                    _aliases.Remove(alias);
                }

                _reactions.Remove(reactionName);
                await _fileService.SaveJsonAsync("reactions.json", _reactions);
                await _fileService.SaveJsonAsync("aliases.json", _aliases);

                await message.Channel.SendMessageAsync($"✅ Reaction '{reactionName}' deleted!");
                Console.WriteLine($"[DEBUG] Deleted reaction: {reactionName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to delete reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to delete reaction!");
                return true;
            }
        }
    }
}