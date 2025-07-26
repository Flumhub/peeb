using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class AddReactionCommand : ICommandHandler
    {
        public string Command => "addreaction";
        public string Description => "Save attached file as reaction";
        public string Usage => "addreaction [name] - attach a file to this message";

        private readonly Dictionary<string, string> _reactions;
        private readonly Dictionary<string, string> _aliases;
        private readonly FileService _fileService;
        private readonly string _reactionsFolder;

        public AddReactionCommand(Dictionary<string, string> reactions, Dictionary<string, string> aliases, 
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

            if (message.Attachments.Count == 0)
            {
                await message.Channel.SendMessageAsync("Please attach a file to save as a reaction!");
                return true;
            }

            var attachment = message.Attachments.First();
            string fileExtension = Path.GetExtension(attachment.Filename);
            string savedFileName = $"{reactionName}{fileExtension}";
            string filePath = Path.Combine(_reactionsFolder, savedFileName);

            try
            {
                var fileData = await _fileService.DownloadFileAsync(attachment.Url);
                await File.WriteAllBytesAsync(filePath, fileData);

                _reactions[reactionName] = savedFileName;
                await _fileService.SaveJsonAsync("reactions.json", _reactions);

                await message.Channel.SendMessageAsync($"✅ Reaction '{reactionName}' saved! Type `{reactionName}` to use it.");
                Console.WriteLine($"[DEBUG] Saved reaction: {reactionName} -> {savedFileName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to save reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to save reaction file!");
                return true;
            }
        }
    }
}