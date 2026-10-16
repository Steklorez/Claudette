#!/usr/bin/env bash
# Builds the user guide in docs/ and serves it at http://127.0.0.1:4000, building it again as pages change
# (GUIDE.md, "Previewing it"). host-docs.bat does the same on Windows.
#
# Needs Ruby 3 and a compiler for the gems' native parts:
# - macOS: its own Ruby is 2.6, too old, so install one with Homebrew (brew install ruby) and put it first on PATH, as
#   brew's message says; xcode-select --install gives the compiler.
# - Linux: the distribution's Ruby 3 with its headers (ruby-dev or ruby-devel) and build tools (build-essential).
# The first run installs the gems GitHub Pages builds the site with. Anything after the script's name goes to
# jekyll serve:
#   ./host-docs.sh --livereload     reloads the page in the browser too
#   ./host-docs.sh --port 4001      serves on another port
# The Downloads page reads the releases from GitHub's API, which answers a few times an hour without a token; set
# JEKYLL_GITHUB_TOKEN for more.
set -euo pipefail
cd "$(dirname "$0")/docs"

if ! command -v ruby >/dev/null || ! ruby -e 'exit Gem::Version.new(RUBY_VERSION) >= Gem::Version.new("3.0")'; then
    echo "The guide needs Ruby 3 on PATH. On macOS: brew install ruby, then put it first on PATH as brew says." >&2
    exit 1
fi

bundle check >/dev/null 2>&1 || bundle install
exec bundle exec jekyll serve "$@"
