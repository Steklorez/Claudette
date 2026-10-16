@echo off
rem Builds the user guide in docs\ and serves it at http://127.0.0.1:4000, building it again as pages change
rem (GUIDE.md, "Previewing it"). host-docs.sh does the same on macOS and Linux.
rem
rem Needs Ruby 3 with its DevKit, from https://rubyinstaller.org. The first run installs the gems GitHub Pages builds
rem the site with. Anything after the script's name goes to jekyll serve:
rem   host-docs.bat --livereload     reloads the page in the browser too
rem   host-docs.bat --port 4001      serves on another port
rem The Downloads page reads the releases from GitHub's API, which answers a few times an hour without a token; set
rem JEKYLL_GITHUB_TOKEN for more.
setlocal
cd /d "%~dp0docs" || exit /b 1

where bundle >nul 2>nul || (
    echo Ruby isn't on PATH. Install Ruby 3 with its DevKit from https://rubyinstaller.org, then run this again.
    exit /b 1
)

call bundle check >nul 2>nul || call bundle install || exit /b 1
call bundle exec jekyll serve %*
