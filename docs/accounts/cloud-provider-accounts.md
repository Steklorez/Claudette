---
title: Cloud provider accounts
parent: Accounts
nav_order: 3
summary: "A cloud provider account lets a tab run Claude Code through Amazon Bedrock, Google Cloud or Microsoft Foundry instead of your Claude sign-in."
---

# Cloud provider accounts

A cloud provider account lets a tab run Claude Code through Amazon Bedrock, Google Cloud or Microsoft Foundry instead of your Claude sign-in. It suits a studio whose Claude comes through its cloud, beside a personal plan. You choose where it's used as you would for an API key: [a folder's account](a-folders-account.md), or [moving a tab](moving-a-tab-to-another-account.md).

{% include shot.html name="guide/accounts/settings-accounts" alt="Settings → Claude Code, at its Accounts: Default, signed in, with Sign out…; an API-key account named Studio and an Amazon Bedrock account named Render farm, each with its name, tag color, what it is and its spend warnings, and an Ollama account named Workstation, without spend warnings; then New folders use, and the switch for warning before a session continues under another account. The sidebar has Tabs, Usage and Advanced under Deventry, and All agents, Claude Code and Codex under Agents" %}

## The providers

| Provider | What it needs | How it can sign in |
|---|---|---|
| **Amazon Bedrock** | **Region** | **AWS profile or SSO**, or a **Bedrock API key** |
| **Claude Platform on AWS** | **Workspace ID** and **Region** | **AWS profile or SSO**, or a **Workspace API key** |
| **Google Cloud's Agent Platform**, formerly Vertex AI | **Project ID** and **Region** | **gcloud sign-in**, or a **Service account key file** |
| **Claude Models on Google Cloud**, run by Anthropic | **Project ID**, **Workspace ID** and, if not global, **Location** | **gcloud sign-in**, or a **Service account key file** |
| **Microsoft Foundry** | **Resource name** | **Microsoft Entra ID (az login)**, or a **Foundry API key** |

Your provider has to be set up for Claude first, such as Claude's models enabled in your AWS account or Google Cloud project. Each provider's form links to Claude Code's page about it, under **How to set up**.

## Adding an account

1. Open Settings → Claude Code → Accounts.
2. Under **Add an account**, type a **Name**, such as Render farm, and choose the provider as its **Kind**.
3. Choose how it signs in under **Sign in with**, and fill in what the provider needs.
4. Optionally, fill in the models: see [Models](#models).
5. Click **Add account**.

If something's missing or isn't right, such as a region with a space in it or a Foundry resource given as a web address, Settings says what under the button.

## Signing in

- **AWS profile or SSO.** A profile from your `~/.aws` folder, such as one for IAM Identity Center. Leave **AWS profile** empty for the AWS credentials your environment has, as a terminal would. An access key isn't kept by Deventry: save it to a profile with `aws configure --profile <name>`, and name that profile here.
- **gcloud sign-in.** Google's Application Default Credentials: sign in once with `gcloud auth application-default login`.
- **Service account key file.** A service account's JSON key, where it is on your computer: **Browse…** finds it. Claude Code reads the file itself, so it's the file's to keep safe.
- **Microsoft Entra ID (az login).** Sign in with `az login` before a tab starts.

**Sign in again when the credentials expire**, on for an AWS profile and gcloud's sign-in unless you turn it off, lets Claude Code run `aws sso login --profile <name>` or `gcloud auth application-default login` when the sign-in stops working. It opens your browser for you to sign in, and the tab carries on.

## A provider's own key

A **Bedrock API key**, a **Workspace API key** or a **Foundry API key** is kept in your computer's credential store, as an API key is. But Claude Code reads these keys only from its environment, so when a tab on the account starts, Deventry puts the key in the environment of that tab's Claude Code.

Every command Claude runs in the tab inherits that environment: its Bash commands and scripts, hooks, MCP servers and anything they start. Any of them can read the key, and print it, log it or send it on, and a prompt that asks Claude for it gets it. Settings says so in the form, and adds the key only once you tick **I understand that the commands Claude runs in this account's tabs can read this key**. Where you can, sign in another way, which keeps the key out of Claude's reach.

**Change key…** on the account says the same and replaces the key. A tab uses the new key when it starts again.

If one of Claude Code's settings files sets the same variable in its `env`, such as your `settings.json` after `/setup-bedrock`, Claude Code uses that key instead. The tab says which file: remove it there for the account's key to be used.

## Models

**Opus**, **Sonnet**, **Haiku** and **Fable** choose the model Claude Code uses for each of its model choices on the provider: Bedrock's inference profiles, such as `us.anthropic.claude-opus-4-8`, or Foundry's deployment names. Leave them empty for Claude Code's defaults on the provider, which may not be enabled for you yet. Foundry needs them in practice, since its deployments' names are yours.

## Changing and removing an account

A cloud provider account has what an API-key account has (its name, **Tag color**, the spend warnings and **Remove…**) and **Change settings…**, which changes the provider's settings, the profile or key file, **Sign in again when the credentials expire** and the models. Click **Save changes**. Its tabs use them when they start again. How it signs in stays: to change that, add another account. An account with a key has **Change key…** too.

Without a credential store, such as Linux without `secret-tool`, an account that signs in without a key can still be added and removed.

## What its tabs share, and what they don't have

Its tabs share Claude Code's configuration folder with the Default account, as an API key's do. Each starts with its provider turned on and every other one turned off, whatever your own settings say, so a provider you set up for Default doesn't apply.

- **Plan limits.** There are none: the header shows the account's estimated spend instead. See [Spend and its warnings](spend-and-its-warnings.md).
- **The Claude app.** Its tabs can't connect to it, since that needs a Claude sign-in. See [The Claude app](../away/the-claude-app.md).
- **What Claude Code leaves out on a cloud provider**, such as web search on Bedrock and connectors from claude.ai.

If Claude Code runs a tab on another provider than its account's, as an organization's managed settings can make it, the tab says so: *Claude Code is using Anthropic's API for this tab, not Google Cloud's Agent Platform as Render farm says.*

## If Claude Code can't sign in

A sign-in Claude Code can't use is that tab's own error, saying what to do: *Claude Code couldn't sign in to Amazon Bedrock for Render farm. Sign in to AWS with "aws sso login --profile studio-sso", or check the profile in Settings → Claude Code → Accounts. Or move the tab to another account.* A key that's gone from the credential store stops the tab before Claude Code starts, and says so.

## See also

- [API-key accounts](api-key-accounts.md) for an Anthropic API key.
- [Passwords and keys](../privacy/passwords-and-keys.md) for how keys are stored.
