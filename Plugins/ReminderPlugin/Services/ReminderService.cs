using Discord.WebSocket;
using DiscordBot.Core.Services;
using DiscordBot.Plugins.ReminderPlugin.Models;

namespace DiscordBot.Plugins.ReminderPlugin.Services
{
    public class ReminderService
    {
        private readonly FileService _fileService;
        private readonly Timer _reminderTimer;
        private ReminderData _reminderData = new();
        private DiscordSocketClient _client = null!;

        private const string ReminderFile = "reminders.json";

        public ReminderService(FileService fileService)
        {
            _fileService = fileService;
            _reminderTimer = new Timer(CheckReminders, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public async Task InitializeAsync(DiscordSocketClient client)
        {
            _client = client;
            _reminderData = await _fileService.LoadJsonAsync<ReminderData>(ReminderFile);
            
            // Clean up old triggered reminders
            _reminderData.Reminders.RemoveAll(r => r.IsTriggered && r.TriggerTime < DateTime.Now.AddDays(-1));
            await SaveRemindersAsync();
            
            Console.WriteLine($"[DEBUG] Loaded {_reminderData.Reminders.Count} reminders");
        }

        public async Task<string> AddReminderAsync(ulong userId, ulong channelId, ulong guildId, DateTime triggerTime, string message)
        {
            var reminder = new Reminder
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ChannelId = channelId,
                GuildId = guildId,
                TriggerTime = triggerTime,
                Message = message,
                CreatedAt = DateTime.Now,
                IsTriggered = false
            };

            _reminderData.Reminders.Add(reminder);
            await SaveRemindersAsync();

            var timeUntil = triggerTime - DateTime.Now;
            string timeDescription = FormatTimeSpan(timeUntil);
            
            return $"✅ Reminder set! I'll remind you {timeDescription} on {triggerTime:MMM dd, yyyy 'at' h:mm tt}";
        }

        public async Task<List<Reminder>> GetUserRemindersAsync(ulong userId)
        {
            return _reminderData.Reminders
                .Where(r => r.UserId == userId && !r.IsTriggered && r.TriggerTime > DateTime.Now)
                .OrderBy(r => r.TriggerTime)
                .ToList();
        }

        public async Task<bool> RemoveReminderAsync(ulong userId, string reminderId)
        {
            var reminder = _reminderData.Reminders.FirstOrDefault(r => 
                r.Id == reminderId && r.UserId == userId && !r.IsTriggered);
            
            if (reminder != null)
            {
                _reminderData.Reminders.Remove(reminder);
                await SaveRemindersAsync();
                return true;
            }
            
            return false;
        }

        private async void CheckReminders(object? state)
        {
            try
            {
                var now = DateTime.Now;
                var triggeredReminders = _reminderData.Reminders
                    .Where(r => !r.IsTriggered && r.TriggerTime <= now)
                    .ToList();

                foreach (var reminder in triggeredReminders)
                {
                    await TriggerReminderAsync(reminder);
                    reminder.IsTriggered = true;
                }

                if (triggeredReminders.Any())
                {
                    await SaveRemindersAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error checking reminders: {ex.Message}");
            }
        }

        private async Task TriggerReminderAsync(Reminder reminder)
        {
            try
            {
                var guild = _client.GetGuild(reminder.GuildId);
                var channel = guild?.GetTextChannel(reminder.ChannelId);
                var user = guild?.GetUser(reminder.UserId);

                if (channel != null && user != null)
                {
                    var embed = new Discord.EmbedBuilder()
                        .WithTitle("⏰ Reminder!")
                        .WithDescription(reminder.Message)
                        .WithColor(Discord.Color.Orange)
                        .WithFooter($"Set on {reminder.CreatedAt:MMM dd, yyyy 'at' h:mm tt}")
                        .WithCurrentTimestamp()
                        .Build();

                    await channel.SendMessageAsync($"{user.Mention}", embed: embed);
                    Console.WriteLine($"[DEBUG] Triggered reminder for {user.Username}: {reminder.Message}");
                }
                else
                {
                    Console.WriteLine($"[WARNING] Could not find channel or user for reminder {reminder.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to trigger reminder {reminder.Id}: {ex.Message}");
            }
        }

        private async Task SaveRemindersAsync()
        {
            await _fileService.SaveJsonAsync(ReminderFile, _reminderData);
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            var parts = new List<string>();
            
            if (timeSpan.Days > 0)
                parts.Add($"{timeSpan.Days} day{(timeSpan.Days != 1 ? "s" : "")}");
            
            if (timeSpan.Hours > 0)
                parts.Add($"{timeSpan.Hours} hour{(timeSpan.Hours != 1 ? "s" : "")}");
            
            if (timeSpan.Minutes > 0)
                parts.Add($"{timeSpan.Minutes} minute{(timeSpan.Minutes != 1 ? "s" : "")}");
            
            if (timeSpan.TotalMinutes < 1)
                parts.Add($"{timeSpan.Seconds} second{(timeSpan.Seconds != 1 ? "s" : "")}");

            if (parts.Count == 0)
                return "now";
            
            if (parts.Count == 1)
                return $"in {parts[0]}";
            
            if (parts.Count == 2)
                return $"in {parts[0]} and {parts[1]}";
            
            return $"in {string.Join(", ", parts.Take(parts.Count - 1))} and {parts.Last()}";
        }

        public void Dispose()
        {
            _reminderTimer?.Dispose();
        }
    }
}