---
title: Ollama and llama.cpp accounts
parent: Accounts
nav_order: 4
summary: "An Ollama or llama.cpp account runs Claude Code on a model your own computer serves, for nothing and with no Claude account."
---

# Ollama and llama.cpp accounts

{% include unreleased.html %}

An Ollama or llama.cpp account runs a tab's Claude Code on a model your own computer serves, through [Ollama](https://ollama.com) or llama.cpp's `llama-server`. Claude Code is still the agent: its tools, permission prompts, subagents and the rest are as ever, but its model is yours, so nothing is spent and nothing needs a Claude account. You choose where it's used as for any account: [a folder's account](a-folders-account.md), or [moving a tab](moving-a-tab-to-another-account.md).

{% include shot.html name="guide/accounts/ollama-account" alt="Settings → Claude Code → Accounts, with an Ollama account named Workstation opened for Change settings…: what an Ollama account is, the server's address with Check, the model, the window, where Claude Code compacts the conversation, the longest reply, and starting Ollama when it isn't running" %}

Anthropic doesn't support Claude Code on models other than Claude. How well it works depends on the model you choose: a small one follows Claude Code's tools less well, and Deventry's permission prompts matter on it as much as on Claude. What goes wrong is the model's or the server's to fix, not Claude Code's.

## What you need

- **Ollama** or **llama.cpp's `llama-server`**, installed. Deventry doesn't install either, but it can start one that's installed when it isn't running.
- **A model that can use tools.** Claude Code does everything through its tools, so a model that can't use them can't be chosen. Ollama's library marks the ones that can.
- **A window of 64,000 tokens or more.** A model's window is how much of the conversation it can read at once. Claude Code's own instructions and tools take about 15,000 before you've said anything, and it keeps room for the longest reply. Under 32,000 tokens, Claude Code would compact the conversation before every turn, so the account can't be saved.

## Adding an account

1. Open Settings → Claude Code → Accounts.
2. Under **Add an account**, type a **Name**, such as Workstation, and choose **Ollama** or **llama.cpp** as its **Kind**. The form says what an account of that kind is before anything else.
3. Check the **Address**: `http://localhost:11434` for Ollama, and `http://localhost:9931`, `llama-server`'s own, for llama.cpp. Click **Check**: it says the server's version and how many models it has, or why it couldn't ask.
4. Choose the **Model** from what **Check** found, or type its name. Each says its size, its window and whether it can use tools, think and see images.
5. Check the **Window (tokens)** and the **Longest reply (tokens)**: see [The window](#the-window).
6. Click **Add account**.

**Model for background work (optional)** is the model Claude Code uses for its small jobs, such as naming a session. It's the same model unless you choose a smaller, quicker one.

## The window

The window a model runs at is the server's to choose, not Claude Code's, and Claude Code is told it, so it compacts the conversation in time. Under the **Window (tokens)**, the form says where Claude Code will compact it.

- **Ollama** runs a model at its own setting (the app's slider, or `OLLAMA_CONTEXT_LENGTH`), which a request can't change. Unless it's changed, that's 4,096 tokens on a graphics card under 24 GB, far too small: Ollama cuts the start off each request that doesn't fit, without saying so, and Claude forgets what it was doing. **Check the window** loads the model and says the window Ollama runs it at. When it's smaller than the account's, **Make a copy with a …-token window…** asks first, then makes a copy of the model, such as `qwen3:8b-64k`, that runs at the account's window whatever Ollama's setting is. The copy shares the model's files, so nothing is downloaded; `ollama rm` removes it. Or change Ollama's own setting, or let Deventry start Ollama, which runs it at the account's window.
- **llama-server** runs its model at the window it was started with (`-c`), or the whole window the model was trained for. **Check** reads it. A request that doesn't fit is refused, never cut.

**Longest reply (tokens)**, 8,192 unless changed, is the most the model writes in one go. Claude Code keeps that much of the window free, and its own default of 32,000 would take half of a 64,000-token window.

## Starting the server

**Start Ollama when it isn't running** (or **Start llama-server when it isn't running**), on unless you turn it off, has Deventry start the server when a tab needs it and it isn't answering. It works only for an address on this computer. **Path to ollama** or **Path to llama-server** is found where its installer puts it, or on your `PATH`, unless you choose it.

- **Ollama** is started as `ollama serve` on the address's port, with the account's window as the default for the models it loads.
- **llama-server** needs the **Model to load**: a `.gguf` file, or a model on Hugging Face such as `unsloth/Qwen3-8B-GGUF:Q4_K_M`, which `llama-server` downloads the first time. It's started with that model, the account's model name and window, on this computer alone and without a key.

A server Deventry starts writes its output to `ollama.log` or `llama-server.log` in Deventry's log folder, and is stopped when Deventry closes. A server that was already running, such as Ollama's own app, is used as it is and never stopped.

## Keys

Tick **It takes an API key** for a `llama-server` started with `--api-key`, or for Ollama's cloud at `https://ollama.com`, with a key from your ollama.com settings. The key is kept in your computer's credential store, as an API key is, and Claude Code asks Deventry for it when a tab starts, so it's never in a command line or the environment. Ollama's cloud models run on ollama.com, not your computer, and count toward your Ollama plan.

## Keeping Claude Code off the internet

Claude Code still talks to Anthropic for its updates, telemetry and error reports, even with your model on your own computer. **Keep Claude Code off the internet** stops that, and the check with Anthropic before WebFetch reads a page. Claude Code's features that wait on its feature flags stay off too.

## A tab on one

- **The header** names the account, the model and where it runs, such as *Ollama · qwen3:8b · on this computer*, in place of the plan's meters or an API key's spend. Its turns are recorded with their tokens and cost nothing; the Usage window's **Accounts** lists them too.
- **Starting.** Before a tab's Claude Code starts, Deventry makes sure the server answers, starting it if the account says to, and loads the model so the first reply doesn't wait for it. If the server runs the model at a smaller window than the account's, the tab says so. If the server doesn't answer, or hasn't the model, the tab says why instead of starting.
- **The model** is the account's, which the model picker shows. To use another of the server's models, change the account's **Model** in Settings, or add another account for it. Effort isn't offered: a model that can think, thinks.
- **What it doesn't have:** web search, which needs Anthropic's (the tab starts without it), the prompt cache's countdown, the Claude app, and images for a model that can't see them, which the tab won't send.
- **If the server stops** in the middle, the turn ends with Claude Code's *Connection refused*, and the tab says the server stopped answering. Your next message checks it first, starting it again if the account says to.

## Without a Claude account

You don't need a Claude account for an Ollama or llama.cpp account's tabs. On the sign-in screen, **Use Ollama or llama.cpp instead** has the same form: fill it in and click **Add it and open Deventry**. The account becomes what new folders use, and Deventry opens. With such an account, Deventry opens without asking you to sign in, and the account menu says *Not signed in*, with **Sign in** for when you want Claude too. Claude Code itself still has to be installed.

## Moving a tab onto one

**Account ▸** moves a tab onto an Ollama or llama.cpp account as onto any other, and says the model reads the conversation for nothing. A conversation too big for the model's window can't move as it is: the question offers **New chat with a digest** instead, a new chat under the account with a digest of this one in the composer to trim and send, as [continuing under another account](moving-a-tab-to-another-account.md) does. Moving back to Claude works too.

## See also

- [Cloud provider accounts](cloud-provider-accounts.md), for Claude through Amazon Bedrock, Google Cloud or Microsoft Foundry.
- [Moving a tab to another account](moving-a-tab-to-another-account.md).
- [Your Claude sign-in](your-claude-sign-in.md).
