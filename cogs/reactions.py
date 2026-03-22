# --- START: cogs/reactions.py ---
import discord
from discord.ext import commands
import json
import os
import aiohttp


REACTIONS_FILE = "reactions.json"
ALIASES_FILE = "aliases.json"
REACTIONS_FOLDER = "reactions"


def load_json(path):
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    return {}


def save_json(path, data):
    with open(path, "w") as f:
        json.dump(data, f, indent=2)


class Reactions(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        os.makedirs(REACTIONS_FOLDER, exist_ok=True)
        self.reactions = load_json(REACTIONS_FILE)
        self.aliases = load_json(ALIASES_FILE)

    # ── Listener: send reaction file if message matches a reaction/alias ──
    @commands.Cog.listener()
    async def on_message(self, message):
        if message.author.bot:
            return
        content = message.content.lower().strip()
        # Don't intercept commands
        if content.startswith(self.bot.command_prefix.lower()):
            return
        if content in self.reactions:
            await self._send_reaction(message, self.reactions[content])
        elif content in self.aliases and self.aliases[content] in self.reactions:
            await self._send_reaction(message, self.reactions[self.aliases[content]])

    async def _send_reaction(self, message, filename):
        path = os.path.join(REACTIONS_FOLDER, filename)
        if os.path.exists(path):
            await message.channel.send(file=discord.File(path))
        else:
            await message.channel.send("Reaction file not found!")

    # ── Commands ──────────────────────────────────────────────────────────

    @commands.command()
    async def addreaction(self, ctx, name: str = None):
        if not name:
            await ctx.send("Usage: `addreaction [name]` — attach a file")
            return
        name = name.lower()
        if not ctx.message.attachments:
            await ctx.send("Please attach a file!")
            return
        attachment = ctx.message.attachments[0]
        ext = os.path.splitext(attachment.filename)[1]
        filename = f"{name}{ext}"
        path = os.path.join(REACTIONS_FOLDER, filename)
        async with aiohttp.ClientSession() as session:
            async with session.get(attachment.url) as resp:
                data = await resp.read()
        with open(path, "wb") as f:
            f.write(data)
        self.reactions[name] = filename
        save_json(REACTIONS_FILE, self.reactions)
        await ctx.send(f"✅ Reaction `{name}` saved! Type `{name}` to use it.")

    @commands.command()
    async def deletereaction(self, ctx, name: str = None):
        if not name:
            await ctx.send("Usage: `deletereaction [name]`")
            return
        name = name.lower()
        if name not in self.reactions:
            await ctx.send(f"Reaction `{name}` not found!")
            return
        path = os.path.join(REACTIONS_FOLDER, self.reactions[name])
        if os.path.exists(path):
            os.remove(path)
        # Remove all aliases pointing to this reaction
        to_remove = [k for k, v in self.aliases.items() if v == name]
        for k in to_remove:
            del self.aliases[k]
        del self.reactions[name]
        save_json(REACTIONS_FILE, self.reactions)
        save_json(ALIASES_FILE, self.aliases)
        await ctx.send(f"✅ Reaction `{name}` deleted!")

    @commands.command()
    async def changereaction(self, ctx, name: str = None):
        if not name:
            await ctx.send("Usage: `changereaction [name]` — attach a new file")
            return
        name = name.lower()
        if name not in self.reactions:
            await ctx.send(f"Reaction `{name}` not found!")
            return
        if not ctx.message.attachments:
            await ctx.send("Please attach a file!")
            return
        old_path = os.path.join(REACTIONS_FOLDER, self.reactions[name])
        if os.path.exists(old_path):
            os.remove(old_path)
        attachment = ctx.message.attachments[0]
        ext = os.path.splitext(attachment.filename)[1]
        filename = f"{name}{ext}"
        path = os.path.join(REACTIONS_FOLDER, filename)
        async with aiohttp.ClientSession() as session:
            async with session.get(attachment.url) as resp:
                data = await resp.read()
        with open(path, "wb") as f:
            f.write(data)
        self.reactions[name] = filename
        save_json(REACTIONS_FILE, self.reactions)
        await ctx.send(f"✅ Reaction `{name}` updated!")

    @commands.command()
    async def renamereaction(self, ctx, old: str = None, new: str = None):
        if not old or not new:
            await ctx.send("Usage: `renamereaction [old_name] [new_name]`")
            return
        old, new = old.lower(), new.lower()
        if old not in self.reactions:
            await ctx.send(f"Reaction `{old}` not found!")
            return
        if new in self.reactions:
            await ctx.send(f"Reaction `{new}` already exists!")
            return
        self.reactions[new] = self.reactions.pop(old)
        for k, v in self.aliases.items():
            if v == old:
                self.aliases[k] = new
        save_json(REACTIONS_FILE, self.reactions)
        save_json(ALIASES_FILE, self.aliases)
        await ctx.send(f"✅ Reaction renamed `{old}` → `{new}`!")

    @commands.command(name="reactions")
    async def list_reactions(self, ctx):
        if not self.reactions:
            await ctx.send("No saved reactions found!")
            return
        embed = discord.Embed(
            title="Saved Reactions",
            description=f"Found {len(self.reactions)} saved reactions:",
            color=discord.Color.purple()
        )
        for name, filename in self.reactions.items():
            aliases = [k for k, v in self.aliases.items() if v == name]
            alias_text = f" (Aliases: {', '.join(aliases)})" if aliases else ""
            embed.add_field(name=f"`{name}`", value=f"File: {filename}{alias_text}", inline=True)
        await ctx.send(embed=embed)

    @commands.command()
    async def addalias(self, ctx, reaction: str = None, *alias_names):
        if not reaction or not alias_names:
            await ctx.send("Usage: `addalias [reaction] [alias1] [alias2]...`")
            return
        reaction = reaction.lower()
        if reaction not in self.reactions:
            await ctx.send(f"Reaction `{reaction}` not found!")
            return
        added, skipped = [], []
        for alias in alias_names:
            alias = alias.lower()
            if alias in self.aliases or alias in self.reactions:
                skipped.append(alias)
            else:
                self.aliases[alias] = reaction
                added.append(alias)
        if added:
            save_json(ALIASES_FILE, self.aliases)
            await ctx.send(f"✅ Added aliases for `{reaction}`: {', '.join(added)}")
        if skipped:
            await ctx.send(f"⚠️ Skipped existing: {', '.join(skipped)}")

    @commands.command()
    async def removealias(self, ctx, alias: str = None):
        if not alias:
            await ctx.send("Usage: `removealias [alias]`")
            return
        alias = alias.lower()
        if alias not in self.aliases:
            await ctx.send(f"Alias `{alias}` not found!")
            return
        reaction = self.aliases.pop(alias)
        save_json(ALIASES_FILE, self.aliases)
        await ctx.send(f"✅ Alias `{alias}` removed from reaction `{reaction}`!")

    @commands.command()
    async def renamealias(self, ctx, old: str = None, new: str = None):
        if not old or not new:
            await ctx.send("Usage: `renamealias [old_alias] [new_alias]`")
            return
        old, new = old.lower(), new.lower()
        if old not in self.aliases:
            await ctx.send(f"Alias `{old}` not found!")
            return
        if new in self.aliases:
            await ctx.send(f"Alias `{new}` already exists!")
            return
        self.aliases[new] = self.aliases.pop(old)
        save_json(ALIASES_FILE, self.aliases)
        await ctx.send(f"✅ Alias renamed `{old}` → `{new}`!")


def setup(bot):
    bot.add_cog(Reactions(bot))
# --- END: cogs/reactions.py ---