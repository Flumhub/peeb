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

            // Check for saved reactions first (with sanitization)
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
            // Sanitize the content for reaction matching
            string sanitized = SanitizeForReactionMatch(content);
            
            // Also try to extract potential reaction words from the message
            var potentialReactions = ExtractPotentialReactions(content);
            
            // Check direct reactions with sanitized content
            if (_reactions.ContainsKey(sanitized))
            {
                return await SendReactionFile(message, _reactions[sanitized]);
            }

            // Check aliases with sanitized content
            if (_aliases.ContainsKey(sanitized) && _reactions.ContainsKey(_aliases[sanitized]))
            {
                return await SendReactionFile(message, _reactions[_aliases[sanitized]]);
            }
            
            // Check each potential reaction word/phrase
            foreach (var potential in potentialReactions)
            {
                // Check direct reactions
                if (_reactions.ContainsKey(potential))
                {
                    return await SendReactionFile(message, _reactions[potential]);
                }

                // Check aliases
                if (_aliases.ContainsKey(potential) && _reactions.ContainsKey(_aliases[potential]))
                {
                    return await SendReactionFile(message, _reactions[_aliases[potential]]);
                }
            }

            return false;
        }
        
        private string SanitizeForReactionMatch(string input)
        {
            // Convert to lowercase and trim
            string sanitized = input.ToLower().Trim();
            
            // Remove common punctuation at the end
            sanitized = Regex.Replace(sanitized, @"[.!?,;:]+$", "");
            
            // Remove common punctuation at the beginning
            sanitized = Regex.Replace(sanitized, @"^[.!?,;:]+", "");
            
            // Normalize multiple spaces to single space
            sanitized = Regex.Replace(sanitized, @"\s+", " ");
            
            return sanitized.Trim();
        }
        
        private List<string> ExtractPotentialReactions(string input)
        {
            var potentials = new List<string>();
            
            // Sanitize the full input
            string sanitized = SanitizeForReactionMatch(input);
            
            // Split by common word boundaries and punctuation
            var words = Regex.Split(sanitized, @"[\s,;.!?]+")
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.ToLower().Trim())
                .Distinct()
                .ToList();
            
            // Add individual words
            potentials.AddRange(words);
            
            // Add two-word combinations (for reactions like "thank you")
            for (int i = 0; i < words.Count - 1; i++)
            {
                potentials.Add($"{words[i]} {words[i + 1]}");
            }
            
            // Add three-word combinations (for reactions like "oh my god")
            for (int i = 0; i < words.Count - 2; i++)
            {
                potentials.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
            }
            
            // Also check if the message starts or ends with a known reaction
            // This helps with messages like "thanks!" or "lol what happened"
            var allReactionKeys = _reactions.Keys.Union(_aliases.Keys).ToList();
            foreach (var reaction in allReactionKeys)
            {
                // Check if message starts with this reaction
                if (sanitized.StartsWith(reaction))
                {
                    potentials.Add(reaction);
                }
                
                // Check if message ends with this reaction
                if (sanitized.EndsWith(reaction))
                {
                    potentials.Add(reaction);
                }
                
                // Check if reaction appears as a whole word in the message
                var pattern = $@"\b{Regex.Escape(reaction)}\b";
                if (Regex.IsMatch(sanitized, pattern))
                {
                    potentials.Add(reaction);
                }
            }
            
            return potentials.Distinct().ToList();
        }

        private async Task<bool> SendReactionFile(SocketMessage message, string fileName)
        {
            string filePath = Path.Combine(ReactionsFolder, fileName);
            if (File.Exists(filePath))
            {
                await message.Channel.SendFileAsync(filePath);
                Console.WriteLine($"[DEBUG] Sent reaction: {fileName} for message: \"{message.Content}\"");
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