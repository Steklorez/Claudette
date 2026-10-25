// Claudette's release steps that change files (GUIDE.md, "The changelog", "Releasing"). Node 22+, no dependencies.
// Run from anywhere in the checkout; each prints what it changed and leaves committing to you.
//
//   node packaging/release.mjs prepare [--version X.Y.Z] [--date YYYY-MM-DD]
//       Step 1, before tagging. CHANGELOG.md's Unreleased section becomes the version, dated today, under a new empty
//       Unreleased, and the links at the bottom compare Unreleased with it and it with the release before. The version
//       is the one Directory.Build.props names, or --version, which Directory.Build.props then names too (a patch
//       release, 0.5.1, while 0.6.0 is being worked on). It must follow the changelog's rule: until 1.0.0, a section
//       with Added, Changed, Deprecated or Removed raises the second number, and Fixed or Security alone the third.
//       Then tag the change vX.Y.Z (step 2).
//
//   node packaging/release.mjs after [--next X.Y.Z]
//       Steps 3 and 4, once the release is published, for the changelog's latest version. The guide's "New in the next
//       release" labels go, its examples of the version Claudette shows ("Claudette 0.5.1") become the release's,
//       Directory.Build.props moves to the next version to work on (the next second number, or --next), and the lock
//       files are restored to name it. Wording the script can't safely change, such as "new in the next release" in a
//       sentence, is listed for you to edit. Pushing the change to main rebuilds the site, whose Downloads page and
//       download buttons list the release by themselves.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const changelogPath = path.join(root, 'CHANGELOG.md');
const propsPath = path.join(root, 'Directory.Build.props');
const docsDir = path.join(root, 'docs');

const [mode, ...rest] = process.argv.slice(2);
const opts = {};
for (let i = 0; i < rest.length; i += 2) opts[rest[i].replace(/^--/, '')] = rest[i + 1];

switch (mode) {
  case 'prepare': prepare(); break;
  case 'after': after(); break;
  default:
    console.error('Usage: node packaging/release.mjs prepare [--version X.Y.Z] [--date YYYY-MM-DD] | after [--next X.Y.Z]');
    process.exit(2);
}

// --- Step 1 ----------------------------------------------------------------------------------------------

function prepare() {
  const version = opts.version ?? propsVersion();
  if (!isVersion(version)) {
    fail(`"${version}" isn't a release version (X.Y.Z). A pre-release (0.7.0-beta.1) needs no section: tag it, and its notes are Unreleased's.`);
  }
  const date = opts.date ?? today();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) fail(`"${date}" isn't a date (YYYY-MM-DD).`);

  const changelog = readLines(changelogPath);
  const lines = changelog.lines;
  const unreleased = lines.findIndex(line => line === '## [Unreleased]');
  if (unreleased < 0) fail('CHANGELOG.md has no "## [Unreleased]" heading.');
  const releases = versionHeadings(lines);
  if (releases.some(r => r.version === version)) fail(`CHANGELOG.md already has a section for ${version}.`);
  const latest = releases[0]?.version;
  const end = releases[0]?.index ?? lines.findIndex(line => /^\[[^\]]+\]: /.test(line));
  const section = lines.slice(unreleased + 1, end < 0 ? lines.length : end);
  if (!section.some(line => line.startsWith('- '))) fail('Nothing is under Unreleased to release.');
  if (latest) checkRule(version, latest, section);

  // The new heading goes under Unreleased, which keeps its blank line above the old entries.
  lines.splice(unreleased + 1, 0, '', `## [${version}] - ${date}`);

  // The links at the bottom: Unreleased against the new version, and the new version against the one before.
  const link = lines.findIndex(line => line.startsWith('[Unreleased]: '));
  if (link < 0) fail('CHANGELOG.md has no "[Unreleased]: " link at the bottom.');
  const compare = /^\[Unreleased\]: (.*\/compare\/)v(.+)\.\.\.HEAD$/.exec(lines[link]);
  if (!compare) fail(`The Unreleased link isn't a comparison with HEAD: ${lines[link]}`);
  const [, base, previous] = compare;
  lines.splice(link, 1, `[Unreleased]: ${base}v${version}...HEAD`, `[${version}]: ${base}v${previous}...v${version}`);
  writeLines(changelogPath, changelog);
  console.log(`CHANGELOG.md: Unreleased is now ${version}, dated ${date}, compared with v${previous}.`);

  if (opts.version && opts.version !== propsVersion()) {
    setPropsVersion(version);
    console.log(`Directory.Build.props: ${version}.`);
    restoreLockFiles();
  }
  console.log(`\nNext: commit, tag the commit v${version} and push the tag (step 2).`);
}

/** Until 1.0.0, Added, Changed, Deprecated or Removed raise the second number, and Fixed or Security alone the third. */
function checkRule(version, latest, section) {
  const [major, minor, patch] = version.split('.').map(Number);
  const [lastMajor, lastMinor, lastPatch] = latest.split('.').map(Number);
  if (major >= 1 || lastMajor >= 1) {
    console.log('From 1.0.0 the changelog\'s rule for which number to raise is yours to check.');
    return;
  }
  const headings = section.filter(line => line.startsWith('### ')).map(line => line.slice(4).trim());
  const features = headings.some(h => ['Added', 'Changed', 'Deprecated', 'Removed'].includes(h));
  const expected = features ? `${lastMajor}.${lastMinor + 1}.0` : `${lastMajor}.${lastMinor}.${lastPatch + 1}`;
  if (version !== expected) {
    const why = features ? `has ${headings.join(', ')}, which raise the second number` : `has only ${headings.join(' and ')}, which raise the third`;
    fail(`After ${latest}, Unreleased ${why}: that's ${expected}, not ${version}. Pass --version ${expected}.`);
  }
}

// --- Steps 3 and 4 ---------------------------------------------------------------------------------------

function after() {
  const releases = versionHeadings(readLines(changelogPath).lines);
  if (releases.length === 0) fail('CHANGELOG.md has no released version.');
  const released = releases[0].version;
  const previous = releases[1]?.version;
  const [major, minor] = released.split('.').map(Number);
  const next = opts.next ?? `${major}.${minor + 1}.0`;
  if (!isVersion(next)) fail(`"${next}" isn't a version (X.Y.Z).`);

  const label = /^\{% include unreleased\.html %\}$/;
  const example = new RegExp(`\\bClaudette ${escape(previous ?? released)}\\b`, 'g');
  const older = new Set(releases.slice(1).map(r => r.version));
  const toCheck = [];
  for (const file of docsFiles()) {
    const doc = readLines(file);
    const name = path.relative(root, file).replaceAll('\\', '/');
    let labels = 0;
    let examples = 0;
    for (let i = doc.lines.length - 1; i >= 0; i--) {
      if (label.test(doc.lines[i])) {
        // The label and the blank line under it.
        doc.lines.splice(i, doc.lines[i + 1] === '' ? 2 : 1);
        labels++;
      }
    }
    if (previous) {
      doc.lines = doc.lines.map(line => line.replace(example, () => {
        examples++;
        return `Claudette ${released}`;
      }));
    }
    if (labels || examples) {
      writeLines(file, doc);
      console.log(`${name}: ${[labels && `${labels} label${labels > 1 ? 's' : ''} removed`, examples && `${examples} example${examples > 1 ? 's' : ''} now ${released}`].filter(Boolean).join(', ')}`);
    }
    // What a person words: "new in the next release" in a sentence, the landing page's "Soon" and "Next release".
    // An earlier release named as Claudette is too: an example that wasn't the last release's, or history.
    doc.lines.forEach((line, i) => {
      if (/next release/i.test(line) || /class="soon"|\bSoon\b/.test(line)) toCheck.push(`${name}:${i + 1}: ${line.trim().slice(0, 160)}`);
      const named = new Set([...line.matchAll(/\bClaudette (\d+\.\d+\.\d+)\b/g)].map(m => m[1]).filter(v => older.has(v)));
      for (const version of named) {
        toCheck.push(`${name}:${i + 1}: names Claudette ${version}, an earlier release: an example to bring up to ${released}, or history?`);
      }
    });
  }

  if (propsVersion() !== next) {
    setPropsVersion(next);
    console.log(`Directory.Build.props: ${next}.`);
    restoreLockFiles();
  }
  if (toCheck.length > 0) {
    console.log(`\nCheck these by hand:\n${toCheck.map(line => `  ${line}`).join('\n')}`);
  }
  console.log(`\nNext: commit, and push to main to rebuild the site (step 3).`);
}

/** The lock files name the projects' version, and CI restores in locked mode. */
function restoreLockFiles() {
  const result = spawnSync('dotnet', ['restore', 'Claudette.slnx', '--force-evaluate'], { cwd: root, encoding: 'utf8', shell: process.platform === 'win32' });
  if (result.status === 0) {
    console.log('The lock files are restored for it.');
    return;
  }
  console.log(`Restoring the lock files failed; run it yourself: dotnet restore Claudette.slnx --force-evaluate\n${(result.stderr || result.stdout || String(result.error ?? '')).trim()}`);
  process.exitCode = 1;
}

// --- Files -----------------------------------------------------------------------------------------------

/**
 * A text file as lines, remembering its line ending and whether it ends with one, so writing it back keeps both. A byte
 * order mark stays on the first line.
 */
function readLines(file) {
  const text = fs.readFileSync(file, 'utf8');
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  const lines = text.split(/\r?\n/);
  const endsWithNewline = lines.at(-1) === '';
  if (endsWithNewline) lines.pop();
  return { lines, eol, endsWithNewline };
}

function writeLines(file, { lines, eol, endsWithNewline }) {
  fs.writeFileSync(file, lines.join(eol) + (endsWithNewline ? eol : ''), 'utf8');
}

/** The guide's pages, not the site Jekyll built from them, nor the label's own include. */
function* docsFiles(dir = docsDir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['_site', '_includes', 'vendor', 'node_modules'].includes(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* docsFiles(full);
    else if (/\.(md|html)$/.test(entry.name)) yield full;
  }
}

/** The changelog's releases, newest first: "## [0.6.0] - 2026-10-24". */
function versionHeadings(lines) {
  return lines.flatMap((line, index) => {
    const match = /^## \[(\d+\.\d+\.\d+)\]/.exec(line);
    return match ? [{ version: match[1], index }] : [];
  });
}

function propsVersion() {
  const match = /<Version>([^<]+)<\/Version>/.exec(fs.readFileSync(propsPath, 'utf8'));
  if (!match) fail('Directory.Build.props has no <Version>.');
  return match[1];
}

function setPropsVersion(version) {
  const props = readLines(propsPath);
  props.lines = props.lines.map(line => line.replace(/<Version>[^<]+<\/Version>/, `<Version>${version}</Version>`));
  writeLines(propsPath, props);
}

// --- Helpers ---------------------------------------------------------------------------------------------

function isVersion(text) {
  return /^\d+\.\d+\.\d+$/.test(text ?? '');
}

/** Today in this computer's time zone, as the changelog dates its releases. */
function today() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

function escape(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
