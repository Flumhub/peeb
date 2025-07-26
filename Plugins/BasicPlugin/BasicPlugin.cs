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
                .WithTitle("Bot Commands")
                .WithDescription("Here are the available commands:")
                .AddField($"{_configService.CommandPrefix}ping", "Check if bot is responsive")
                .AddField($"{_configService.CommandPrefix}hello", "Say hello")
                .AddField($"{_configService.CommandPrefix}info", "Get server information")
                .AddField($"{_configService.CommandPrefix}addreaction [name]", "Save attached file as reaction")
                .AddField($"{_configService.CommandPrefix}deletereaction [name]", "Delete saved reaction")
                .AddField($"{_configService.CommandPrefix}reactions", "List all saved reactions")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp()
                .Build();

            await message.Channel.SendMessageAsync(embed: embed);
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