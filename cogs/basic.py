# --- START: cogs/basic.py ---
import discord
from discord.ext import commands


class Basic(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    @commands.Cog.listener()
    async def on_message(self, message):
        if message.author.bot:
            return
        content = message.content.lower()
        if "good peeb" in content:
            await message.add_reaction("😊")
            await message.channel.send("Thank you! 😊")
        elif "bad peeb" in content:
            await message.add_reaction("😢")
            await message.channel.send("I'm sorry... I'll try to do better! 😢")

    @commands.command()
    async def version(self, ctx):
        await ctx.send("Peeb Bot v2.0.0")

    @commands.command()
    async def ping(self, ctx):
        await ctx.send("Pong! 🏓")

    @commands.command()
    async def hello(self, ctx):
        await ctx.send(f"Hello {ctx.author.mention}! 👋")

    @commands.command()
    async def info(self, ctx):
        guild = ctx.guild
        if not guild:
            await ctx.send("This command only works in servers!")
            return
        embed = discord.Embed(title=f"Server Info: {guild.name}", color=discord.Color.green())
        embed.add_field(name="Members", value=guild.member_count, inline=True)
        embed.add_field(name="Created", value=guild.created_at.strftime("%Y-%m-%d"), inline=True)
        embed.add_field(name="Owner", value=str(guild.owner) if guild.owner else "Unknown", inline=True)
        if guild.icon:
            embed.set_thumbnail(url=guild.icon.url)
        await ctx.send(embed=embed)

    @commands.command(name="help")
    async def help_command(self, ctx):
        p = self.bot.command_prefix
        embed = discord.Embed(
            title="🤖 Peeb Bot Commands",
            description="Here are all the available commands:",
            color=discord.Color.blue()
        )
        embed.add_field(name="**🔧 Basic Commands**",
            value=(
                f"`{p}ping` - Check if bot is responsive\n"
                f"`{p}hello` - Say hello\n"
                f"`{p}info` - Get server information\n"
                f"`{p}help` - Show this help message"
            ), inline=False)
        embed.add_field(name="**😄 Reaction Commands**",
            value=(
                f"`{p}addreaction [name]` - Save attached file as reaction\n"
                f"`{p}deletereaction [name]` - Delete saved reaction\n"
                f"`{p}changereaction [name]` - Replace reaction with new file\n"
                f"`{p}renamereaction [old] [new]` - Rename reaction\n"
                f"`{p}reactions` - List all saved reactions"
            ), inline=False)
        embed.add_field(name="**🔗 Alias Commands**",
            value=(
                f"`{p}addalias [reaction] [alias1] [alias2]...` - Add aliases\n"
                f"`{p}removealias [alias]` - Remove an alias\n"
                f"`{p}renamealias [old] [new]` - Rename an alias"
            ), inline=False)
        embed.add_field(name="**⏰ One-Time Reminders**",
            value=(
                f"`{p}remindme [time] [message]` - Set a reminder\n"
                f"`{p}myreminders` - List your active reminders\n"
                f"`{p}cancelreminder [id]` - Cancel a reminder"
            ), inline=False)
        embed.add_field(name="**🔄 Recurring Reminders**",
            value=(
                f"`{p}every day [at time] [message]` - Daily reminders\n"
                f"`{p}every week [on day(s)] [at time] [message]` - Weekly reminders\n"
                f"`{p}every month [on day] [at time] [message]` - Monthly reminders\n"
                f"`{p}every [N] days/weeks/months` - Custom intervals"
            ), inline=False)
        embed.add_field(name="**📝 Examples**",
            value=(
                f"`{p}remindme in 2 hours take a break`\n"
                f"`{p}remindme at 3pm call mom`\n"
                f"`{p}every day at 9am take vitamins`\n"
                f"`{p}every week on monday at 2pm team meeting`\n"
                f"`{p}every month on the 15th pay bills`"
            ), inline=False)
        embed.set_footer(text="💡 Type reaction names directly (like 'thanks') to use saved reactions!")
        await ctx.send(embed=embed)


def setup(bot):
    bot.add_cog(Basic(bot))
# --- END: cogs/basic.py ---