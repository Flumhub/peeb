using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;
using DiscordBot.Plugins.ReactionPlugin.Commands;
using System.Text.RegularExpressions;

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

            // Handle commands first
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

            // Check for saved reactions - sanitize the first word
            if (await CheckSavedReactions(message, content))
                return true;

            return false;
        }

        private async Task<bool> CheckSavedReactions(SocketMessage message, string content)
        {
            // First try exact match with just basic sanitization
            string exactMatch = content.ToLower().Trim();
            
            // Check direct reactions with exact match
            if (_reactions.ContainsKey(exactMatch))
            {
                return await SendReactionFile(message, _reactions[exactMatch]);
            }

            // Check aliases with exact match
            if (_aliases.ContainsKey(exactMatch) && _reactions.ContainsKey(_aliases[exactMatch]))
            {
                return await SendReactionFile(message, _reactions[_aliases[exactMatch]]);
            }
            
            // Now try with punctuation removed (for things like "thanks!")
            string sanitized = GetFirstWordSanitized(content);
            
            // Check direct reactions
            if (_reactions.ContainsKey(sanitized))
            {
                return await SendReactionFile(message, _reactions[sanitized]);
            }

            // Check aliases
            if (_aliases.ContainsKey(sanitized) && _reactions.ContainsKey(_aliases[sanitized]))
            {
                return await SendReactionFile(message, _reactions[_aliases[sanitized]]);
            }

            return false;
        }
        
        private string GetFirstWordSanitized(string input)
        {
            // Convert to lowercase and trim
            string sanitized = input.ToLower().Trim();
            
            // Get the first word (split by space)
            var firstWord = sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            
            // Remove common punctuation at the end
            firstWord = Regex.Replace(firstWord, @"[.!?,;:]+$", "");
            
            return firstWord;
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