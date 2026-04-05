# --- START: cogs/reminders.py ---
import discord
from discord.ext import commands, tasks
import json
import os
import uuid
import re
from datetime import datetime, timedelta


REMINDERS_FILE = "reminders.json"

MONTHS = {
    "january": 1, "jan": 1, "february": 2, "feb": 2, "march": 3, "mar": 3,
    "april": 4, "apr": 4, "may": 5, "june": 6, "jun": 6, "july": 7, "jul": 7,
    "august": 8, "aug": 8, "september": 9, "sep": 9, "october": 10, "oct": 10,
    "november": 11, "nov": 11, "december": 12, "dec": 12
}

DAYS = {
    "monday": 0, "mon": 0, "tuesday": 1, "tue": 1, "tues": 1,
    "wednesday": 2, "wed": 2, "thursday": 3, "thu": 3, "thur": 3, "thurs": 3,
    "friday": 4, "fri": 4, "saturday": 5, "sat": 5, "sunday": 6, "sun": 6
}

WEEK_OF_MONTH = {"first": 1, "second": 2, "third": 3, "fourth": 4, "last": -1}


# ── Time parsing ──────────────────────────────────────────────────────────────

def parse_time_part(s):
    """Parse a time string like '3pm', '14:30', '9:00am'. Returns timedelta from midnight or None."""
    s = s.strip().lower()
    m = re.match(r'^(\d{1,2})(?::(\d{2}))?\s*(am|pm)$', s)
    if m:
        hour = int(m.group(1))
        minute = int(m.group(2) or 0)
        if m.group(3) == "pm" and hour != 12:
            hour += 12
        if m.group(3) == "am" and hour == 12:
            hour = 0
        if 0 <= hour < 24 and 0 <= minute < 60:
            return timedelta(hours=hour, minutes=minute)
    m = re.match(r'^(\d{1,2}):(\d{2})(?::(\d{2}))?$', s)
    if m:
        hour, minute = int(m.group(1)), int(m.group(2))
        if 0 <= hour < 24 and 0 <= minute < 60:
            return timedelta(hours=hour, minutes=minute)
    if re.match(r'^\d+$', s):
        hour = int(s)
        if 0 <= hour < 24:
            return timedelta(hours=hour)
    return None


def parse_relative(text):
    """Parse 'in X hours Y minutes' style. Returns datetime or None."""
    now = datetime.now()
    delta = timedelta()
    found = False
    for pattern, fn in [
        (r'(\d+)\s*(?:days?|d)\b', lambda v: timedelta(days=v)),
        (r'(\d+)\s*(?:hours?|hrs?|h)\b', lambda v: timedelta(hours=v)),
        (r'(\d+)\s*(?:minutes?|mins?|m)\b', lambda v: timedelta(minutes=v)),
        (r'(\d+)\s*(?:seconds?|secs?|s)\b', lambda v: timedelta(seconds=v)),
    ]:
        for m in re.finditer(pattern, text, re.IGNORECASE):
            delta += fn(int(m.group(1)))
            found = True
    if found and delta.total_seconds() > 0:
        return now + delta
    return None


def parse_absolute(text):
    """Parse absolute date/time expressions. Returns datetime or None."""
    now = datetime.now()
    text = text.strip().lower()

    if text == "tomorrow":
        return now.replace(hour=9, minute=0, second=0, microsecond=0) + timedelta(days=1)
    if text == "today":
        t = now.replace(minute=0, second=0, microsecond=0) + timedelta(hours=1)
        return t

    # Split on "at"
    parts = re.split(r'\s+at\s+', text, maxsplit=1)
    date_part = parts[0].strip()
    time_part = parts[1].strip() if len(parts) > 1 else None

    result = now
    date_set = False

    # Day name (monday, friday, etc.)
    for day_name, day_num in DAYS.items():
        if date_part == day_name or date_part.endswith(" " + day_name):
            today_num = now.weekday()
            diff = (day_num - today_num) % 7 or 7
            result = now.date() + timedelta(days=diff)
            result = datetime(result.year, result.month, result.day)
            date_set = True
            break

    # Month + day (dec 25, 25 dec)
    if not date_set:
        m = re.match(r'(\w+)\s+(\d+)$', date_part) or re.match(r'(\d+)\s+(\w+)$', date_part)
        if m:
            a, b = m.group(1), m.group(2)
            month_str, day_str = (a, b) if a in MONTHS else (b, a)
            if month_str in MONTHS:
                month = MONTHS[month_str]
                day = int(day_str)
                year = now.year
                try:
                    candidate = datetime(year, month, day)
                    if candidate.date() < now.date():
                        candidate = datetime(year + 1, month, day)
                    result = candidate
                    date_set = True
                except ValueError:
                    pass

    # Numeric date (12/25)
    if not date_set:
        m = re.match(r'(\d+)[\/\-](\d+)(?:[\/\-](\d+))?', date_part)
        if m:
            try:
                mo, d = int(m.group(1)), int(m.group(2))
                yr = int(m.group(3)) if m.group(3) else now.year
                candidate = datetime(yr, mo, d)
                if candidate.date() < now.date():
                    candidate = datetime(yr + 1, mo, d)
                result = candidate
                date_set = True
            except ValueError:
                pass

    # tomorrow / today in date_part
    if not date_set:
        if "tomorrow" in date_part:
            result = datetime(now.year, now.month, now.day) + timedelta(days=1)
            date_set = True
        elif "today" in date_part:
            result = datetime(now.year, now.month, now.day)
            date_set = True

    # Parse time part
    if time_part:
        td = parse_time_part(time_part)
        if td is None:
            return None
        result = datetime(result.year, result.month, result.day) + td
    elif not date_set:
        # Try to parse the whole thing as a time
        td = parse_time_part(text)
        if td:
            result = datetime(now.year, now.month, now.day) + td
            if result <= now:
                result += timedelta(days=1)
            return result
        return None
    else:
        if result.hour == 0 and result.minute == 0:
            result = result.replace(hour=9)

    if not date_set and result <= now:
        result += timedelta(days=1)

    return result if result > now else None


def parse_reminder_input(text):
    """Returns (datetime, message) or (None, error_string)."""
    text = text.strip()
    lower = text.lower()

    # Relative: starts with "in "
    if lower.startswith("in "):
        # Find where the message starts by trying progressively longer time strings
        dt = None
        msg = ""
        words = text.split()
        for i in range(2, len(words) + 1):
            candidate = " ".join(words[:i])
            parsed = parse_relative(candidate)
            if parsed:
                dt = parsed
                msg = " ".join(words[i:])
        if dt:
            return dt, msg or "Reminder"
        return None, "Could not parse time. Try: `in 2 hours take a break`"

    # Absolute with "at" somewhere
    # Try splitting on "at" to find where message starts
    at_match = re.search(r'\bat\s+\d', lower)
    if at_match or re.search(r'\d+(am|pm)', lower):
        # Find time expression end
        # Strategy: try each word boundary as message start
        words = text.split()
        for i in range(len(words), 0, -1):
            time_candidate = " ".join(words[:i])
            msg_candidate = " ".join(words[i:])
            dt = parse_absolute(time_candidate)
            if dt and dt > datetime.now():
                return dt, msg_candidate or "Reminder"

    # Try plain absolute
    words = text.split()
    for i in range(len(words), 0, -1):
        time_candidate = " ".join(words[:i])
        msg_candidate = " ".join(words[i:])
        dt = parse_absolute(time_candidate)
        if dt and dt > datetime.now():
            return dt, msg_candidate or "Reminder"

    return None, "Could not parse reminder. Try: `remindme in 2 hours take a break` or `remindme at 3pm call mom`"


# ── Persistence ───────────────────────────────────────────────────────────────

def load_reminders():
    if os.path.exists(REMINDERS_FILE):
        with open(REMINDERS_FILE, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    return []


def save_reminders(data):
    with open(REMINDERS_FILE, "w") as f:
        json.dump(data, f, indent=2, default=str)


def reminder_to_dict(r):
    return {
        "id": r["id"],
        "user_id": r["user_id"],
        "channel_id": r["channel_id"],
        "guild_id": r["guild_id"],
        "trigger_time": r["trigger_time"].isoformat() if isinstance(r["trigger_time"], datetime) else r["trigger_time"],
        "message": r["message"],
        "created_at": r["created_at"].isoformat() if isinstance(r["created_at"], datetime) else r["created_at"],
        "is_triggered": r["is_triggered"],
        "is_recurring": r["is_recurring"],
        "recurrence_type": r.get("recurrence_type"),
        "recurrence_interval": r.get("recurrence_interval", 1),
        "recurrence_days": r.get("recurrence_days", []),
        "monthly_day": r.get("monthly_day"),
        "week_of_month": r.get("week_of_month"),
        "weekly_day_of_week": r.get("weekly_day_of_week"),
        "trigger_count": r.get("trigger_count", 0),
        "image_url": r.get("image_url"),
    }


def dict_to_reminder(d):
    d = dict(d)
    d["trigger_time"] = datetime.fromisoformat(d["trigger_time"])
    d["created_at"] = datetime.fromisoformat(d["created_at"])
    return d


# ── Next trigger calculation ──────────────────────────────────────────────────

def find_week_day_in_month(year, month, week_of_month, day_of_week, time_of_day):
    """week_of_month: 1-4 or -1 for last. day_of_week: 0=Mon...6=Sun"""
    import calendar
    if week_of_month == -1:
        last_day = calendar.monthrange(year, month)[1]
        d = datetime(year, month, last_day)
        while d.weekday() != day_of_week:
            d -= timedelta(days=1)
    else:
        first = datetime(year, month, 1)
        diff = (day_of_week - first.weekday()) % 7
        d = first + timedelta(days=diff + (week_of_month - 1) * 7)
        if d.month != month:
            d -= timedelta(weeks=1)
    return datetime(d.year, d.month, d.day) + time_of_day


def calculate_next_trigger(reminder):
    current = reminder["trigger_time"]
    rt = reminder.get("recurrence_type")
    interval = reminder.get("recurrence_interval", 1)

    if rt == "daily":
        return current + timedelta(days=interval)

    elif rt == "weekly":
        days = reminder.get("recurrence_days", [])
        if days:
            current_dow = current.weekday()
            # Find next day in the same week
            next_in_week = [d for d in days if d > current_dow]
            if next_in_week:
                diff = next_in_week[0] - current_dow
                return datetime(current.year, current.month, current.day) + timedelta(days=diff) + timedelta(hours=current.hour, minutes=current.minute)
            # Wrap to next interval week
            first_day = min(days)
            diff = (first_day - current_dow) % 7 or 7
            diff += (interval - 1) * 7
            return datetime(current.year, current.month, current.day) + timedelta(days=diff) + timedelta(hours=current.hour, minutes=current.minute)
        return current + timedelta(weeks=interval)

    elif rt == "monthly":
        tod = timedelta(hours=current.hour, minutes=current.minute)
        next_month = current.month + interval
        next_year = current.year + (next_month - 1) // 12
        next_month = ((next_month - 1) % 12) + 1
        md = reminder.get("monthly_day")
        wom = reminder.get("week_of_month")
        wdow = reminder.get("weekly_day_of_week")
        if md is not None:
            import calendar
            day = calendar.monthrange(next_year, next_month)[1] if md == -1 else min(md, calendar.monthrange(next_year, next_month)[1])
            return datetime(next_year, next_month, day) + tod
        elif wom is not None and wdow is not None:
            return find_week_day_in_month(next_year, next_month, wom, wdow, tod)
        return current.replace(month=next_month, year=next_year)

    return None


# ── Cog ───────────────────────────────────────────────────────────────────────

class Reminders(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        raw = load_reminders()
        self.reminders = [dict_to_reminder(r) for r in raw]
        self._cleanup_old()
        self.check_reminders.start()

    def cog_unload(self):
        self.check_reminders.cancel()

    def _cleanup_old(self):
        cutoff = datetime.now() - timedelta(days=1)
        self.reminders = [
            r for r in self.reminders
            if r["is_recurring"] or not r["is_triggered"] or r["trigger_time"] > cutoff
        ]
        self._save()

    def _save(self):
        save_reminders([reminder_to_dict(r) for r in self.reminders])

    def _new_reminder(self, user_id, channel_id, guild_id, trigger_time, message, recurring=False, **kwargs):
        r = {
            "id": str(uuid.uuid4()),
            "user_id": user_id,
            "channel_id": channel_id,
            "guild_id": guild_id,
            "trigger_time": trigger_time,
            "message": message,
            "created_at": datetime.now(),
            "is_triggered": False,
            "is_recurring": recurring,
            "recurrence_type": kwargs.get("recurrence_type"),
            "recurrence_interval": kwargs.get("recurrence_interval", 1),
            "recurrence_days": kwargs.get("recurrence_days", []),
            "monthly_day": kwargs.get("monthly_day"),
            "week_of_month": kwargs.get("week_of_month"),
            "weekly_day_of_week": kwargs.get("weekly_day_of_week"),
            "trigger_count": 0,
            "image_url": kwargs.get("image_url"),
        }
        self.reminders.append(r)
        self._save()
        return r

    @tasks.loop(seconds=30)
    async def check_reminders(self):
        now = datetime.now()
        for r in list(self.reminders):
            if r["trigger_time"] <= now and (not r["is_triggered"] or r["is_recurring"]):
                await self._trigger(r)
                if r["is_recurring"]:
                    nxt = calculate_next_trigger(r)
                    if nxt:
                        r["trigger_time"] = nxt
                        r["trigger_count"] = r.get("trigger_count", 0) + 1
                    else:
                        r["is_triggered"] = True
                else:
                    r["is_triggered"] = True
        self._save()

    async def _trigger(self, r):
        try:
            guild = self.bot.get_guild(r["guild_id"])
            channel = guild.get_channel(r["channel_id"]) if guild else None
            if not guild or not channel:
                return
            user = guild.get_member(r["user_id"]) or await guild.fetch_member(r["user_id"])
            if not user:
                return
            embed = discord.Embed(
                title="🔄 Recurring Reminder!" if r["is_recurring"] else "⏰ Reminder!",
                description=r["message"],
                color=discord.Color.blue() if r["is_recurring"] else discord.Color.orange()
            )
            created = r["created_at"]
            count = r.get("trigger_count", 0)
            embed.set_footer(text=f"Set on {created.strftime('%b %d, %Y at %I:%M %p')}" +
                             (f" • Trigger #{count + 1}" if r["is_recurring"] else ""))
            embed.timestamp = datetime.now()
            if r.get("image_url"):
                embed.set_image(url=r["image_url"])
            if r["is_recurring"]:
                nxt = calculate_next_trigger(r)
                if nxt:
                    embed.add_field(name="Next Reminder", value=nxt.strftime("%b %d, %Y at %I:%M %p"), inline=True)
                else:
                    embed.add_field(name="Status", value="This was the final reminder", inline=True)
            await channel.send(f"{user.mention}", embed=embed)
        except Exception as e:
            print(f"[ERROR] Failed to trigger reminder: {e}")

    def _format_recurrence(self, r):
        rt = r.get("recurrence_type")
        interval = r.get("recurrence_interval", 1)
        if rt == "daily":
            return "Daily" if interval == 1 else f"Every {interval} days"
        if rt == "weekly":
            days = r.get("recurrence_days", [])
            day_names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
            if days:
                ds = ", ".join(day_names[d] for d in days)
                return f"Weekly on {ds}" if interval == 1 else f"Every {interval} weeks on {ds}"
            return "Weekly" if interval == 1 else f"Every {interval} weeks"
        if rt == "monthly":
            md = r.get("monthly_day")
            wom = r.get("week_of_month")
            wdow = r.get("weekly_day_of_week")
            day_names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
            wom_names = {1: "first", 2: "second", 3: "third", 4: "fourth", -1: "last"}
            if md is not None:
                d = "last day" if md == -1 else f"day {md}"
                return f"Monthly on {d}" if interval == 1 else f"Every {interval} months on {d}"
            if wom is not None and wdow is not None:
                return f"Monthly on {wom_names.get(wom, '')} {day_names[wdow]}"
        return "Recurring"

    # ── Commands ──────────────────────────────────────────────────────────

    @commands.command()
    async def remindme(self, ctx, *, args: str = None):
        if not args:
            await ctx.send(
                "Usage: `remindme [time] [message]`\n"
                "Examples:\n"
                "• `remindme in 2 hours take a break`\n"
                "• `remindme at 3pm call mom`\n"
                "• `remindme tomorrow at 9am meeting`\n"
                "• `remindme Dec 25 Christmas!`"
            )
            return
        if not ctx.guild:
            await ctx.send("❌ Reminders can only be set in server channels!")
            return
        dt, msg = parse_reminder_input(args)
        if dt is None:
            await ctx.send(f"❌ {msg}")
            return
        image_url = ctx.message.attachments[0].url if ctx.message.attachments else None
        r = self._new_reminder(ctx.author.id, ctx.channel.id, ctx.guild.id, dt, msg, image_url=image_url)
        time_until = dt - datetime.now()
        days = time_until.days
        hours, rem = divmod(time_until.seconds, 3600)
        minutes = rem // 60
        parts = []
        if days: parts.append(f"{days} day{'s' if days != 1 else ''}")
        if hours: parts.append(f"{hours} hour{'s' if hours != 1 else ''}")
        if minutes: parts.append(f"{minutes} minute{'s' if minutes != 1 else ''}")
        time_str = f"in {' and '.join(parts)}" if parts else "shortly"
        await ctx.send(f"✅ Reminder set! I'll remind you {time_str} on {dt.strftime('%b %d, %Y at %I:%M %p')}")

    @commands.command(name="remind")
    async def remind_me(self, ctx, me: str = None, *, args: str = None):
        """Handles 'remind me ...' syntax"""
        if me and me.lower() == "me" and args:
            await ctx.invoke(self.remindme, args=args)
        else:
            await ctx.send("Usage: `remind me [time] [message]`")

    @commands.command()
    async def every(self, ctx, *, args: str = None):
        if not args:
            await ctx.send(
                "Usage: `every [interval] [time] [message]`\n"
                "Examples:\n"
                "• `every day at 9am take vitamins`\n"
                "• `every week on monday at 2pm team meeting`\n"
                "• `every month on the 15th pay bills`\n"
                "• `every 2 weeks on tuesday and friday standup`\n"
                "• `every month on the first monday review goals`"
            )
            return
        if not ctx.guild:
            await ctx.send("❌ Reminders can only be set in server channels!")
            return
        result = self._parse_every(args.lower().strip())
        if result is None:
            await ctx.send("❌ Could not parse recurring reminder. Use: `every day/week/month [at time] [message]`")
            return
        first_trigger, message, kwargs = result
        image_url = ctx.message.attachments[0].url if ctx.message.attachments else None
        r = self._new_reminder(ctx.author.id, ctx.channel.id, ctx.guild.id, first_trigger, message, recurring=True, image_url=image_url, **kwargs)
        desc = self._format_recurrence(r)
        await ctx.send(f"✅ Recurring reminder set! {desc}. Starting {first_trigger.strftime('%b %d, %Y at %I:%M %p')}")

    def _parse_every(self, text):
        """Returns (first_trigger, message, kwargs) or None."""
        now = datetime.now()

        # Determine recurrence type and extract interval
        interval = 1
        m = re.match(r'^(\d+)\s+(days?|weeks?|months?)', text)
        if m:
            interval = int(m.group(1))
            text = text[m.end():].strip()
            unit = m.group(2).rstrip('s')
        else:
            m = re.match(r'^(days?|weeks?|months?)', text)
            if not m:
                return None
            unit = m.group(1).rstrip('s')
            text = text[m.end():].strip()

        if unit == "day":
            return self._parse_daily(text, now, interval)
        elif unit == "week":
            return self._parse_weekly(text, now, interval)
        elif unit == "month":
            return self._parse_monthly(text, now, interval)
        return None

    def _extract_time_and_message(self, text, default_time="9:00am"):
        """Split 'at TIME message' or just 'message'. Returns (time_str, message)."""
        m = re.match(r'^(?:at\s+)?(\d{1,2}(?::\d{2})?\s*(?:am|pm)|\d{1,2}:\d{2})\s*(.*)', text, re.IGNORECASE)
        if m:
            return m.group(1).strip(), m.group(2).strip()
        # Try "on ... at TIME message"
        m = re.search(r'\bat\s+(\d{1,2}(?::\d{2})?\s*(?:am|pm)|\d{1,2}:\d{2})\s*(.*)', text, re.IGNORECASE)
        if m:
            before_at = text[:m.start()].strip()
            return m.group(1).strip(), (before_at + " " + m.group(2).strip()).strip()
        return default_time, text

    def _parse_daily(self, text, now, interval):
        time_str, message = self._extract_time_and_message(text)
        td = parse_time_part(time_str)
        if td is None:
            td = timedelta(hours=9)
        message = message or "Daily reminder"
        first = datetime(now.year, now.month, now.day) + td
        if first <= now:
            first += timedelta(days=1)
        return first, message, {"recurrence_type": "daily", "recurrence_interval": interval}

    def _parse_weekly(self, text, now, interval):
        # Extract "on DAY(s)"
        days = []
        on_match = re.match(r'^on\s+(.+?)(?:\s+at\s+|\s+\d|$)', text, re.IGNORECASE)
        if on_match:
            days_text = on_match.group(1)
            for sep in [",", " and ", "&", "+"]:
                days_text = days_text.replace(sep, " ")
            for word in days_text.split():
                if word in DAYS:
                    d = DAYS[word]
                    if d not in days:
                        days.append(d)
            text = text[on_match.end():].strip() if on_match.end() < len(text) else ""
        
        time_str, message = self._extract_time_and_message(text)
        td = parse_time_part(time_str)
        if td is None:
            td = timedelta(hours=9)
        message = message or "Weekly reminder"

        if not days:
            days = [now.weekday()]

        # Find first trigger
        target_days = sorted(days)
        first = None
        for d in target_days:
            diff = (d - now.weekday()) % 7
            candidate = datetime(now.year, now.month, now.day) + timedelta(days=diff) + td
            if candidate > now:
                first = candidate
                break
        if first is None:
            diff = (target_days[0] - now.weekday()) % 7 or 7
            first = datetime(now.year, now.month, now.day) + timedelta(days=diff) + td

        return first, message, {"recurrence_type": "weekly", "recurrence_interval": interval, "recurrence_days": days}

    def _parse_monthly(self, text, now, interval):
        import calendar
        monthly_day = None
        week_of_month = None
        weekly_day_of_week = None

        # Remove "on the" prefix
        text = re.sub(r'^on\s+(?:the\s+)?', '', text, flags=re.IGNORECASE).strip()

        # "last day"
        if re.match(r'^last\s+day', text, re.IGNORECASE):
            monthly_day = -1
            text = re.sub(r'^last\s+day(?:\s+of\s+\w+)?\s*', '', text, flags=re.IGNORECASE)

        # "Nth" specific day
        elif re.match(r'^\d+(?:st|nd|rd|th)?(?:\s|$)', text, re.IGNORECASE):
            m = re.match(r'^(\d+)', text)
            monthly_day = int(m.group(1))
            text = text[m.end():].strip().lstrip('stndrh').strip()

        # "first/second/third/fourth/last DAYNAME"
        else:
            m = re.match(r'^(first|second|third|fourth|last)\s+(\w+)', text, re.IGNORECASE)
            if m:
                wom_str = m.group(1).lower()
                day_str = m.group(2).lower()
                if wom_str in WEEK_OF_MONTH and day_str in DAYS:
                    week_of_month = WEEK_OF_MONTH[wom_str]
                    weekly_day_of_week = DAYS[day_str]
                    text = text[m.end():].strip()

        if monthly_day is None and week_of_month is None:
            monthly_day = 1

        time_str, message = self._extract_time_and_message(text)
        td = parse_time_part(time_str)
        if td is None:
            td = timedelta(hours=9)
        message = message or "Monthly reminder"

        # Calculate first trigger
        first = None
        for month_offset in range(2):
            mo = now.month + month_offset
            yr = now.year + (mo - 1) // 12
            mo = ((mo - 1) % 12) + 1
            try:
                if monthly_day is not None:
                    d = calendar.monthrange(yr, mo)[1] if monthly_day == -1 else min(monthly_day, calendar.monthrange(yr, mo)[1])
                    candidate = datetime(yr, mo, d) + td
                elif week_of_month is not None:
                    candidate = find_week_day_in_month(yr, mo, week_of_month, weekly_day_of_week, td)
                else:
                    candidate = datetime(yr, mo, 1) + td
                if candidate > now:
                    first = candidate
                    break
            except Exception:
                continue

        if first is None:
            nm = now.replace(day=1) + timedelta(days=32)
            first = datetime(nm.year, nm.month, 1) + td

        return first, message, {
            "recurrence_type": "monthly",
            "recurrence_interval": interval,
            "monthly_day": monthly_day,
            "week_of_month": week_of_month,
            "weekly_day_of_week": weekly_day_of_week,
        }

    @commands.command()
    async def myreminders(self, ctx):
        active = [
            r for r in self.reminders
            if r["user_id"] == ctx.author.id and (not r["is_triggered"] or r["is_recurring"])
        ]
        active.sort(key=lambda r: r["trigger_time"])
        if not active:
            await ctx.send("📅 You have no active reminders.")
            return
        embed = discord.Embed(title="📅 Your Active Reminders", color=discord.Color.blue())
        embed.timestamp = datetime.now()
        for r in active[:10]:
            diff = r["trigger_time"] - datetime.now()
            total_min = int(diff.total_seconds() / 60)
            if diff.days > 1:
                time_desc = f"in {diff.days} days"
            elif diff.seconds > 3600:
                time_desc = f"in {diff.seconds // 3600} hours"
            else:
                time_desc = f"in {max(total_min, 1)} minutes"
            title = (
                f"🔄 {r['trigger_time'].strftime('%b %d, %I:%M %p')} ({self._format_recurrence(r)})"
                if r["is_recurring"]
                else f"⏰ {r['trigger_time'].strftime('%b %d, %I:%M %p')}"
            )
            desc = f"**{r['message']}**\n{time_desc}"
            if r["is_recurring"]:
                desc += f" • Triggered {r.get('trigger_count', 0)} times"
            desc += f" • ID: `{r['id'][:8]}`"
            embed.add_field(name=title, value=desc, inline=False)
        if len(active) > 10:
            embed.set_footer(text=f"Showing first 10 of {len(active)} reminders")
        await ctx.send(embed=embed)

    @commands.command()
    async def cancelreminder(self, ctx, reminder_id: str = None):
        if not reminder_id:
            await ctx.send("Usage: `cancelreminder [id]`\nUse `myreminders` to see your reminder IDs.")
            return
        match = next(
            (r for r in self.reminders if r["user_id"] == ctx.author.id and r["id"].startswith(reminder_id)),
            None
        )
        if match:
            self.reminders.remove(match)
            self._save()
            await ctx.send(f"✅ Reminder `{reminder_id}` cancelled.")
        else:
            await ctx.send(f"❌ No reminder found with ID `{reminder_id}`. Use `myreminders` to see your IDs.")


def setup(bot):
    bot.add_cog(Reminders(bot))
# --- END: cogs/reminders.py ---