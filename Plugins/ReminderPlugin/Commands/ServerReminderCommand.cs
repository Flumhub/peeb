using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Plugins.ReminderPlugin.Services;
using DiscordBot.Plugins.ReminderPlugin.Models;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin.Commands
{
    public class ServerReminderCommand : ICommandHandler
    {
        public string Command => "serverreminder";
        public string Description => "Set a recurring server-wide reminder (owner only)";
        public string Usage => "serverreminder every [interval] [at time] [message] - attach an image optionally";

        private readonly ReminderService _reminderService;

        public ServerReminderCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2 || !args[1].Equals("every", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`\n\n" +
                    "Examples:\n" +
                    "• `peeb serverreminder every day at 9am Check in to HoYoLAB!`\n" +
                    "• `peeb serverreminder every day at 12pm Lunch time reminder` (attach image)\n" +
                    "• `peeb serverreminder every week on monday at 10am Weekly reset!`");
                return true;
            }

            // Get image URL if attached
            string? imageUrl = null;
            if (message.Attachments.Any())
            {
                var attachment = message.Attachments.First();
                if (attachment.ContentType?.StartsWith("image/") == true)
                {
                    imageUrl = attachment.Url;
                }
            }

            // Parse: "every day at 9am message" or "every week on monday at 2pm message"
            var fullInput = string.Join(" ", args.Skip(2)); // Skip "serverreminder" and "every"
            var parseResult = ParseServerReminderInput(fullInput);

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
                    await message.Channel.SendMessageAsync("❌ Server reminders can only be set in server channels!");
                    return true;
                }

                var result = await _reminderService.AddServerReminderAsync(
                    message.Author.Id,
                    message.Channel.Id,
                    guildChannel.Guild.Id,
                    parseResult.FirstTrigger!.Value,
                    parseResult.Message,
                    parseResult.RecurrenceType,
                    parseResult.Interval,
                    parseResult.RecurringDays,
                    parseResult.MonthlyDay,
                    parseResult.WeekOfMonth,
                    parseResult.WeeklyDayOfWeek,
                    imageUrl
                );

                await message.Channel.SendMessageAsync(result);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to add server reminder: {ex.Message}");
                await message.Channel.SendMessageAsync("❌ Failed to set server reminder. Please try again.");
                return true;
            }
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType,
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth,
                 DayOfWeek? WeeklyDayOfWeek, string Error) ParseServerReminderInput(string input)
        {
            var normalizedInput = input.ToLower().Trim();

            RecurrenceType recurrenceType;
            if (normalizedInput.StartsWith("day"))
                recurrenceType = RecurrenceType.Daily;
            else if (normalizedInput.StartsWith("week"))
                recurrenceType = RecurrenceType.Weekly;
            else if (normalizedInput.StartsWith("month"))
                recurrenceType = RecurrenceType.Monthly;
            else if (Regex.IsMatch(normalizedInput, @"^\d+\s+days?"))
                recurrenceType = RecurrenceType.Daily;
            else if (Regex.IsMatch(normalizedInput, @"^\d+\s+weeks?"))
                recurrenceType = RecurrenceType.Weekly;
            else if (Regex.IsMatch(normalizedInput, @"^\d+\s+months?"))
                recurrenceType = RecurrenceType.Monthly;
            else
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null,
                       "Must specify 'day', 'week', or 'month'. Example: 'every day at 9am message'");

            return recurrenceType switch
            {
                RecurrenceType.Daily => ParseDailyReminder(normalizedInput, input),
                RecurrenceType.Weekly => ParseWeeklyReminder(normalizedInput, input),
                RecurrenceType.Monthly => ParseMonthlyReminder(normalizedInput, input),
                _ => (false, null, "", RecurrenceType.None, 0, null, null, null, null, "Unsupported recurrence type.")
            };
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType,
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth,
                 DayOfWeek? WeeklyDayOfWeek, string Error) ParseDailyReminder(string normalizedInput, string originalInput)
        {
            var interval = 1;
            var intervalMatch = Regex.Match(normalizedInput, @"^(\d+)\s+days?");
            if (intervalMatch.Success)
            {
                interval = int.Parse(intervalMatch.Groups[1].Value);
            }

            // Extract time and message - more flexible regex
            var atMatch = Regex.Match(originalInput, @"at\s+(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s+(.+)", RegexOptions.IgnoreCase);
            if (!atMatch.Success)
            {
                // Try alternative pattern for 24-hour format
                atMatch = Regex.Match(originalInput, @"at\s+(\d{1,2}:\d{2})\s+(.+)", RegexOptions.IgnoreCase);
            }
            if (!atMatch.Success)
            {
                return (false, null, "", RecurrenceType.Daily, 0, null, null, null, null,
                       "Could not parse time. Use format: 'every day at 9am message' or 'every day at 14:30 message'");
            }

            var timeStr = atMatch.Groups[1].Value.Trim();
            var message = atMatch.Groups[2].Value.Trim();

            // Parse time manually for better control
            var timeParsed = ParseTimeString(timeStr);
            if (!timeParsed.Success)
            {
                return (false, null, "", RecurrenceType.Daily, 0, null, null, null, null,
                       $"Could not parse time '{timeStr}'. Use formats like '9am', '2:30pm', or '14:30'.");
            }

            var firstTrigger = DateTime.Today.Add(timeParsed.Time!.Value);
            if (firstTrigger <= DateTime.Now)
            {
                firstTrigger = firstTrigger.AddDays(1);
            }

            return (true, firstTrigger, message, RecurrenceType.Daily, interval, null, null, null, null, "");
        }

        private (bool Success, TimeSpan? Time) ParseTimeString(string timeStr)
        {
            timeStr = timeStr.Trim().ToLower();
            
            // Try 12-hour format: 2pm, 2:30pm, 2:30 pm
            var match12 = Regex.Match(timeStr, @"^(\d{1,2})(?::(\d{2}))?\s*(am|pm)$", RegexOptions.IgnoreCase);
            if (match12.Success)
            {
                int hour = int.Parse(match12.Groups[1].Value);
                int minute = match12.Groups[2].Success ? int.Parse(match12.Groups[2].Value) : 0;
                bool isPm = match12.Groups[3].Value.ToLower() == "pm";
                
                if (hour == 12) hour = 0;
                if (isPm) hour += 12;
                
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                    return (true, new TimeSpan(hour, minute, 0));
            }
            
            // Try 24-hour format: 14:30, 9:00
            var match24 = Regex.Match(timeStr, @"^(\d{1,2}):(\d{2})$");
            if (match24.Success)
            {
                int hour = int.Parse(match24.Groups[1].Value);
                int minute = int.Parse(match24.Groups[2].Value);
                
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                    return (true, new TimeSpan(hour, minute, 0));
            }
            
            // Try hour only: 9, 14
            if (int.TryParse(timeStr, out int hourOnly) && hourOnly >= 0 && hourOnly < 24)
            {
                return (true, new TimeSpan(hourOnly, 0, 0));
            }
            
            return (false, null);
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType,
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth,
                 DayOfWeek? WeeklyDayOfWeek, string Error) ParseWeeklyReminder(string normalizedInput, string originalInput)
        {
            var interval = 1;
            var intervalMatch = Regex.Match(normalizedInput, @"^(\d+)\s+weeks?");
            if (intervalMatch.Success)
            {
                interval = int.Parse(intervalMatch.Groups[1].Value);
            }

            // Parse days
            var recurringDays = new List<DayOfWeek>();
            var dayNames = new Dictionary<string, DayOfWeek>
            {
                {"sunday", DayOfWeek.Sunday}, {"sun", DayOfWeek.Sunday},
                {"monday", DayOfWeek.Monday}, {"mon", DayOfWeek.Monday},
                {"tuesday", DayOfWeek.Tuesday}, {"tue", DayOfWeek.Tuesday},
                {"wednesday", DayOfWeek.Wednesday}, {"wed", DayOfWeek.Wednesday},
                {"thursday", DayOfWeek.Thursday}, {"thu", DayOfWeek.Thursday},
                {"friday", DayOfWeek.Friday}, {"fri", DayOfWeek.Friday},
                {"saturday", DayOfWeek.Saturday}, {"sat", DayOfWeek.Saturday}
            };

            var onMatch = Regex.Match(normalizedInput, @"on\s+(.+?)\s+at");
            if (onMatch.Success)
            {
                var daysPart = onMatch.Groups[1].Value;
                foreach (var day in dayNames)
                {
                    if (daysPart.Contains(day.Key) && !recurringDays.Contains(day.Value))
                    {
                        recurringDays.Add(day.Value);
                    }
                }
            }

            if (!recurringDays.Any())
            {
                recurringDays.Add(DateTime.Now.DayOfWeek);
            }

            // Extract time and message
            var atMatch = Regex.Match(originalInput, @"at\s+(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s+(.+)", RegexOptions.IgnoreCase);
            if (!atMatch.Success)
            {
                return (false, null, "", RecurrenceType.Weekly, 0, null, null, null, null,
                       "Could not parse time. Use format: 'every week on monday at 9am message'");
            }

            var timeStr = atMatch.Groups[1].Value.Trim();
            var message = atMatch.Groups[2].Value.Trim();

            var timeResult = TimeParser.ParseTime($"today at {timeStr}");
            if (!timeResult.Success)
            {
                return (false, null, "", RecurrenceType.Weekly, 0, null, null, null, null,
                       $"Could not parse time '{timeStr}'.");
            }

            var targetTime = timeResult.DateTime!.Value.TimeOfDay;
            var firstDay = recurringDays.OrderBy(d => d).First();
            var today = DateTime.Now;
            var daysUntilFirst = ((int)firstDay - (int)today.DayOfWeek + 7) % 7;

            var firstTrigger = today.Date.AddDays(daysUntilFirst).Add(targetTime);
            if (daysUntilFirst == 0 && firstTrigger <= DateTime.Now)
            {
                firstTrigger = firstTrigger.AddDays(7);
            }

            return (true, firstTrigger, message, RecurrenceType.Weekly, interval, recurringDays, null, null, null, "");
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType,
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth,
                 DayOfWeek? WeeklyDayOfWeek, string Error) ParseMonthlyReminder(string normalizedInput, string originalInput)
        {
            var interval = 1;
            var intervalMatch = Regex.Match(normalizedInput, @"^(\d+)\s+months?");
            if (intervalMatch.Success)
            {
                interval = int.Parse(intervalMatch.Groups[1].Value);
            }

            int? monthlyDay = null;
            WeekOfMonth? weekOfMonth = null;
            DayOfWeek? weeklyDayOfWeek = null;

            // Parse "on the 15th" or "on the first monday"
            var onMatch = Regex.Match(normalizedInput, @"on\s+(?:the\s+)?(.+?)\s+at");
            if (onMatch.Success)
            {
                var daySpec = onMatch.Groups[1].Value.Trim();

                if (daySpec.Contains("last day"))
                {
                    monthlyDay = -1;
                }
                else if (Regex.IsMatch(daySpec, @"^\d+"))
                {
                    var dayNum = Regex.Match(daySpec, @"^(\d+)");
                    if (dayNum.Success) monthlyDay = int.Parse(dayNum.Groups[1].Value);
                }
                else
                {
                    var weekDayMatch = Regex.Match(daySpec, @"(first|second|third|fourth|last)\s+(\w+)");
                    if (weekDayMatch.Success)
                    {
                        weekOfMonth = weekDayMatch.Groups[1].Value.ToLower() switch
                        {
                            "first" => WeekOfMonth.First,
                            "second" => WeekOfMonth.Second,
                            "third" => WeekOfMonth.Third,
                            "fourth" => WeekOfMonth.Fourth,
                            "last" => WeekOfMonth.Last,
                            _ => null
                        };

                        var dayNames = new Dictionary<string, DayOfWeek>
                        {
                            {"sunday", DayOfWeek.Sunday}, {"monday", DayOfWeek.Monday},
                            {"tuesday", DayOfWeek.Tuesday}, {"wednesday", DayOfWeek.Wednesday},
                            {"thursday", DayOfWeek.Thursday}, {"friday", DayOfWeek.Friday},
                            {"saturday", DayOfWeek.Saturday}
                        };

                        if (dayNames.TryGetValue(weekDayMatch.Groups[2].Value.ToLower(), out var dow))
                        {
                            weeklyDayOfWeek = dow;
                        }
                    }
                }
            }

            if (!monthlyDay.HasValue && !weekOfMonth.HasValue)
            {
                monthlyDay = 1;
            }

            // Extract time and message
            var atMatch = Regex.Match(originalInput, @"at\s+(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s+(.+)", RegexOptions.IgnoreCase);
            if (!atMatch.Success)
            {
                return (false, null, "", RecurrenceType.Monthly, 0, null, null, null, null,
                       "Could not parse time. Use format: 'every month on the 15th at 9am message'");
            }

            var timeStr = atMatch.Groups[1].Value.Trim();
            var message = atMatch.Groups[2].Value.Trim();

            var timeResult = TimeParser.ParseTime($"today at {timeStr}");
            if (!timeResult.Success)
            {
                return (false, null, "", RecurrenceType.Monthly, 0, null, null, null, null,
                       $"Could not parse time '{timeStr}'.");
            }

            var targetTime = timeResult.DateTime!.Value.TimeOfDay;
            var firstTrigger = CalculateFirstMonthlyTrigger(DateTime.Now, targetTime, monthlyDay, weekOfMonth, weeklyDayOfWeek);

            return (true, firstTrigger, message, RecurrenceType.Monthly, interval, null, monthlyDay, weekOfMonth, weeklyDayOfWeek, "");
        }

        private DateTime CalculateFirstMonthlyTrigger(DateTime now, TimeSpan targetTime, int? monthlyDay, WeekOfMonth? weekOfMonth, DayOfWeek? weeklyDayOfWeek)
        {
            for (int monthOffset = 0; monthOffset < 2; monthOffset++)
            {
                var targetDate = now.AddMonths(monthOffset);
                var year = targetDate.Year;
                var month = targetDate.Month;

                DateTime candidate;

                if (monthlyDay.HasValue)
                {
                    var day = monthlyDay.Value;
                    if (day == -1)
                        day = DateTime.DaysInMonth(year, month);
                    else if (day > DateTime.DaysInMonth(year, month))
                        day = DateTime.DaysInMonth(year, month);

                    candidate = new DateTime(year, month, day).Add(targetTime);
                }
                else if (weekOfMonth.HasValue && weeklyDayOfWeek.HasValue)
                {
                    candidate = FindWeekDayInMonth(year, month, weekOfMonth.Value, weeklyDayOfWeek.Value, targetTime);
                }
                else
                {
                    candidate = new DateTime(year, month, 1).Add(targetTime);
                }

                if (candidate > now)
                    return candidate;
            }

            var nextMonth = now.AddMonths(1);
            return new DateTime(nextMonth.Year, nextMonth.Month, 1).Add(targetTime);
        }

        private DateTime FindWeekDayInMonth(int year, int month, WeekOfMonth weekOfMonth, DayOfWeek dayOfWeek, TimeSpan time)
        {
            if (weekOfMonth == WeekOfMonth.Last)
            {
                var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                while (lastDay.DayOfWeek != dayOfWeek)
                    lastDay = lastDay.AddDays(-1);
                return lastDay.Add(time);
            }
            else
            {
                var firstDay = new DateTime(year, month, 1);
                var daysToTarget = ((int)dayOfWeek - (int)firstDay.DayOfWeek + 7) % 7;
                var targetDate = firstDay.AddDays(daysToTarget + (((int)weekOfMonth - 1) * 7));

                if (targetDate.Month != month)
                    targetDate = targetDate.AddDays(-7);

                return targetDate.Add(time);
            }
        }
    }
}