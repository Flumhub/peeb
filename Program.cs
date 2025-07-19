using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiscordBot
{
    class Program
    {
        private DiscordSocketClient _client = null!;
        private IConfiguration _configuration = null!;
        private Dictionary<string, string> _reactions = new Dictionary<string, string>();
        private Dictionary<string, string> _aliases = new Dictionary<string, string>(); // alias -> reaction name
        private readonly string _reactionsFile = "reactions.json";
        private readonly string _aliasesFile = "aliases.json";
        private readonly string _reactionsFolder = "reactions";
        private string _commandPrefix = "peeb "; // Default prefix

        static void Main(string[] args)
            => new Program().RunBotAsync().GetAwaiter().GetResult();

        public async Task RunBotAsync()
        {
            // Load configuration
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Load command prefix from configuration
            _commandPrefix = _configuration["Discord:CommandPrefix"] ?? "peeb ";

            // Load saved reactions and aliases
            await LoadReactionsAsync();
            await LoadAliasesAsync();

            // Create reactions folder if it doesn't exist
            Directory.CreateDirectory(_reactionsFolder);

            // Get bot token from configuration
            string botToken = _configuration["Discord:BotToken"]
                ?? throw new InvalidOperationException("Bot token not found in appsettings.json");

            // Create client with required intents
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds |
                                GatewayIntents.GuildMessages |
                                GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);

            // Hook into events
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;

            // Login and start
            await _client.LoginAsync(TokenType.Bot, botToken);
            await _client.StartAsync();

            // Keep the bot running
            await Task.Delay(-1);
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private Task ReadyAsync()
        {
            Console.WriteLine($"{_client.CurrentUser} is connected and ready!");
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            // Debug logging
            Console.WriteLine($"[DEBUG] Message received from {message.Author.Username}");
            Console.WriteLine($"[DEBUG] Message content: '{message.Content}'");
            Console.WriteLine($"[DEBUG] Author is bot: {message.Author.IsBot}");
            Console.WriteLine($"[DEBUG] Message channel: {message.Channel.Name}");

            // Don't respond to bots (including ourselves)
            if (message.Author.IsBot)
            {
                Console.WriteLine("[DEBUG] Ignoring bot message");
                return;
            }

            // Convert to user message for easier handling
            var userMessage = message as SocketUserMessage;
            if (userMessage == null)
            {
                Console.WriteLine("[DEBUG] Message is not a user message");
                return;
            }

            Console.WriteLine($"Message from {message.Author.Username}: {message.Content}");

            // Check for saved reactions first (including aliases)
            if (await CheckSavedReactions(message))
                return;

            // Simple command handling based on message content
            var messageContent = message.Content.ToLower();
            Console.WriteLine($"[DEBUG] Processing command: '{messageContent}'");

            // Check if message starts with command prefix
            if (!messageContent.StartsWith(_commandPrefix.ToLower()))
            {
                // Check for file upload conditions ONLY for non-command messages
                await CheckFileUploadConditions(message);

                // Check for non-command reactions
                if (messageContent.Contains("good bot"))
                {
                    Console.WriteLine("[DEBUG] Good bot detected, responding...");
                    await message.AddReactionAsync(new Emoji("😊"));
                    await message.Channel.SendMessageAsync("Thank you! 😊");
                }
                else if (messageContent.Contains("bad bot"))
                {
                    Console.WriteLine("[DEBUG] Bad bot detected, responding...");
                    await message.AddReactionAsync(new Emoji("😢"));
                    await message.Channel.SendMessageAsync("I'm sorry... I'll try to do better! 😢");
                }
                return;
            }

            // Remove prefix and get command
            string command = messageContent.Substring(_commandPrefix.Length);

            // Basic ping command
            if (command == BotConfig.Commands.Ping)
            {
                Console.WriteLine("[DEBUG] Ping command detected, responding...");
                await message.Channel.SendMessageAsync("Pong! 🏓");
            }

            // Reaction management commands
            else if (command.StartsWith(BotConfig.Commands.MakeReaction))
            {
                await HandleMakeReactionCommand(message, command);
            }
            else if (command.StartsWith(BotConfig.Commands.DeleteReaction))
            {
                await HandleDeleteReactionCommand(message, command);
            }
            else if (command.StartsWith(BotConfig.Commands.ChangeReaction))
            {
                await HandleChangeReactionCommand(message, command);  // Now replaces image
            }
            else if (command.StartsWith(BotConfig.Commands.RenameReaction))
            {
                await HandleRenameReactionCommand(message, command);  // Now renames reaction
            }
            else if (command == BotConfig.Commands.Reactions)
            {
                await HandleListReactionsCommand(message);
            }

            // Alias management commands
            else if (command.StartsWith(BotConfig.Commands.AddAlias))
            {
                await HandleAddAliasCommand(message, command);
            }
            else if (command.StartsWith(BotConfig.Commands.RemoveAlias))
            {
                await HandleRemoveAliasCommand(message, command);
            }
            else if (command.StartsWith(BotConfig.Commands.RenameAlias))
            {
                await HandleRenameAliasCommand(message, command);
            }

            // Hello command
            else if (command.StartsWith(BotConfig.Commands.Hello))
            {
                Console.WriteLine("[DEBUG] Hello command detected, responding...");
                await message.Channel.SendMessageAsync($"Hello {message.Author.Mention}! 👋");
            }

            // Help command
            else if (command == BotConfig.Commands.Help)
            {
                Console.WriteLine("[DEBUG] Help command detected, responding...");
                var embed = new EmbedBuilder()
                    .WithTitle("Bot Commands")
                    .WithDescription("Here are the available commands:")
                    .AddField($"{_commandPrefix}ping", "Check if bot is responsive")
                    .AddField($"{_commandPrefix}addreaction [name]", "Save attached file as reaction")
                    .AddField($"{_commandPrefix}deletereaction [name]", "Delete saved reaction")
                    .AddField($"{_commandPrefix}changereaction [name]", "Replace existing reaction with new attached file")
                    .AddField($"{_commandPrefix}renamereaction [old] [new]", "Rename saved reaction")
                    .AddField($"{_commandPrefix}reactions", "List all saved reactions")
                    .AddField($"{_commandPrefix}addalias [reaction] [alias1] [alias2]...", "Add aliases to a reaction")
                    .AddField($"{_commandPrefix}removealias [alias]", "Remove an alias")
                    .AddField($"{_commandPrefix}renamealias [old] [new]", "Rename an alias")
                    //                    .AddField($"{_commandPrefix}cat", "Get a cat image")
                    //                    .AddField($"{_commandPrefix}log", "Get the bot's log file")
                    //                    .AddField($"{_commandPrefix}info", "Get server information")
                    .WithColor(Color.Blue)
                    .WithCurrentTimestamp()
                    .Build();

                await message.Channel.SendMessageAsync(embed: embed);
            }

            // Send a cat image file
            else if (command == BotConfig.Commands.Cat)
            {
                Console.WriteLine("[DEBUG] Cat command detected, responding...");
                await SendCatImageAsync(message.Channel);
            }

            // Send log file
            else if (command == BotConfig.Commands.Log)
            {
                Console.WriteLine("[DEBUG] Log command detected, responding...");
                await SendLogFileAsync(message.Channel);
            }

            // Server info command
            else if (command == BotConfig.Commands.Info)
            {
                Console.WriteLine("[DEBUG] Info command detected, responding...");
                if (message.Channel is SocketGuildChannel guildChannel)
                {
                    var guild = guildChannel.Guild;
                    var embed = new EmbedBuilder()
                        .WithTitle($"Server Info: {guild.Name}")
                        .AddField("Members", guild.MemberCount, true)
                        .AddField("Created", guild.CreatedAt.ToString("yyyy-MM-dd"), true)
                        .AddField("Owner", guild.Owner?.Username ?? "Unknown", true)
                        .WithThumbnailUrl(guild.IconUrl)
                        .WithColor(Color.Green)
                        .Build();

                    await message.Channel.SendMessageAsync(embed: embed);
                }
                else
                {
                    await message.Channel.SendMessageAsync("This command only works in servers!");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] No command matched for: '{command}'");
            }
        }

        private async Task<bool> CheckSavedReactions(SocketMessage message)
        {
            string messageContent = message.Content.ToLower().Trim();

            // Check direct reactions first
            if (_reactions.ContainsKey(messageContent))
            {
                return await SendReactionFile(message, messageContent, _reactions[messageContent]);
            }

            // Check aliases
            if (_aliases.ContainsKey(messageContent))
            {
                string reactionName = _aliases[messageContent];
                if (_reactions.ContainsKey(reactionName))
                {
                    return await SendReactionFile(message, messageContent, _reactions[reactionName]);
                }
            }

            return false;
        }

        private async Task<bool> SendReactionFile(SocketMessage message, string trigger, string fileName)
        {
            string filePath = Path.Combine(_reactionsFolder, fileName);
            if (File.Exists(filePath))
            {
                await message.Channel.SendFileAsync(filePath);
                Console.WriteLine($"[DEBUG] Sent saved reaction: {trigger}");
                return true;
            }
            else
            {
                Console.WriteLine($"[ERROR] Saved reaction file not found: {filePath}");
                await message.Channel.SendMessageAsync($"Reaction file for '{trigger}' not found!");
                return false;
            }
        }

        private async Task HandleMakeReactionCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}addreaction [filename]` - attach a file to this message");
                return;
            }

            string reactionName = parts[1].ToLower();

            if (message.Attachments.Count == 0)
            {
                await message.Channel.SendMessageAsync("Please attach a file to save as a reaction!");
                return;
            }

            var attachment = message.Attachments.First();
            string fileExtension = Path.GetExtension(attachment.Filename);
            string savedFileName = $"{reactionName}{fileExtension}";
            string filePath = Path.Combine(_reactionsFolder, savedFileName);

            try
            {
                using (var httpClient = new HttpClient())
                {
                    var fileData = await httpClient.GetByteArrayAsync(attachment.Url);
                    await File.WriteAllBytesAsync(filePath, fileData);
                }

                _reactions[reactionName] = savedFileName;
                await SaveReactionsAsync();

                await message.Channel.SendMessageAsync($"✅ Reaction '{reactionName}' saved! Type `{reactionName}` to use it.");
                Console.WriteLine($"[DEBUG] Saved reaction: {reactionName} -> {savedFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to save reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to save reaction file!");
            }
        }

        private async Task HandleDeleteReactionCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}deletereaction [filename]`");
                return;
            }

            string reactionName = parts[1].ToLower();

            if (!_reactions.ContainsKey(reactionName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{reactionName}' not found!");
                return;
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
                await SaveReactionsAsync();
                await SaveAliasesAsync();

                await message.Channel.SendMessageAsync($"✅ Reaction '{reactionName}' deleted!");
                Console.WriteLine($"[DEBUG] Deleted reaction: {reactionName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to delete reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to delete reaction!");
            }
        }

        // NEW: HandleChangeReactionCommand - Replaces existing reaction with new image
        private async Task HandleChangeReactionCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}changereaction [reaction_name]` - attach a new file to replace the existing reaction");
                return;
            }

            string reactionName = parts[1].ToLower();

            if (!_reactions.ContainsKey(reactionName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{reactionName}' not found!");
                return;
            }

            if (message.Attachments.Count == 0)
            {
                await message.Channel.SendMessageAsync("Please attach a file to replace the existing reaction!");
                return;
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
                using (var httpClient = new HttpClient())
                {
                    var fileData = await httpClient.GetByteArrayAsync(attachment.Url);
                    await File.WriteAllBytesAsync(newFilePath, fileData);
                }

                // Update the reaction mapping
                _reactions[reactionName] = newFileName;
                await SaveReactionsAsync();

                await message.Channel.SendMessageAsync($"✅ Reaction '{reactionName}' has been updated with new file!");
                Console.WriteLine($"[DEBUG] Updated reaction: {reactionName} -> {newFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to update reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to update reaction file!");
            }
        }

        // NEW: HandleRenameReactionCommand - Renames reaction (old changereaction functionality)
        private async Task HandleRenameReactionCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 3)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}renamereaction [existing_name] [new_name]`");
                return;
            }

            string oldName = parts[1].ToLower();
            string newName = parts[2].ToLower();

            if (!_reactions.ContainsKey(oldName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{oldName}' not found!");
                return;
            }

            if (_reactions.ContainsKey(newName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{newName}' already exists!");
                return;
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

                await SaveReactionsAsync();
                await SaveAliasesAsync();

                await message.Channel.SendMessageAsync($"✅ Reaction renamed from '{oldName}' to '{newName}'!");
                Console.WriteLine($"[DEBUG] Renamed reaction: {oldName} -> {newName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to rename reaction: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to rename reaction!");
            }
        }

        private async Task HandleListReactionsCommand(SocketMessage message)
        {
            if (_reactions.Count == 0)
            {
                await message.Channel.SendMessageAsync("No saved reactions found!");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Saved Reactions")
                .WithDescription($"Found {_reactions.Count} saved reactions:")
                .WithColor(Color.Purple)
                .WithCurrentTimestamp();

            foreach (var reaction in _reactions)
            {
                var aliases = _aliases.Where(kvp => kvp.Value == reaction.Key).Select(kvp => kvp.Key).ToList();
                string aliasText = aliases.Count > 0 ? $" (Aliases: {string.Join(", ", aliases)})" : "";
                embed.AddField($"`{reaction.Key}`", $"File: {reaction.Value}{aliasText}", true);
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }

        private async Task HandleAddAliasCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 3)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}addalias [reaction_name] [alias1] [alias2] ...`");
                return;
            }

            string reactionName = parts[1].ToLower();

            if (!_reactions.ContainsKey(reactionName))
            {
                await message.Channel.SendMessageAsync($"Reaction '{reactionName}' not found!");
                return;
            }

            var newAliases = parts.Skip(2).Select(a => a.ToLower()).ToList();
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
                await SaveAliasesAsync();
                await message.Channel.SendMessageAsync($"✅ Added aliases for '{reactionName}': {string.Join(", ", addedAliases)}");
            }

            if (skippedAliases.Count > 0)
            {
                await message.Channel.SendMessageAsync($"⚠️ Skipped existing aliases: {string.Join(", ", skippedAliases)}");
            }

            Console.WriteLine($"[DEBUG] Added aliases for {reactionName}: {string.Join(", ", addedAliases)}");
        }

        private async Task HandleRemoveAliasCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}removealias [alias_name]`");
                return;
            }

            string aliasName = parts[1].ToLower();

            if (!_aliases.ContainsKey(aliasName))
            {
                await message.Channel.SendMessageAsync($"Alias '{aliasName}' not found!");
                return;
            }

            try
            {
                string reactionName = _aliases[aliasName];
                _aliases.Remove(aliasName);
                await SaveAliasesAsync();

                await message.Channel.SendMessageAsync($"✅ Alias '{aliasName}' removed from reaction '{reactionName}'!");
                Console.WriteLine($"[DEBUG] Removed alias: {aliasName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to remove alias: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to remove alias!");
            }
        }

        private async Task HandleRenameAliasCommand(SocketMessage message, string command)
        {
            var parts = command.Split(' ');
            if (parts.Length < 3)
            {
                await message.Channel.SendMessageAsync($"Usage: `{_commandPrefix}renamealias [old_alias] [new_alias]`");
                return;
            }

            string oldAlias = parts[1].ToLower();
            string newAlias = parts[2].ToLower();

            if (!_aliases.ContainsKey(oldAlias))
            {
                await message.Channel.SendMessageAsync($"Alias '{oldAlias}' not found!");
                return;
            }

            if (_aliases.ContainsKey(newAlias) || _reactions.ContainsKey(newAlias))
            {
                await message.Channel.SendMessageAsync($"Alias '{newAlias}' already exists!");
                return;
            }

            try
            {
                string reactionName = _aliases[oldAlias];
                _aliases.Remove(oldAlias);
                _aliases[newAlias] = reactionName;
                await SaveAliasesAsync();

                await message.Channel.SendMessageAsync($"✅ Alias renamed from '{oldAlias}' to '{newAlias}'!");
                Console.WriteLine($"[DEBUG] Renamed alias: {oldAlias} -> {newAlias}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to rename alias: {ex.Message}");
                await message.Channel.SendMessageAsync("Failed to rename alias!");
            }
        }

        private async Task LoadReactionsAsync()
        {
            try
            {
                if (File.Exists(_reactionsFile))
                {
                    string json = await File.ReadAllTextAsync(_reactionsFile);
                    _reactions = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                    Console.WriteLine($"[DEBUG] Loaded {_reactions.Count} reactions from file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load reactions: {ex.Message}");
                _reactions = new Dictionary<string, string>();
            }
        }

        private async Task SaveReactionsAsync()
        {
            try
            {
                string json = JsonSerializer.Serialize(_reactions, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_reactionsFile, json);
                Console.WriteLine($"[DEBUG] Saved {_reactions.Count} reactions to file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to save reactions: {ex.Message}");
            }
        }



        private async Task LoadAliasesAsync()
        {
            try
            {
                if (File.Exists(_aliasesFile))
                {
                    string json = await File.ReadAllTextAsync(_aliasesFile);
                    _aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                    Console.WriteLine($"[DEBUG] Loaded {_aliases.Count} aliases from file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load aliases: {ex.Message}");
                _aliases = new Dictionary<string, string>();
            }
        }

        private async Task SaveAliasesAsync()
        {
            try
            {
                string json = JsonSerializer.Serialize(_aliases, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_aliasesFile, json);
                Console.WriteLine($"[DEBUG] Saved {_aliases.Count} aliases to file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to save aliases: {ex.Message}");
            }
        }

        private async Task CheckFileUploadConditions(SocketMessage message)
        {
            string messageContent = message.Content.ToLower();

            // Upload specific files based on message content
            if (messageContent.Contains("funny video"))
            {
                await SendLocalFile(message.Channel, "funny.webm");
            }
            else if (messageContent == "meme")
            {
                await SendLocalFile(message.Channel, "meme.jpg");
            }
            else if (messageContent.Contains("reaction"))
            {
                await SendLocalFile(message.Channel, "reaction.gif");
            }

        }

        private async Task SendLocalFile(ISocketMessageChannel channel, string fileName)
        {
            try
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);

                if (File.Exists(filePath))
                {
                    await channel.SendFileAsync(filePath);
                    Console.WriteLine($"[DEBUG] Sent file: {fileName}");
                }
                else
                {
                    Console.WriteLine($"[ERROR] File not found: {filePath}");
                    await channel.SendMessageAsync($"File `{fileName}` not found!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to send {fileName}: {ex.Message}");
            }
        }

        private async Task SendCatImageAsync(ISocketMessageChannel channel)
        {
            try
            {
                // Create a simple cat image file (you can replace this with actual image files)
                string catImagePath = "cat.txt";

                // Create a simple text file as an example (replace with actual image handling)
                await File.WriteAllTextAsync(catImagePath, "🐱 Here's your cat! (This is a placeholder - replace with actual image file)");

                // Send the file
                await channel.SendFileAsync(catImagePath, "Here's a cat for you! 🐱");

                // Clean up
                if (File.Exists(catImagePath))
                    File.Delete(catImagePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending cat image: {ex.Message}");
                await channel.SendMessageAsync("Sorry, I couldn't send the cat image right now! 😿");
            }
        }



        private async Task SendLogFileAsync(ISocketMessageChannel channel)
        {
            try
            {
                // Create a simple log file
                string logPath = "bot-log.txt";
                string logContent = $"Bot Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                   $"Status: Running\n" +
                                   $"Uptime: {DateTime.Now}\n" +
                                   $"Connected Servers: {_client.Guilds.Count}\n" +
                                   $"Bot User: {_client.CurrentUser?.Username}\n" +
                                   $"Command Prefix: {_commandPrefix}\n" +
                                   $"Reactions: {_reactions.Count}\n" +
                                   $"Aliases: {_aliases.Count}\n";

                await File.WriteAllTextAsync(logPath, logContent);

                // Send the log file
                await channel.SendFileAsync(logPath, "Here's the current bot log:");

                // Clean up
                if (File.Exists(logPath))
                    File.Delete(logPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending log file: {ex.Message}");
                await channel.SendMessageAsync("Sorry, I couldn't generate the log file right now!");
            }
        }
    }
}



// Updated Configuration Class
public static class BotConfig
{
    // Command strings (without prefix)
    public static class Commands
    {
        public const string Ping = "ping";
        public const string Hello = "hello";
        public const string Help = "help";
        public const string Cat = "cat";
        public const string Log = "log";
        public const string Info = "info";
        public const string MakeReaction = "addreaction";
        public const string DeleteReaction = "deletereaction";
        public const string ChangeReaction = "changereaction";  // Now replaces image
        public const string RenameReaction = "renamereaction";  // Now renames reaction
        public const string Reactions = "reactions";
        public const string AddAlias = "addalias";
        public const string RemoveAlias = "removealias";
        public const string RenameAlias = "renamealias";
    }

    public static class Messages
    {
        public const string BotReady = "Bot is ready and connected!";
        public const string ErrorGeneric = "An error occurred while processing your request.";
    }

    // File upload triggers
    public static class FileUploadTriggers
    {
        public const string FunnyVideo = "funny video";
        public const string Meme = "meme";
        public const string Reaction = "reaction";
    }
}