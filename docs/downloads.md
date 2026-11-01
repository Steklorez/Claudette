---
title: Downloads
nav_order: 2
description: Download Deventry for Windows or macOS, or build it on Linux.
---

# Downloads

Deventry comes as an MSIX package for Windows and a `.dmg` for macOS, each for two kinds of processor. On Linux, build it from source: [Installing on Linux](getting-started/installing-on-linux.md) takes a few commands. Install [Claude Code](https://code.claude.com/docs) first: Deventry runs it in every tab.

{% comment %}
  GitHub Pages builds this page with the repository's releases (site.github, from jekyll-github-metadata), newest first.
  It's rebuilt by the next push to main, such as the change that follows a release (GUIDE.md, "Releasing"). Releases from
  1.0.0 are Deventry's; the ones before were Claudette's, Deventry's old name, and have a section of their own.
{% endcomment %}
{%- assign releases = site.github.releases | where: "draft", false -%}
{%- assign latest = nil -%}
{%- assign deventry_count = 0 -%}
{%- for release in releases -%}
  {%- assign major = release.tag_name | remove_first: "v" | split: "." | first | plus: 0 -%}
  {%- if major >= 1 and release.prerelease == false -%}
    {%- assign deventry_count = deventry_count | plus: 1 -%}
    {%- if latest == nil -%}{%- assign latest = release -%}{%- endif -%}
  {%- endif -%}
{%- endfor %}

{% if releases.size > 0 %}
{% if latest %}
## Deventry {{ latest.tag_name | remove_first: "v" }}
{: #latest }

Released {{ latest.published_at | date: "%-d %B %Y" }}. [What's new]({{ latest.html_url }}), and every version's changes in the [changelog](https://github.com/dotBunny/Deventry/blob/main/CHANGELOG.md).

{% include release.html release=latest buttons=true %}
{% else %}
## Deventry 1.0.0
{: #latest }

**Coming soon.** Deventry 1.0.0 is the first version under Deventry's own name. Until it's out, the latest release is [Claudette's](#claudette), below: Deventry's name before 1.0.0. On Windows, Claudette updates itself to Deventry when it's released; on a Mac, install Deventry by hand that once ([Coming from Claudette](updates/deventrys-updates.md#coming-from-claudette)).
{% endif %}

- **Windows:** **x64** for most PCs with an Intel or AMD processor, **Arm64** for a Snapdragon or other Arm processor. Settings → System → About → **System type** says which yours is.
- **macOS:** **Apple silicon** for a Mac with an M-series chip, **Intel** for the rest. Apple menu → **About This Mac** → **Chip** (or **Processor**) says which.
- **Linux:** there's no package yet. [Build it from source](getting-started/installing-on-linux.md), which takes a few commands, and it keeps itself current from there. Every change is built and tested on Linux too.

The SHA-256 digest is there for anyone who wants to check a file: `Get-FileHash <file>` in PowerShell, `shasum -a 256 <file>` on a Mac. Deventry checks its own updates' files itself.

{%- assign upcoming = nil -%}
{%- assign newest_time = 0 -%}
{%- for release in releases -%}
  {%- unless release.prerelease -%}{%- assign newest_time = release.published_at | date: "%s" | plus: 0 -%}{%- break -%}{%- endunless -%}
{%- endfor -%}
{%- for release in releases -%}
  {%- assign time = release.published_at | date: "%s" | plus: 0 -%}
  {%- if release.prerelease and time > newest_time -%}
    {%- assign upcoming = release -%}
    {%- break -%}
  {%- endif -%}
{%- endfor %}

{% if upcoming %}
## The next version: {{ upcoming.tag_name | remove_first: "v" }}
{: #next }

{: .warning }
> A pre-release, from {{ upcoming.published_at | date: "%-d %B %Y" }}, for trying what's coming. Deventry offers pre-releases as updates only with Settings → General → **Include pre-releases** on. [Its notes]({{ upcoming.html_url }}).

{% include release.html release=upcoming %}
{% endif %}

{% if deventry_count > 1 %}
## Earlier releases
{: #earlier }

{% for release in releases %}
{%- assign major = release.tag_name | remove_first: "v" | split: "." | first | plus: 0 -%}
{%- if release.prerelease or major < 1 or release.tag_name == latest.tag_name %}{% continue %}{% endif %}
### {{ release.tag_name | remove_first: "v" }}

Released {{ release.published_at | date: "%-d %B %Y" }}. [Its notes]({{ release.html_url }}).

{% include release.html release=release %}
{% endfor %}
{% endif %}

## Claudette
{: #claudette }

Before 1.0.0, Deventry was called Claudette. These are Claudette's releases, newest first. Each still updates itself to the latest version, Deventry's once it's out, except on a Mac, where Deventry is installed by hand once ([Coming from Claudette](updates/deventrys-updates.md#coming-from-claudette)).

{% for release in releases %}
{%- assign major = release.tag_name | remove_first: "v" | split: "." | first | plus: 0 -%}
{%- if release.prerelease or major >= 1 %}{% continue %}{% endif %}
### Claudette {{ release.tag_name | remove_first: "v" }}

Released {{ release.published_at | date: "%-d %B %Y" }}. [Its notes]({{ release.html_url }}).
{% if release.tag_name == "v0.1.0" %}
{: .note }
> 0.1.0 is from before the repository moved: it announces later versions but can't install them. Install the later version by hand.
{% endif %}

{% include release.html release=release %}
{% endfor %}
{% else %}
The packages are on [Deventry's Releases page on GitHub](https://github.com/dotBunny/Deventry/releases): the `.msix` files are for Windows and the `.dmg` files for macOS, `x64` for Intel and AMD processors and `arm64` for Arm ones, including Apple silicon. On Linux, [build it from source](getting-started/installing-on-linux.md).
{% endif %}

## Next

- [What you need](getting-started/what-you-need.md), [Installing](getting-started/installing.md) and [Installing on Linux](getting-started/installing-on-linux.md).
- Once it's installed, Deventry [updates itself](updates/deventrys-updates.md).
