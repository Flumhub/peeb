using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Plugins.ReminderPlugin.Services;
using DiscordBot.Plugins.ReminderPlugin.Models;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin.Commands
{
    public class RemindMeCommand : ICommandHandler
    {
        public string Command => "remindme";
        public string Description => "Set a reminder";
        public string Usage => "remindme [in/at] [time] [message] - e.g. 'remindme in 2 hours take a break'";

        private readonly ReminderService _reminderService;

        public RemindMeCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`\n\n" +
                    "Examples:\n" +
                    "• `peeb remindme in 2 hours take a break`\n" +
                    "• `peeb remindme at 3pm call mom`\n" +
                    "• `peeb remindme tomorrow at 9am meeting`\n" +
                    "• `peeb remindme Dec 25 Christmas!`\n" +
                    "• `peeb remindme in 1 day 5 hours check server`");
                return true;
            }

            // Join all args except the command itself
            var fullInput = string.Join(" ", args.Skip(1));
            
            // Parse the reminder
            var parseResult = ParseReminderInput(fullInput);
            if (!parseResult.Success)
            {
                await message.Channel.SendMessageAsync($"❌ {parseResult.Error}");
                return true;
            }

            try
            {
                var guildChannel = message.Channel as SocketGuildChannel;
                if (guildChannel == null)
                {
                    await message.Channel.SendMessageAsync("❌ Reminders can only be set in server channels!");
                    return true;
                }

                var result = await _reminderService.AddReminderAsync(
                    message.Author.Id,
                    message.Channel.Id,
                    guildChannel.Guild.Id,
                    parseResult.DateTime.Value,
                    parseResult.Message
                );

                await message.Channel.SendMessageAsync(result);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to add reminder: {ex.Message}");
                await message.Channel.SendMessageAsync("❌ Failed to set reminder. Please try again.");
                return true;
            }
        }

        private (bool Success, DateTime? DateTime, string Message, string Error) ParseReminderInput(string input)
        {
            // Pattern to match various reminder formats
            // This regex looks for time expressions and captures everything after as the message
            var patterns = new[]
            {
                // "in X time MESSAGE"
                @"^(?:in\s+)(.+?)(?:\s+(.+))$",
                // "at TIME MESSAGE" 
                @"^(?:at\s+)(.+?)(?:\s+(.+))$",
                // "TIME MESSAGE" (no in/at prefix)
                @"^(.+?)(?:\s+(.+))$"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var timePart = match.Groups[1].Value.Trim();
                    var messagePart = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";

                    // Try parsing the time part
                    var timeResult = TimeParser.ParseTime(pattern.Contains("in\\s+") ? $"in {timePart}" : timePart);
                    
                    if (timeResult.Success)
                    {
                        // If no message was captured, the entire input might be a time expression
                        if (string.IsNullOrEmpty(messagePart))
                        {
                            // Try to find where the time ends and message begins
                            var splitResult = SplitTimeAndMessage(input);
                            if (splitResult.Success)
                            {
                                messagePart = splitResult.Message;
                            }
                        }

                        if (string.IsNullOrEmpty(messagePart))
                        {
                            messagePart = "Reminder"; // Default message
                        }

                        return (true, timeResult.DateTime, messagePart, "");
                    }
                }
            }

            // If no pattern matched, try to split on common time keywords
            var splitOnKeywords = SplitTimeAndMessage(input);
            if (splitOnKeywords.Success)
            {
                var timeResult = TimeParser.ParseTime(splitOnKeywords.TimePart);
                if (timeResult.Success)
                {
                    return (true, timeResult.DateTime, splitOnKeywords.Message, "");
                }
                return (false, null, "", timeResult.Error);
            }

            return (false, null, "", "Could not parse reminder. Use format: 'remindme [in/at] [time] [message]'");
        }

        private (bool Success, string TimePart, string Message) SplitTimeAndMessage(string input)
        {
            // Common time-related words that typically end a time expression
            var timeEndMarkers = new[] { "am", "pm", "hours?", "minutes?", "days?", "seconds?", "hrs?", "mins?", "secs?", @"\d+", "tomorrow", "today" };
            
            foreach (var marker in timeEndMarkers)
            {
                var pattern = @$"^(.*?{marker})\s+(.+)$";
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return (true, match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
                }
            }

            return (false, "", "");
        }
    }

    public class MyRemindersCommand : ICommandHandler
    {
        public string Command => "myreminders";
        public string Description => "List your active reminders in this channel";
        public string Usage => "myreminders";

        private readonly ReminderService _reminderService;

        public MyRemindersCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            // Get reminders only for this channel
            var reminders = await _reminderService.GetUserRemindersAsync(message.Author.Id, message.Channel.Id);

            if (!reminders.Any())
            {
                await message.Channel.SendMessageAsync("📅 You have no active reminders in this channel.");
                return true;
            }

            var embed = new EmbedBuilder()
                .WithTitle("📅 Your Active Reminders (This Channel)")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            foreach (var reminder in reminders.Take(10)) // Limit to 10 to avoid embed limits
            {
                var timeUntil = reminder.TriggerTime - DateTime.Now;
                var timeDesc = timeUntil.TotalDays > 1 
                    ? $"in {(int)timeUntil.TotalDays} days"
                    : timeUntil.TotalHours > 1 
                    ? $"in {(int)timeUntil.TotalHours} hours"
                    : $"in {(int)timeUntil.TotalMinutes} minutes";

                var reminderTitle = reminder.IsServerReminder
                    ? $"📢 {reminder.TriggerTime:MMM dd, h:mm tt} ({GetRecurrenceDescription(reminder)})"
                    : reminder.IsRecurring 
                    ? $"🔄 {reminder.TriggerTime:MMM dd, h:mm tt} ({GetRecurrenceDescription(reminder)})"
                    : $"⏰ {reminder.TriggerTime:MMM dd, h:mm tt}";

                var reminderDescription = $"**{reminder.Message}**\n{timeDesc}";
                
                if (reminder.IsRecurring)
                {
                    reminderDescription += $" • Triggered {reminder.TriggerCount} times";
                }
                
                reminderDescription += $" • ID: `{reminder.Id[..8]}`";

                embed.AddField(reminderTitle, reminderDescription, false);
            }

            if (reminders.Count > 10)
            {
                embed.WithFooter($"Showing first 10 of {reminders.Count} reminders");
            }

            await message.Channel.SendMessageAsync(embed: embed.Build());
            return true;
        }

        private string GetRecurrenceDescription(Reminder reminder)
        {
            switch (reminder.RecurrenceType)
            {
                case RecurrenceType.Daily:
                    return reminder.RecurrenceInterval == 1 ? "Daily" : $"Every {reminder.RecurrenceInterval} days";
                
                case RecurrenceType.Weekly:
                    if (reminder.RecurrenceDays.Any())
                    {
                        var dayNames = reminder.RecurrenceDays.Select(d => d.ToString().Substring(0, 3)).ToList();
                        var dayString = string.Join(", ", dayNames);
                        return reminder.RecurrenceInterval == 1 ? $"Weekly on {dayString}" : $"Every {reminder.RecurrenceInterval} weeks on {dayString}";
                    }
                    return reminder.RecurrenceInterval == 1 ? "Weekly" : $"Every {reminder.RecurrenceInterval} weeks";
                
                case RecurrenceType.Monthly:
                    if (reminder.MonthlyDay.HasValue)
                    {
                        var dayDesc = reminder.MonthlyDay.Value == -1 ? "last day" : $"day {reminder.MonthlyDay.Value}";
                        return reminder.RecurrenceInterval == 1 ? $"Monthly on {dayDesc}" : $"Every {reminder.RecurrenceInterval} months on {dayDesc}";
                    }
                    else if (reminder.WeekOfMonth.HasValue && reminder.WeeklyDayOfWeek.HasValue)
                    {
                        var weekDesc = reminder.WeekOfMonth.Value == WeekOfMonth.Last ? "last" : reminder.WeekOfMonth.Value.ToString().ToLower();
                        return reminder.RecurrenceInterval == 1 ? $"Monthly on {weekDesc} {reminder.WeeklyDayOfWeek.Value}" : $"Every {reminder.RecurrenceInterval} months on {weekDesc} {reminder.WeeklyDayOfWeek.Value}";
                    }
                    return reminder.RecurrenceInterval == 1 ? "Monthly" : $"Every {reminder.RecurrenceInterval} months";
                
                default:
                    return "Unknown";
            }
        }
    }

    public class CancelReminderCommand : ICommandHandler
    {
        public string Command => "cancelreminder";
        public string Description => "Cancel a reminder by ID (must be in the same channel where it was created)";
        public string Usage => "cancelreminder [reminder_id]";

        private readonly ReminderService _reminderService;

        public CancelReminderCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`\nUse `peeb myreminders` to see your reminder IDs in this channel.");
                return true;
            }

            var reminderId = args[1];
            
            // If they provided a short ID (first 8 chars), find the full ID from this channel only
            var userReminders = await _reminderService.GetUserRemindersAsync(message.Author.Id, message.Channel.Id);
            var fullReminderId = userReminders.FirstOrDefault(r => r.Id.StartsWith(reminderId))?.Id ?? reminderId;

            var success = await _reminderService.RemoveReminderAsync(message.Author.Id, message.Channel.Id, fullReminderId);

            if (success)
            {
                await message.Channel.SendMessageAsync($"✅ Reminder `{reminderId}` has been cancelled.");
            }
            else
            {
                await message.Channel.SendMessageAsync($"❌ Could not find reminder with ID `{reminderId}` in this channel. Use `peeb myreminders` to see your active reminders here.");
            }

            return true;
        }
    }
}