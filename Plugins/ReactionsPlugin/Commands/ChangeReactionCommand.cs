using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.ReactionPlugin.Commands
{
    public class ChangeReactionCommand : ICommandHandler
    {
        public string Command => "changereaction";
        public string Description => "Replace existing reaction with new file";
        public string Usage => "changereaction [name] - attach a new file to replace the existing reaction";

        private readonly Dictionary<string, string> _reactions;
        private readonly FileService _fileService;
        private readonly string _reactionsFolder;

        public ChangeReactionCommand(Dictionary<string, string> reactions, FileService fileService, string reactionsFolder)
        {
            _reactions = reactions;
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

            if (message.Attachments.Count == 0)
            {
                await message.Channel.SendMessageAsync("Please attach a file to replace the existing reaction!");
                return true;
            }

            var attachment = message.Attachments.First();
            string fileExtension = Path.GetExtension(attachment.Filename);
            string newFileName = $"{reactionName}{fileExtension}";
            string newFilePath = Path.Combine(_reactionsFolder, newFileName);

            try
            {
                // Delete old file if it exists
                string oldFileName = _reactions[reactionName];
                string oldFilePath = Path.Combine(_reactionsFolder, oldFileName);
                if (File.Exists(oldFilePath))
                {
                    File.Delete(oldFilePath);
                    Console.WriteLine($"[DEBUG] Deleted old reaction file: {oldFileName}");
                }

                // Save new file
                var fileData = await _fileService.DownloadFileAsync(attachment.Url);
                await File.WriteAllBytesAsync(newFilePath, fileData);

                // Update the reaction mapping
                _reactions[reactionName] = newFileName;
                await _fileService.SaveJsonAsync("reactions.json", _reactions);

                await message.Channel.SendMessageAsync($"✅ Reaction '{reactionName}' has been updated with new file!");
                Console.WriteLine($"[DEBUG] Updated reaction: {reactionName} -> {newFileName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to update reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to update reaction file!");
                return true;
            }
        }
    }
}