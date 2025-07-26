using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Core.Services;

namespace DiscordBot.Plugins.BasicPlugin
{
    public class BasicPlugin : IPlugin
    {
        public string Name => "Basic Commands";
        public string Description => "Provides basic bot functionality";

        private readonly ConfigurationService _configService;
        private DiscordSocketClient _client = null!;

        public BasicPlugin(ConfigurationService configService)
        {
            _configService = configService;
        }

        public Task InitializeAsync(DiscordSocketClient client)
        {
            _client = client;
            return Task.CompletedTask;
        }

        public async Task<bool> HandleMessageAsync(SocketMessage message)
        {
            if (message.Author.IsBot) return false;

            string content = message.Content.ToLower();
            string prefix = _configService.CommandPrefix.ToLower();

            // Handle non-command reactions
            if (content.Contains("good bot"))
            {
                await message.AddReactionAsync(new Emoji("😊"));
                await message.Channel.SendMessageAsync("Thank you! 😊");
                return true;
            }
            else if (content.Contains("bad bot"))
            {
                await message.AddReactionAsync(new Emoji("😢"));
                await message.Channel.SendMessageAsync("I'm sorry... I'll try to do better! 😢");
                return true;
            }

            // Handle commands
            if (content.StartsWith(prefix))
            {
                string command = content.Substring(prefix.Length).Trim();

                switch (command)
                {
                    case "ping":
                        await message.Channel.SendMessageAsync("Pong! 🏓");
                        return true;

                    case "hello":
                        await message.Channel.SendMessageAsync($"Hello {message.Author.Mention}! 👋");
                        return true;

                    case "help":
                        await SendHelpMessage(message);
                        return true;

                    case "info":
                        await SendServerInfo(message);
                        return true;
                }
            }

            return false;
        }

        private async Task SendHelpMessage(SocketMessage message)
        {
            var embed = new EmbedBuilder()
                .WithTitle("🤖 Peeb Bot Commands")
                .WithDescription("Here are all the available commands:")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            // Basic Commands
            embed.AddField("**🔧 Basic Commands**",
                $"`{_configService.CommandPrefix}ping` - Check if bot is responsive\n" +
                $"`{_configService.CommandPrefix}hello` - Say hello\n" +
                $"`{_configService.CommandPrefix}info` - Get server information\n" +
                $"`{_configService.CommandPrefix}help` - Show this help message", false);

            // Reaction Commands
            embed.AddField("**😄 Reaction Commands**",
                $"`{_configService.CommandPrefix}addreaction [name]` - Save attached file as reaction\n" +
                $"`{_configService.CommandPrefix}deletereaction [name]` - Delete saved reaction\n" +
                $"`{_configService.CommandPrefix}changereaction [name]` - Replace reaction with new file\n" +
                $"`{_configService.CommandPrefix}renamereaction [old] [new]` - Rename reaction\n" +
                $"`{_configService.CommandPrefix}reactions` - List all saved reactions", false);

            // Alias Commands
            embed.AddField("**🔗 Alias Commands**",
                $"`{_configService.CommandPrefix}addalias [reaction] [alias1] [alias2]...` - Add aliases\n" +
                $"`{_configService.CommandPrefix}removealias [alias]` - Remove an alias\n" +
                $"`{_configService.CommandPrefix}renamealias [old] [new]` - Rename an alias", false);

            // One-Time Reminder Commands
            embed.AddField("**⏰ One-Time Reminders**",
                $"`{_configService.CommandPrefix}remindme [time] [message]` - Set a reminder\n" +
                $"`{_configService.CommandPrefix}remind me [time] [message]` - Alternative syntax\n" +
                $"`{_configService.CommandPrefix}myreminders` - List your active reminders\n" +
                $"`{_configService.CommandPrefix}cancelreminder [id]` - Cancel a reminder", false);

            // Recurring Reminder Commands
            embed.AddField("**🔄 Recurring Reminders**",
                $"`{_configService.CommandPrefix}every day [at time] [message]` - Daily reminders\n" +
                $"`{_configService.CommandPrefix}every week [on day(s)] [at time] [message]` - Weekly reminders\n" +
                $"`{_configService.CommandPrefix}every month [on day] [at time] [message]` - Monthly reminders\n" +
                $"`{_configService.CommandPrefix}every [N] days/weeks/months` - Custom intervals", false);

            // One-Time Examples
            embed.AddField("**📝 One-Time Examples**",
                $"`{_configService.CommandPrefix}remindme in 2 hours take a break`\n" +
                $"`{_configService.CommandPrefix}remindme at 3pm call mom`\n" +
                $"`{_configService.CommandPrefix}remindme tomorrow at 9am meeting`\n" +
                $"`{_configService.CommandPrefix}remindme Dec 25 Christmas!`", false);

            // Recurring Examples
            embed.AddField("**🔄 Recurring Examples**",
                $"`{_configService.CommandPrefix}every day at 9am take vitamins`\n" +
                $"`{_configService.CommandPrefix}every week on monday at 2pm team meeting`\n" +
                $"`{_configService.CommandPrefix}every month on the 15th pay bills`\n" +
                $"`{_configService.CommandPrefix}every 2 weeks on tuesday and friday standup`\n" +
                $"`{_configService.CommandPrefix}every month on the first monday review goals`\n" +
                $"`{_configService.CommandPrefix}every month on the last day reports`", false);

            embed.WithFooter("💡 You can also just type reaction names (like 'thanks' or 'haha') to use saved reactions!");

            await message.Channel.SendMessageAsync(embed: embed.Build());
        }

        private async Task SendServerInfo(SocketMessage message)
        {
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

        public Task CleanupAsync() => Task.CompletedTask;
    }
}