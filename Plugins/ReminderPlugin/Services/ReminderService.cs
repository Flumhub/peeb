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
            
            // Clean up old non-recurring triggered reminders
            _reminderData.Reminders.RemoveAll(r => 
                r.IsTriggered && 
                !r.IsRecurring && 
                r.TriggerTime < DateTime.Now.AddDays(-1));
            
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
                IsTriggered = false,
                IsRecurring = false
            };

            _reminderData.Reminders.Add(reminder);
            await SaveRemindersAsync();

            var timeUntil = triggerTime - DateTime.Now;
            string timeDescription = FormatTimeSpan(timeUntil);
            
            return $"✅ Reminder set! I'll remind you {timeDescription} on {triggerTime:MMM dd, yyyy 'at' h:mm tt}";
        }

        public async Task<string> AddRecurringReminderAsync(ulong userId, ulong channelId, ulong guildId, 
            DateTime firstTrigger, string message, RecurrenceType recurrenceType, int interval = 1,
            List<DayOfWeek>? recurringDays = null, int? monthlyDay = null, 
            WeekOfMonth? weekOfMonth = null, DayOfWeek? weeklyDayOfWeek = null,
            DateTime? endDate = null, int? maxTriggers = null)
        {
            var reminder = new Reminder
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ChannelId = channelId,
                GuildId = guildId,
                TriggerTime = firstTrigger,
                Message = message,
                CreatedAt = DateTime.Now,
                IsTriggered = false,
                IsRecurring = true,
                RecurrenceType = recurrenceType,
                RecurrenceInterval = interval,
                RecurrenceEndDate = endDate,
                MaxTriggers = maxTriggers,
                RecurrenceDays = recurringDays ?? new List<DayOfWeek>(),
                MonthlyDay = monthlyDay,
                WeekOfMonth = weekOfMonth,
                WeeklyDayOfWeek = weeklyDayOfWeek
            };

            _reminderData.Reminders.Add(reminder);
            await SaveRemindersAsync();

            string recurrenceDescription = FormatRecurrenceDescription(reminder);
            return $"✅ Recurring reminder set! {recurrenceDescription} Starting {firstTrigger:MMM dd, yyyy 'at' h:mm tt}";
        }

        public async Task<string> AddServerReminderAsync(ulong userId, ulong channelId, ulong guildId,
            DateTime firstTrigger, string message, RecurrenceType recurrenceType, int interval = 1,
            List<DayOfWeek>? recurringDays = null, int? monthlyDay = null,
            WeekOfMonth? weekOfMonth = null, DayOfWeek? weeklyDayOfWeek = null,
            string? imageUrl = null)
        {
            var reminder = new Reminder
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ChannelId = channelId,
                GuildId = guildId,
                TriggerTime = firstTrigger,
                Message = message,
                CreatedAt = DateTime.Now,
                IsTriggered = false,
                IsRecurring = true,
                IsServerReminder = true,
                ImageUrl = imageUrl,
                RecurrenceType = recurrenceType,
                RecurrenceInterval = interval,
                RecurrenceDays = recurringDays ?? new List<DayOfWeek>(),
                MonthlyDay = monthlyDay,
                WeekOfMonth = weekOfMonth,
                WeeklyDayOfWeek = weeklyDayOfWeek
            };

            _reminderData.Reminders.Add(reminder);
            await SaveRemindersAsync();

            string recurrenceDescription = FormatRecurrenceDescription(reminder);
            return $"📢 Server reminder set! {recurrenceDescription} Starting {firstTrigger:MMM dd, yyyy 'at' h:mm tt}";
        }

        public async Task<List<Reminder>> GetUserRemindersAsync(ulong userId)
        {
            return _reminderData.Reminders
                .Where(r => r.UserId == userId && (!r.IsTriggered || r.IsRecurring) && 
                           (r.RecurrenceEndDate == null || r.RecurrenceEndDate > DateTime.Now) &&
                           (r.MaxTriggers == null || r.TriggerCount < r.MaxTriggers))
                .OrderBy(r => r.TriggerTime)
                .ToList();
        }

        public async Task<bool> RemoveReminderAsync(ulong userId, string reminderId)
        {
            var reminder = _reminderData.Reminders.FirstOrDefault(r => 
                r.Id == reminderId && r.UserId == userId);
            
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
                    .Where(r => r.TriggerTime <= now && 
                               (!r.IsTriggered || r.IsRecurring) &&
                               (r.RecurrenceEndDate == null || r.RecurrenceEndDate > now) &&
                               (r.MaxTriggers == null || r.TriggerCount < r.MaxTriggers))
                    .ToList();

                foreach (var reminder in triggeredReminders)
                {
                    await TriggerReminderAsync(reminder);
                    
                    if (reminder.IsRecurring)
                    {
                        // Calculate next trigger time
                        var nextTrigger = CalculateNextTriggerTime(reminder);
                        if (nextTrigger.HasValue)
                        {
                            reminder.TriggerTime = nextTrigger.Value;
                            reminder.TriggerCount++;
                        }
                        else
                        {
                            // Recurring reminder has ended
                            reminder.IsTriggered = true;
                        }
                    }
                    else
                    {
                        reminder.IsTriggered = true;
                    }
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

        private DateTime? CalculateNextTriggerTime(Reminder reminder)
        {
            var current = reminder.TriggerTime;
            
            switch (reminder.RecurrenceType)
            {
                case RecurrenceType.Daily:
                    return current.AddDays(reminder.RecurrenceInterval);
                
                case RecurrenceType.Weekly:
                    if (reminder.RecurrenceDays.Any())
                    {
                        // Find next occurrence of specified days
                        var nextDay = FindNextWeeklyOccurrence(current, reminder.RecurrenceDays, reminder.RecurrenceInterval);
                        return new DateTime(nextDay.Year, nextDay.Month, nextDay.Day, 
                                          current.Hour, current.Minute, current.Second);
                    }
                    return current.AddDays(7 * reminder.RecurrenceInterval);
                
                case RecurrenceType.Monthly:
                    return CalculateNextMonthlyOccurrence(current, reminder);
                
                default:
                    return null;
            }
        }

        private DateTime FindNextWeeklyOccurrence(DateTime current, List<DayOfWeek> recurringDays, int intervalWeeks)
        {
            var sortedDays = recurringDays.OrderBy(d => d).ToList();
            var currentDayOfWeek = current.DayOfWeek;
            
            // Look for next day in the same week
            var nextDayThisWeek = sortedDays.FirstOrDefault(d => d > currentDayOfWeek);
            if (nextDayThisWeek != default)
            {
                var daysToAdd = ((int)nextDayThisWeek - (int)currentDayOfWeek);
                return current.Date.AddDays(daysToAdd);
            }
            
            // Move to next interval week and use first day
            var weeksToAdd = intervalWeeks;
            var daysToFirstDay = ((int)sortedDays.First() - (int)currentDayOfWeek + 7) % 7;
            if (daysToFirstDay == 0) daysToFirstDay = 7;
            
            return current.Date.AddDays(daysToFirstDay + (weeksToAdd - 1) * 7);
        }

        private DateTime? CalculateNextMonthlyOccurrence(DateTime current, Reminder reminder)
        {
            if (reminder.MonthlyDay.HasValue)
            {
                // Specific day of month
                var nextMonth = current.AddMonths(reminder.RecurrenceInterval);
                var targetDay = reminder.MonthlyDay.Value;
                
                if (targetDay == -1) // Last day of month
                {
                    targetDay = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                }
                else if (targetDay > DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month))
                {
                    targetDay = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                }
                
                return new DateTime(nextMonth.Year, nextMonth.Month, targetDay, 
                                  current.Hour, current.Minute, current.Second);
            }
            else if (reminder.WeekOfMonth.HasValue && reminder.WeeklyDayOfWeek.HasValue)
            {
                // Specific week and day (e.g., "first Monday", "last Friday")
                var nextMonth = current.AddMonths(reminder.RecurrenceInterval);
                return FindWeekDayInMonth(nextMonth.Year, nextMonth.Month, 
                                        reminder.WeekOfMonth.Value, reminder.WeeklyDayOfWeek.Value, current);
            }
            
            return current.AddMonths(reminder.RecurrenceInterval);
        }

        private DateTime FindWeekDayInMonth(int year, int month, WeekOfMonth weekOfMonth, DayOfWeek dayOfWeek, DateTime originalTime)
        {
            if (weekOfMonth == WeekOfMonth.Last)
            {
                // Find last occurrence of dayOfWeek in month
                var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                while (lastDay.DayOfWeek != dayOfWeek)
                {
                    lastDay = lastDay.AddDays(-1);
                }
                return new DateTime(lastDay.Year, lastDay.Month, lastDay.Day,
                                  originalTime.Hour, originalTime.Minute, originalTime.Second);
            }
            else
            {
                // Find Nth occurrence of dayOfWeek in month
                var firstDay = new DateTime(year, month, 1);
                var daysToTarget = ((int)dayOfWeek - (int)firstDay.DayOfWeek + 7) % 7;
                var targetDate = firstDay.AddDays(daysToTarget + (((int)weekOfMonth - 1) * 7));
                
                if (targetDate.Month != month)
                {
                    // This week doesn't exist in this month, use last week
                    targetDate = targetDate.AddDays(-7);
                }
                
                return new DateTime(targetDate.Year, targetDate.Month, targetDate.Day,
                                  originalTime.Hour, originalTime.Minute, originalTime.Second);
            }
        }

        private async Task TriggerReminderAsync(Reminder reminder)
        {
            try
            {
                var guild = _client.GetGuild(reminder.GuildId);
                var channel = guild?.GetTextChannel(reminder.ChannelId);
                var user = guild?.GetUser(reminder.UserId);

                if (channel != null)
                {
                    if (reminder.IsServerReminder)
                    {
                        // Server reminder - rich embed, no ping
                        await SendServerReminderAsync(channel, reminder);
                    }
                    else if (user != null)
                    {
                        // Personal reminder - ping user
                        await SendPersonalReminderAsync(channel, user, reminder);
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] Could not find user for reminder {reminder.Id}");
                    }
                }
                else
                {
                    Console.WriteLine($"[WARNING] Could not find channel for reminder {reminder.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to trigger reminder {reminder.Id}: {ex.Message}");
            }
        }

        private async Task SendServerReminderAsync(Discord.WebSocket.SocketTextChannel channel, Reminder reminder)
        {
            var embed = new Discord.EmbedBuilder()
                .WithTitle("📢 " + reminder.Message)
                .WithColor(new Discord.Color(88, 101, 242)) // Discord blurple
                .WithCurrentTimestamp();

            // Add image if provided
            if (!string.IsNullOrEmpty(reminder.ImageUrl))
            {
                embed.WithImageUrl(reminder.ImageUrl);
            }

            // Add footer with recurrence info
            var recurrenceInfo = GetRecurrenceInfo(reminder);
            embed.WithFooter($"{recurrenceInfo} • Next: {CalculateNextTriggerTime(reminder):MMM dd 'at' h:mm tt}");

            await channel.SendMessageAsync(embed: embed.Build());
            Console.WriteLine($"[DEBUG] Triggered server reminder: {reminder.Message}");
        }

        private async Task SendPersonalReminderAsync(Discord.WebSocket.SocketTextChannel channel, Discord.WebSocket.SocketGuildUser user, Reminder reminder)
        {
            var embed = new Discord.EmbedBuilder()
                .WithTitle(reminder.IsRecurring ? "🔄 Recurring Reminder!" : "⏰ Reminder!")
                .WithDescription(reminder.Message)
                .WithColor(reminder.IsRecurring ? Discord.Color.Blue : Discord.Color.Orange)
                .WithFooter($"Set on {reminder.CreatedAt:MMM dd, yyyy 'at' h:mm tt}" + 
                           (reminder.IsRecurring ? $" • Trigger #{reminder.TriggerCount + 1}" : ""))
                .WithCurrentTimestamp();

            if (reminder.IsRecurring)
            {
                var nextTrigger = CalculateNextTriggerTime(reminder);
                if (nextTrigger.HasValue && 
                    (reminder.RecurrenceEndDate == null || nextTrigger <= reminder.RecurrenceEndDate) &&
                    (reminder.MaxTriggers == null || reminder.TriggerCount + 1 < reminder.MaxTriggers))
                {
                    embed.AddField("Next Reminder", 
                                 $"{nextTrigger:MMM dd, yyyy 'at' h:mm tt}", true);
                }
                else
                {
                    embed.AddField("Status", "This was the final reminder", true);
                }
            }

            await channel.SendMessageAsync($"{user.Mention}", embed: embed.Build());
            Console.WriteLine($"[DEBUG] Triggered {(reminder.IsRecurring ? "recurring " : "")}reminder for {user.Username}: {reminder.Message}");
        }

        private string GetRecurrenceInfo(Reminder reminder)
        {
            return reminder.RecurrenceType switch
            {
                RecurrenceType.Daily => reminder.RecurrenceInterval == 1 ? "Daily" : $"Every {reminder.RecurrenceInterval} days",
                RecurrenceType.Weekly => reminder.RecurrenceInterval == 1 ? "Weekly" : $"Every {reminder.RecurrenceInterval} weeks",
                RecurrenceType.Monthly => reminder.RecurrenceInterval == 1 ? "Monthly" : $"Every {reminder.RecurrenceInterval} months",
                _ => ""
            };
        }

        private string FormatRecurrenceDescription(Reminder reminder)
        {
            switch (reminder.RecurrenceType)
            {
                case RecurrenceType.Daily:
                    return $"I'll remind you every {(reminder.RecurrenceInterval == 1 ? "day" : $"{reminder.RecurrenceInterval} days")}.";
                
                case RecurrenceType.Weekly:
                    if (reminder.RecurrenceDays.Any())
                    {
                        var dayNames = reminder.RecurrenceDays.Select(d => d.ToString()).ToList();
                        var dayString = string.Join(", ", dayNames.Take(dayNames.Count - 1));
                        if (dayNames.Count > 1) dayString += " and " + dayNames.Last();
                        else dayString = dayNames.First();
                        
                        return $"I'll remind you every {dayString}" + 
                               (reminder.RecurrenceInterval > 1 ? $" (every {reminder.RecurrenceInterval} weeks)" : "") + ".";
                    }
                    return $"I'll remind you every {(reminder.RecurrenceInterval == 1 ? "week" : $"{reminder.RecurrenceInterval} weeks")}.";
                
                case RecurrenceType.Monthly:
                    if (reminder.MonthlyDay.HasValue)
                    {
                        var dayDesc = reminder.MonthlyDay.Value == -1 ? "last day" : $"day {reminder.MonthlyDay.Value}";
                        return $"I'll remind you on the {dayDesc} of every {(reminder.RecurrenceInterval == 1 ? "month" : $"{reminder.RecurrenceInterval} months")}.";
                    }
                    else if (reminder.WeekOfMonth.HasValue && reminder.WeeklyDayOfWeek.HasValue)
                    {
                        var weekDesc = reminder.WeekOfMonth.Value == WeekOfMonth.Last ? "last" : 
                                      reminder.WeekOfMonth.Value.ToString().ToLower();
                        return $"I'll remind you on the {weekDesc} {reminder.WeeklyDayOfWeek.Value} of every {(reminder.RecurrenceInterval == 1 ? "month" : $"{reminder.RecurrenceInterval} months")}.";
                    }
                    return $"I'll remind you every {(reminder.RecurrenceInterval == 1 ? "month" : $"{reminder.RecurrenceInterval} months")}.";
                
                default:
                    return "";
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

//Test comment