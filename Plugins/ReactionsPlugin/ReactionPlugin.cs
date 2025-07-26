using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;
using DiscordBot.Plugins.ReactionPlugin.Commands;

namespace DiscordBot.Plugins.ReactionPlugin
{
    public class ReactionPlugin : IPlugin
    {
        public string Name => "Reaction System";
        public string Description => "Manages custom reactions and aliases";

        private readonly FileService _fileService;
        private readonly ConfigurationService _configService;
        private readonly List<ICommandHandler> _commands = new();
        private Dictionary<string, string> _reactions = new();
        private Dictionary<string, string> _aliases = new();

        private const string ReactionsFile = "reactions.json";
        private const string AliasesFile = "aliases.json";
        private const string ReactionsFolder = "reactions";

        public ReactionPlugin(FileService fileService, ConfigurationService configService)
        {
            _fileService = fileService;
            _configService = configService;
        }

        public async Task InitializeAsync(DiscordSocketClient client)
        {
            // Create reactions folder
            Directory.CreateDirectory(ReactionsFolder);

            // Load data
            _reactions = await _fileService.LoadJsonAsync<Dictionary<string, string>>(ReactionsFile);
            _aliases = await _fileService.LoadJsonAsync<Dictionary<string, string>>(AliasesFile);

            // Register commands
            _commands.Add(new AddReactionCommand(_reactions, _aliases, _fileService, ReactionsFolder));
            _commands.Add(new DeleteReactionCommand(_reactions, _aliases, _fileService, ReactionsFolder));
            _commands.Add(new ChangeReactionCommand(_reactions, _fileService, ReactionsFolder));
            _commands.Add(new RenameReactionCommand(_reactions, _aliases, _fileService));
            _commands.Add(new ListReactionsCommand(_reactions, _aliases));
            _commands.Add(new AddAliasCommand(_reactions, _aliases, _fileService));
            _commands.Add(new RemoveAliasCommand(_aliases, _fileService));
            _commands.Add(new RenameAliasCommand(_aliases, _fileService));

            Console.WriteLine($"[DEBUG] Loaded {_reactions.Count} reactions and {_aliases.Count} aliases");
        }

        public async Task<bool> HandleMessageAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.ToLower().Trim();
            string prefix = _configService.CommandPrefix.ToLower();

            // Check for saved reactions first
            if (await CheckSavedReactions(message, content))
                return true;

            // Handle commands
            if (content.StartsWith(prefix))
            {
                string commandText = content.Substring(prefix.Length);
                string[] parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length == 0) return false;

                string commandName = parts[0];
                
                foreach (var command in _commands)
                {
                    if (commandName == command.Command || commandText.StartsWith(command.Command + " "))
                    {
                        return await command.HandleAsync(message, parts);
                    }
                }
            }

            return false;
        }

        private async Task<bool> CheckSavedReactions(SocketMessage message, string content)
        {
            // Check direct reactions
            if (_reactions.ContainsKey(content))
            {
                return await SendReactionFile(message, _reactions[content]);
            }

            // Check aliases
            if (_aliases.ContainsKey(content) && _reactions.ContainsKey(_aliases[content]))
            {
                return await SendReactionFile(message, _reactions[_aliases[content]]);
            }

            return false;
        }

        private async Task<bool> SendReactionFile(SocketMessage message, string fileName)
        {
            string filePath = Path.Combine(ReactionsFolder, fileName);
            if (File.Exists(filePath))
            {
                await message.Channel.SendFileAsync(filePath);
                return true;
            }
            else
            {
                await message.Channel.SendMessageAsync($"Reaction file not found!");
                return false;
            }
        }

        public async Task CleanupAsync()
        {
            await _fileService.SaveJsonAsync(ReactionsFile, _reactions);
            await _fileService.SaveJsonAsync(AliasesFile, _aliases);
        }
    }
}