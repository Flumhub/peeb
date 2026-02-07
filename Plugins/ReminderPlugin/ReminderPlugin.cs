using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;
using DiscordBot.Plugins.ReminderPlugin.Commands;
using DiscordBot.Plugins.ReminderPlugin.Services;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin
{
    public class ReminderPlugin : IPlugin
    {
        public string Name => "Reminder System";
        public string Description => "Set and manage reminders with flexible time parsing and recurring functionality";

        private readonly ConfigurationService _configService;
        private readonly ReminderService _reminderService;
        private readonly List<ICommandHandler> _commands = new();

        public ReminderPlugin(ConfigurationService configService, FileService fileService)
        {
            _configService = configService;
            _reminderService = new ReminderService(fileService);
        }

        public async Task InitializeAsync(DiscordSocketClient client)
        {
            await _reminderService.InitializeAsync(client);

            // Register commands
            _commands.Add(new RemindMeCommand(_reminderService));
            _commands.Add(new EveryReminderCommand(_reminderService));
            _commands.Add(new ServerReminderCommand(_reminderService));
            _commands.Add(new ChannelReminderCommand(_reminderService));
            _commands.Add(new MyRemindersCommand(_reminderService));
            _commands.Add(new CancelReminderCommand(_reminderService));

            Console.WriteLine($"[DEBUG] Reminder plugin initialized with recurring and server reminder functionality");
        }

        public async Task<bool> HandleMessageAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.Trim();
            string contentLower = content.ToLower();
            string prefix = _configService.CommandPrefix.ToLower();

            // Handle commands
            if (contentLower.StartsWith(prefix))
            {
                // Use original content (preserves case) but skip the prefix
                string commandText = content.Substring(prefix.Length);
                string commandTextLower = commandText.ToLower();
                string[] parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length == 0) return false;

                string commandName = parts[0].ToLower();

                // Handle "remind me" as two words
                if (commandName == "remind" && parts.Length > 1 && parts[1].ToLower() == "me")
                {
                    // Reconstruct the args as if it was "remindme"
                    var newParts = new string[parts.Length - 1];
                    newParts[0] = "remindme";
                    Array.Copy(parts, 2, newParts, 1, parts.Length - 2);
                    
                    var remindMeCommand = _commands.OfType<RemindMeCommand>().FirstOrDefault();
                    if (remindMeCommand != null)
                    {
                        return await remindMeCommand.HandleAsync(message, newParts);
                    }
                }

                // Handle normal commands
                foreach (var command in _commands)
                {
                    if (commandName == command.Command || commandTextLower.StartsWith(command.Command + " "))
                    {
                        return await command.HandleAsync(message, parts);
                    }
                }
            }

            return false;
        }

        public async Task CleanupAsync()
        {
            _reminderService.Dispose();
        }
    }
}