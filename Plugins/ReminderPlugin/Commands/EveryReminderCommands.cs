using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Plugins.ReminderPlugin.Services;
using DiscordBot.Plugins.ReminderPlugin.Models;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin.Commands
{
    public class EveryReminderCommand : ICommandHandler
    {
        public string Command => "every";
        public string Description => "Set a recurring reminder";
        public string Usage => "every [interval] [time] [message] - e.g. 'every day at 9am take vitamins'";

        private readonly ReminderService _reminderService;

        public EveryReminderCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2)
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`\n\n" +
                    "Examples:\n" +
                    "• `peeb every day at 9am take vitamins`\n" +
                    "• `peeb every week on monday at 2pm team meeting`\n" +
                    "• `peeb every month on the 15th pay bills`\n" +
                    "• `peeb every 2 weeks on tuesday and friday standup`\n" +
                    "• `peeb every month on the first monday review goals`\n" +
                    "• `peeb every month on the last day of month reports`");
                return true;
            }

            // Join all args except the command itself
            var fullInput = string.Join(" ", args.Skip(1));
            
            // Parse the recurring reminder
            var parseResult = ParseEveryReminderInput(fullInput);
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

                var result = await _reminderService.AddRecurringReminderAsync(
                    message.Author.Id,
                    message.Channel.Id,
                    guildChannel.Guild.Id,
                    parseResult.FirstTrigger.Value,
                    parseResult.Message,
                    parseResult.RecurrenceType,
                    parseResult.Interval,
                    parseResult.RecurringDays,
                    parseResult.MonthlyDay,
                    parseResult.WeekOfMonth,
                    parseResult.WeeklyDayOfWeek,
                    parseResult.EndDate,
                    parseResult.MaxTriggers
                );

                await message.Channel.SendMessageAsync(result);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to add recurring reminder: {ex.Message}");
                await message.Channel.SendMessageAsync("❌ Failed to set recurring reminder. Please try again.");
                return true;
            }
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType, 
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth, 
                 DayOfWeek? WeeklyDayOfWeek, DateTime? EndDate, int? MaxTriggers, string Error) ParseEveryReminderInput(string input)
        {
            try
            {
                var normalizedInput = input.ToLower().Trim();
                
                // Parse different patterns
                var patterns = new[]
                {
                    // "day at TIME MESSAGE" or "day MESSAGE"
                    @"^(?:(\d+)\s+)?days?\s+(?:at\s+(.+?))?(?:\s+(.+))?$",
                    // "week on DAY at TIME MESSAGE" or "week at TIME MESSAGE" or "week MESSAGE"  
                    @"^(?:(\d+)\s+)?weeks?\s+(?:on\s+(.+?))?(?:\s+at\s+(.+?))?(?:\s+(.+))?$",
                    // "month on the DAY at TIME MESSAGE" or "month at TIME MESSAGE" or "month MESSAGE"
                    @"^(?:(\d+)\s+)?months?\s+(?:on\s+(?:the\s+)?(.+?))?(?:\s+at\s+(.+?))?(?:\s+(.+))?$"
                };

                // Determine recurrence type from input
                RecurrenceType recurrenceType;
                if (normalizedInput.Contains("day"))
                    recurrenceType = RecurrenceType.Daily;
                else if (normalizedInput.Contains("week"))
                    recurrenceType = RecurrenceType.Weekly;
                else if (normalizedInput.Contains("month"))
                    recurrenceType = RecurrenceType.Monthly;
                else
                    return (false, null, "", RecurrenceType.None, 0, null, null, null, null, null, null, 
                           "Must specify 'day', 'week', or 'month' for recurring reminders.");

                return recurrenceType switch
                {
                    RecurrenceType.Daily => ParseDailyReminder(normalizedInput),
                    RecurrenceType.Weekly => ParseWeeklyReminder(normalizedInput),
                    RecurrenceType.Monthly => ParseMonthlyReminder(normalizedInput),
                    _ => (false, null, "", RecurrenceType.None, 0, null, null, null, null, null, null, 
                         "Unsupported recurrence type.")
                };
            }
            catch (Exception ex)
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null, null, null, 
                       $"Error parsing reminder: {ex.Message}");
            }
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType, 
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth, 
                 DayOfWeek? WeeklyDayOfWeek, DateTime? EndDate, int? MaxTriggers, string Error) ParseDailyReminder(string input)
        {
            // Pattern: "every [N] day[s] [at TIME] MESSAGE"
            var match = Regex.Match(input, @"(?:(\d+)\s+)?days?\s*(?:at\s+(.+?))?\s*(.+)?", RegexOptions.IgnoreCase);
            
            var interval = 1;
            if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var parsedInterval))
            {
                interval = parsedInterval;
            }

            var timeStr = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "9:00am";
            var message = match.Groups[3].Success ? match.Groups[3].Value.Trim() : "Daily reminder";

            // If the message contains time indicators, extract them
            if (string.IsNullOrEmpty(timeStr) && !string.IsNullOrEmpty(message))
            {
                var timeExtract = ExtractTimeFromMessage(message);
                if (timeExtract.HasTime)
                {
                    timeStr = timeExtract.TimeStr;
                    message = timeExtract.RemainingMessage;
                }
            }

            if (string.IsNullOrEmpty(message) || message.Length < 2)
            {
                message = "Daily reminder";
            }

            var timeResult = TimeParser.ParseTime($"today at {timeStr}");
            if (!timeResult.Success)
            {
                return (false, null, "", RecurrenceType.Daily, 0, null, null, null, null, null, null, 
                       $"Could not parse time '{timeStr}'. Use format like '9am' or '14:30'.");
            }

            // If time is in the past today, start tomorrow
            var firstTrigger = timeResult.DateTime.Value;
            if (firstTrigger <= DateTime.Now)
            {
                firstTrigger = firstTrigger.AddDays(1);
            }

            return (true, firstTrigger, message, RecurrenceType.Daily, interval, null, null, null, null, null, null, "");
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType, 
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth, 
                 DayOfWeek? WeeklyDayOfWeek, DateTime? EndDate, int? MaxTriggers, string Error) ParseWeeklyReminder(string input)
        {
            // Pattern: "every [N] week[s] [on DAY(s)] [at TIME] MESSAGE"
            var match = Regex.Match(input, @"(?:(\d+)\s+)?weeks?\s*(?:on\s+(.+?))?\s*(?:at\s+(.+?))?\s*(.+)?", RegexOptions.IgnoreCase);
            
            var interval = 1;
            if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var parsedInterval))
            {
                interval = parsedInterval;
            }

            var daysStr = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
            var timeStr = match.Groups[3].Success ? match.Groups[3].Value.Trim() : "9:00am";
            var message = match.Groups[4].Success ? match.Groups[4].Value.Trim() : "Weekly reminder";

            // Parse days of week
            var recurringDays = new List<DayOfWeek>();
            if (!string.IsNullOrEmpty(daysStr))
            {
                var dayNames = new Dictionary<string, DayOfWeek>
                {
                    {"sunday", DayOfWeek.Sunday}, {"sun", DayOfWeek.Sunday},
                    {"monday", DayOfWeek.Monday}, {"mon", DayOfWeek.Monday},
                    {"tuesday", DayOfWeek.Tuesday}, {"tue", DayOfWeek.Tuesday}, {"tues", DayOfWeek.Tuesday},
                    {"wednesday", DayOfWeek.Wednesday}, {"wed", DayOfWeek.Wednesday},
                    {"thursday", DayOfWeek.Thursday}, {"thu", DayOfWeek.Thursday}, {"thur", DayOfWeek.Thursday}, {"thurs", DayOfWeek.Thursday},
                    {"friday", DayOfWeek.Friday}, {"fri", DayOfWeek.Friday},
                    {"saturday", DayOfWeek.Saturday}, {"sat", DayOfWeek.Saturday}
                };

                var dayParts = daysStr.Split(new[] { ",", " and ", "&", "+" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var dayPart in dayParts)
                {
                    var cleanDay = dayPart.Trim().ToLower();
                    if (dayNames.TryGetValue(cleanDay, out var dayOfWeek))
                    {
                        if (!recurringDays.Contains(dayOfWeek))
                            recurringDays.Add(dayOfWeek);
                    }
                }
            }

            // If no specific days, default to same day of week as today
            if (!recurringDays.Any())
            {
                recurringDays.Add(DateTime.Now.DayOfWeek);
            }

            // Extract time from message if not explicitly provided
            if (string.IsNullOrEmpty(timeStr) && !string.IsNullOrEmpty(message))
            {
                var timeExtract = ExtractTimeFromMessage(message);
                if (timeExtract.HasTime)
                {
                    timeStr = timeExtract.TimeStr;
                    message = timeExtract.RemainingMessage;
                }
            }

            if (string.IsNullOrEmpty(message) || message.Length < 2)
            {
                message = "Weekly reminder";
            }

            var timeResult = TimeParser.ParseTime($"today at {timeStr}");
            if (!timeResult.Success)
            {
                return (false, null, "", RecurrenceType.Weekly, 0, null, null, null, null, null, null, 
                       $"Could not parse time '{timeStr}'. Use format like '9am' or '14:30'.");
            }

            // Find next occurrence of the first specified day
            var targetTime = timeResult.DateTime.Value.TimeOfDay;
            var firstDay = recurringDays.OrderBy(d => d).First();
            var today = DateTime.Now;
            var daysUntilFirst = ((int)firstDay - (int)today.DayOfWeek + 7) % 7;
            
            var firstTrigger = today.Date.AddDays(daysUntilFirst).Add(targetTime);
            
            // If it's today but time has passed, move to next week
            if (daysUntilFirst == 0 && firstTrigger <= DateTime.Now)
            {
                daysUntilFirst = 7;
                firstTrigger = today.Date.AddDays(daysUntilFirst).Add(targetTime);
            }

            return (true, firstTrigger, message, RecurrenceType.Weekly, interval, recurringDays, null, null, null, null, null, "");
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType, 
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth, 
                 DayOfWeek? WeeklyDayOfWeek, DateTime? EndDate, int? MaxTriggers, string Error) ParseMonthlyReminder(string input)
        {
            // Pattern: "every [N] month[s] [on the DAY/WEEK] [at TIME] MESSAGE"
            var match = Regex.Match(input, @"(?:(\d+)\s+)?months?\s*(?:on\s+(?:the\s+)?(.+?))?\s*(?:at\s+(.+?))?\s*(.+)?", RegexOptions.IgnoreCase);
            
            var interval = 1;
            if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var parsedInterval))
            {
                interval = parsedInterval;
            }

            var daySpec = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
            var timeStr = match.Groups[3].Success ? match.Groups[3].Value.Trim() : "9:00am";
            var message = match.Groups[4].Success ? match.Groups[4].Value.Trim() : "Monthly reminder";

            int? monthlyDay = null;
            WeekOfMonth? weekOfMonth = null;
            DayOfWeek? weeklyDayOfWeek = null;

            if (!string.IsNullOrEmpty(daySpec))
            {
                // Parse different monthly specifications
                if (daySpec.Contains("last day"))
                {
                    monthlyDay = -1; // Special value for last day of month
                }
                else if (Regex.IsMatch(daySpec, @"^\d+(?:st|nd|rd|th)?$"))
                {
                    // Specific day number (1st, 2nd, 15th, etc.)
                    var dayMatch = Regex.Match(daySpec, @"^(\d+)");
                    if (dayMatch.Success && int.TryParse(dayMatch.Groups[1].Value, out var day))
                    {
                        if (day >= 1 && day <= 31)
                            monthlyDay = day;
                    }
                }
                else
                {
                    // Parse "first monday", "last friday", "second tuesday", etc.
                    var weekDayMatch = Regex.Match(daySpec, @"(first|second|third|fourth|last)\s+(\w+)", RegexOptions.IgnoreCase);
                    if (weekDayMatch.Success)
                    {
                        var weekStr = weekDayMatch.Groups[1].Value.ToLower();
                        var dayStr = weekDayMatch.Groups[2].Value.ToLower();

                        weekOfMonth = weekStr switch
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
                            {"sunday", DayOfWeek.Sunday}, {"sun", DayOfWeek.Sunday},
                            {"monday", DayOfWeek.Monday}, {"mon", DayOfWeek.Monday},
                            {"tuesday", DayOfWeek.Tuesday}, {"tue", DayOfWeek.Tuesday}, {"tues", DayOfWeek.Tuesday},
                            {"wednesday", DayOfWeek.Wednesday}, {"wed", DayOfWeek.Wednesday},
                            {"thursday", DayOfWeek.Thursday}, {"thu", DayOfWeek.Thursday}, {"thur", DayOfWeek.Thursday},
                            {"friday", DayOfWeek.Friday}, {"fri", DayOfWeek.Friday},
                            {"saturday", DayOfWeek.Saturday}, {"sat", DayOfWeek.Saturday}
                        };

                        if (dayNames.TryGetValue(dayStr, out var dayOfWeek))
                        {
                            weeklyDayOfWeek = dayOfWeek;
                        }
                    }
                }
            }

            // Default to first day of month if no specification
            if (!monthlyDay.HasValue && !weekOfMonth.HasValue)
            {
                monthlyDay = 1;
            }

            // Extract time from message if not explicitly provided
            if (string.IsNullOrEmpty(timeStr) && !string.IsNullOrEmpty(message))
            {
                var timeExtract = ExtractTimeFromMessage(message);
                if (timeExtract.HasTime)
                {
                    timeStr = timeExtract.TimeStr;
                    message = timeExtract.RemainingMessage;
                }
            }

            if (string.IsNullOrEmpty(message) || message.Length < 2)
            {
                message = "Monthly reminder";
            }

            var timeResult = TimeParser.ParseTime($"today at {timeStr}");
            if (!timeResult.Success)
            {
                return (false, null, "", RecurrenceType.Monthly, 0, null, null, null, null, null, null, 
                       $"Could not parse time '{timeStr}'. Use format like '9am' or '14:30'.");
            }

            var targetTime = timeResult.DateTime.Value.TimeOfDay;
            var now = DateTime.Now;
            var firstTrigger = CalculateFirstMonthlyTrigger(now, targetTime, monthlyDay, weekOfMonth, weeklyDayOfWeek);

            return (true, firstTrigger, message, RecurrenceType.Monthly, interval, null, monthlyDay, weekOfMonth, weeklyDayOfWeek, null, null, "");
        }

        private DateTime CalculateFirstMonthlyTrigger(DateTime now, TimeSpan targetTime, int? monthlyDay, WeekOfMonth? weekOfMonth, DayOfWeek? weeklyDayOfWeek)
        {
            var currentMonth = now.Year * 12 + now.Month - 1; // 0-based month count
            
            for (int monthOffset = 0; monthOffset < 2; monthOffset++) // Check current and next month
            {
                var targetMonth = currentMonth + monthOffset;
                var year = targetMonth / 12;
                var month = (targetMonth % 12) + 1;
                
                DateTime candidate;
                
                if (monthlyDay.HasValue)
                {
                    var day = monthlyDay.Value;
                    if (day == -1) // Last day of month
                    {
                        day = DateTime.DaysInMonth(year, month);
                    }
                    else if (day > DateTime.DaysInMonth(year, month))
                    {
                        day = DateTime.DaysInMonth(year, month);
                    }
                    
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
                {
                    return candidate;
                }
            }
            
            // Fallback to next month, first day
            var nextMonth = now.AddMonths(1);
            return new DateTime(nextMonth.Year, nextMonth.Month, 1).Add(targetTime);
        }

        private DateTime FindWeekDayInMonth(int year, int month, WeekOfMonth weekOfMonth, DayOfWeek dayOfWeek, TimeSpan time)
        {
            if (weekOfMonth == WeekOfMonth.Last)
            {
                var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                while (lastDay.DayOfWeek != dayOfWeek)
                {
                    lastDay = lastDay.AddDays(-1);
                }
                return lastDay.Add(time);
            }
            else
            {
                var firstDay = new DateTime(year, month, 1);
                var daysToTarget = ((int)dayOfWeek - (int)firstDay.DayOfWeek + 7) % 7;
                var targetDate = firstDay.AddDays(daysToTarget + (((int)weekOfMonth - 1) * 7));
                
                if (targetDate.Month != month)
                {
                    targetDate = targetDate.AddDays(-7);
                }
                
                return targetDate.Add(time);
            }
        }

        private (bool HasTime, string TimeStr, string RemainingMessage) ExtractTimeFromMessage(string message)
        {
            // Look for time patterns in the message
            var timePatterns = new[]
            {
                @"\b(\d{1,2}:\d{2}\s*(?:am|pm))\b",
                @"\b(\d{1,2}\s*(?:am|pm))\b",
                @"\b(\d{1,2}:\d{2})\b"
            };

            foreach (var pattern in timePatterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var timeStr = match.Groups[1].Value;
                    var remainingMessage = message.Replace(match.Value, "").Trim();
                    remainingMessage = Regex.Replace(remainingMessage, @"\s+", " ").Trim();
                    
                    return (true, timeStr, remainingMessage);
                }
            }

            return (false, "", message);
        }
    }
}