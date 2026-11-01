// Draws Deventry's icon and Joulene (DESIGN.md §2, "Packaging and signing", "Renaming to Deventry"). The icon is a
// road-work sign: a diamond in Joulene's gown's blue, lit softly from the top, with a prompt, ">_", on it in white, and a
// white edge round it, so it stands on a light or dark background without a tile. It's drawn as shapes, and the same
// shapes make the SVGs and, drawn here from how far each pixel is from them, every PNG.
// Joulene, the mascot on the composer, is a medieval lady of 12×12 cells, which the app draws cell by cell. Writes:
//   packaging/icon/deventry.svg               the icon as vectors, also Rider's project icon (.idea/.idea.Deventry/.idea/icon.svg)
//   packaging/icon/deventry-1024.png          the macOS icon, the sign on a charcoal tile; build-dmg.sh scales it
//   src/Deventry.App/Assets/deventry.ico      the window and executable icon
//   docs/assets/deventry.svg, favicon.ico     the user guide's logo and favicon (GUIDE.md, "The site")
//   docs/assets/joulene.svg                   Joulene standing, for the guide's pages about her
//   docs/assets/wordmark.svg                  the name in pixels, for the guide's landing page
//   packaging/windows/Assets/*.png            the MSIX logos, and the taskbar's unplated sizes
//   src/Deventry.App/Assets/AppIcon/*.png     the animations' frames (DESIGN.md §10): on the taskbar, chevrons
//                                             lighting or an hourglass running, in a lighter blue; on the Dock,
//                                             the sign's chevrons lighting, its hourglass running, and a "?" and a "!"
//                                             taking turns (AppIconAnimations)
//   src/Deventry.App/Assets/Mascot/mascot.json  Joulene on the composer's poses, props and hats, as cells (DESIGN.md §5)
// Run from anywhere: node packaging/icon/build-icons.mjs

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { crc32, deflateSync } from 'node:zlib';

const root = join(dirname(fileURLToPath(import.meta.url)), '../..');

/** Rows with the cells of `cells`, { 'row,column': letter }, written over them. */
const set = (rows, cells) => rows.map((row, y) => [...row].map((ch, x) => cells[`${y},${x}`] ?? ch).join(''));

/** Rows with the rows of `patch`, { row: 'cells' }, written over them, '?' leaving a cell as it was. */
const patched = (rows, patch) => rows.map((row, y) => patch[y] === undefined ? row
  : [...row].map((ch, x) => (patch[y][x] ?? '?') === '?' ? ch : patch[y][x]).join(''));

// ── The icon: a road-work sign ───────────────────────────────────────────
// Everything is drawn in a 1024 square. A shape is one of
//   { fill: [[x, y], …], grow }   a polygon, filled, and grown by `grow` all round, its corners rounded
//   { line: [[x, y], …], width }  a line through the points, its ends and joins round
//   { ring: [[x, y], …], width }  the same round a closed polygon
//   { dot: [x, y], radius }       a circle
// in a `paint`: a colour, or a gradient, { radial } or { linear }. `opacity` fades it, and `clip`, a polygon, keeps
// only what's inside it.
const white = '#FFFFFF';
// Her gown's blue in the middle, a few shades lighter at the top and a shade darker at the edges: a soft glow.
const gown = [[0, '#4D65AF'], [0.55, '#3B4E8C'], [1, '#334379']];

/** The soft glow over a square box: a radial gradient from a fifth of the way down it. */
const glow = (x, y, size) => ({ radial: { cx: x + size / 2, cy: y + size * 0.2, r: size * 0.85, stops: gown } });

/** A rectangle's corners. */
const rect = (x0, y0, x1, y1) => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];

/** The points along an SVG path of absolute M, L, H, V, Q, C and Z, its curves as short straight lines. */
function points(d) {
  const tokens = d.match(/[MLHVQCZ]|-?\d*\.?\d+/g);
  const out = [];
  let i = 0, command = '', at = [0, 0];
  const number = () => Number(tokens[i++]);
  const curve = (controls, end, steps = 16) => {
    const from = at;
    for (let s = 1; s <= steps; s++) {
      const t = s / steps, u = 1 - t;
      const weights = controls.length === 1 ? [u * u, 2 * u * t, t * t] : [u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t];
      const all = [from, ...controls, end];
      out.push([0, 1].map(k => all.reduce((sum, p, j) => sum + weights[j] * p[k], 0)));
    }
    at = end;
  };
  while (i < tokens.length) {
    if (/[A-Z]/.test(tokens[i])) command = tokens[i++];
    switch (command) {
      case 'M': case 'L': at = [number(), number()]; out.push(at); break;
      case 'H': at = [number(), at[1]]; out.push(at); break;
      case 'V': at = [at[0], number()]; out.push(at); break;
      case 'Q': curve([[number(), number()]], [number(), number()]); break;
      case 'C': curve([[number(), number()], [number(), number()]], [number(), number()]); break;
      case 'Z': break;
      default: throw new Error(`Can't follow ${command} in ${d}`);
    }
  }
  return out;
}

/** A shape scaled by `scale` and turned `turn` degrees about `from`, and put at `to`. */
function moved(shape, { scale = 1, turn = 0, from = [512, 512], to = from }) {
  const cos = Math.cos((turn * Math.PI) / 180), sin = Math.sin((turn * Math.PI) / 180);
  const at = ([x, y]) => {
    const dx = (x - from[0]) * scale, dy = (y - from[1]) * scale;
    return [to[0] + dx * cos - dy * sin, to[1] + dx * sin + dy * cos];
  };
  const out = { ...shape };
  for (const key of ['fill', 'line', 'ring', 'clip']) if (shape[key]) out[key] = shape[key].map(at);
  if (shape.dot) out.dot = at(shape.dot);
  for (const key of ['grow', 'width', 'radius']) if (shape[key] !== undefined) out[key] = shape[key] * scale;
  return out;
}

/** The diamond's corners, `r` from its middle. */
const diamond = r => [[512, 512 - r], [512 + r, 512], [512, 512 + r], [512 - r, 512]];

/**
 * The sign, its corners `r` from the middle, with `symbol` on it: its white edge, its blue, and the symbol, drawn for a
 * sign of 400 and scaled to this one.
 */
const sign = (r, symbol) => [
  { fill: diamond(r), grow: r * 0.17, paint: white },
  { fill: diamond(r), grow: r * 0.08, paint: glow(512 - r, 512 - r, 2 * r) },
  ...symbol.map(shape => moved(shape, { scale: r / 400 })),
];

// What's on the sign, each centred on its middle.
const stroke = (d, width = 62, opacity = 1) => ({ line: points(d), width, paint: white, opacity });

// At rest: the prompt, ">_", the "_" starting clear of the ">"'s point, so the two don't run together when small.
const prompt = [stroke('M348 426 L460 512 L348 598'), stroke('M548 598 H676')];

// Working: the ">" becomes an arrow board's three chevrons, lighting one after another; those still off show faintly.
const chevrons = on => [327, 467, 607].map((x, i) => stroke(`M${x} 426 L${x + 90} 512 L${x} 598`, 58, i < on ? 1 : 0.25));

// Needs you: a "?" and a "!" take turns. They share their dot, and their height, from 345 to 679.
const dot = { dot: [512, 638], radius: 41, paint: white };
const question = [stroke('M436 432 C436 358 588 358 588 432 C588 486 512 486 512 534'), dot];
const bang = [stroke('M512 376 V534'), dot];

// Waiting for a usage limit: an hourglass, drawn in a 100 × 140 box, its bars at the top and bottom, its glass between
// them, and sand in `top` quarters of the top bulb and `bottom` quarters of the bottom one, running between them while
// both have some. It runs down, rests, and turns over: once sideways, then full at the top again.
const glass = points('M14 12 H86 V26 Q86 50 55 68 V72 Q86 90 86 114 V128 H14 V114 Q14 90 45 72 V68 Q14 50 14 26 Z');
const sand = [[4, 0], [3, 1], [2, 2], [1, 3], [0, 4], [0, 4, true]];
function hourglass([top, bottom, turned], scale, line, paint = white) {
  const depth = quarters => (54 * quarters) / 4;
  return [
    { fill: glass, paint, opacity: 0.2 },
    top > 0 && { fill: rect(0, 70 - depth(top), 100, 70), paint, clip: glass },
    bottom > 0 && { fill: rect(0, 128 - depth(bottom), 100, 128), paint, clip: glass },
    top > 0 && bottom > 0 && { fill: rect(47, 66, 53, 128 - depth(bottom)), paint, clip: glass },
    { ring: glass, width: line, paint },
    { fill: rect(7, 7, 93, 11), grow: 5, paint },
    { fill: rect(7, 129, 93, 133), grow: 5, paint },
  ].filter(Boolean).map(shape => moved(shape, { scale, turn: turned ? 90 : 0, from: [50, 70], to: [512, 512] }));
}

// The mark on its own (Windows, the site), and on the Dock, the sign smaller on a charcoal tile, Apple's 824 rounded
// square, since macOS puts an icon that isn't one in a grey one.
const mark = symbol => sign(390, symbol);
const tile = [{ fill: rect(285, 285, 739, 739), grow: 185, paint: { linear: { x1: 100, y1: 100, x2: 924, y2: 924, stops: [[0, '#3A3F47'], [1, '#23262B']] } } }];
const dock = symbol => [...tile, ...sign(300, symbol)];

// The taskbar overlays (Windows), drawn at 32 px, as Windows wants overlays at twice their 16: the symbol alone, the
// chevrons lighting while tabs work or the hourglass running while one waits, with nothing round it. They're in her
// gem's blue, lighter than the sign's, which holds against a dark taskbar (4.2:1) and a light one (3.5:1).
const overlayBlue = '#5B7FD6';
const overlayChevrons = on => [4.5, 13, 21.5].map((x, i) =>
  ({ line: [[x, 9], [x + 6, 16], [x, 23]].map(([px, py]) => [px * 32, py * 32]), width: 3.2 * 32, paint: overlayBlue, opacity: i < on ? 1 : 0.3 }));
// The hourglass 24 of the overlay's 32 pixels tall.
const overlayHourglass = glassFrame => hourglass(glassFrame, (24 * 32) / 140, 10, overlayBlue);

// ── Her cave ─────────────────────────────────────────────────────────────
// The cave in its grassy mound, the dark of its mouth, and the ground in front of it: one of Joulene's props, which she
// visits (below).
const cave = [
  '....gGGg....',
  '..gGGgGGGg..',
  '.gGMhhMMMGg.',
  '.MhhMMMMMMm.',
  'MhMMMMDDMMMm',
  'MMMMDDDDDMMm',
  'MhMDDDDDDDMm',
  'MMMDDDDDDDmm',
  'MMDDDDDDDDDm',
  'mMDDDDDDDDDm',
  'mmDDDDDDDDmm',
  'eeeeeeeeeeee',
];
const palette = {
  g: '#4E7D3E', G: '#6FA35A', // grass
  M: '#8A7F73', m: '#6B5F55', h: '#A89E92', // stone, its shade and its highlight
  D: '#1E1A22', // the dark inside
  e: '#5A4A3C', // the ground
};

// ── Joulene on the composer (DESIGN.md §5) ───────────────────────────────
// A medieval lady: platinum hair falling past her shoulders, longer on her right, a gold circlet with a gem that takes
// the colour of the tab's group, a rose face and arms, and a blue gown with a gold belt, to the floor. Her poses are
// drawn from her standing; the app draws them as cells, so they're written as rows rather than images. Each pose's
// last row is the hem of her gown, on the composer's top edge.
const joulene = [
  '....hHHh....',
  '..hYYKYYYh..',
  '.hHHHHHHHHh.',
  '.HHHOOOOHHH.',
  '.HHOEOOEOHH.',
  '.HHOOOOOOHHh',
  '.HHHOOOOHHHH',
  '.HUUUUUUUUHH',
  '.HOUUhhUUOHh',
  '.h.UUUUUU.H.',
  '..UUUUUUUU..',
  '.UUUUUUUUUU.',
];

const closedEyes = { 4: '.HHOOOOOOHH.' };
const lookLeft = { 4: '.HHEOOEOOHH.' };
const lookRight = { 4: '.HHOOEOOEHH.' };
// Her hands at her sides gone, for an arm that's somewhere else.
const leftHandUp = { '8,2': 'U' };
const rightHandUp = { '8,9': 'U' };
// An arm raised beside her: its sleeve, then her forearm and hand. High is a row further up, beside her head.
const leftArm = { ...leftHandUp, '7,0': 'U', '6,0': 'O', '5,0': 'O' };
const leftArmHigh = { ...leftHandUp, '6,0': 'U', '5,0': 'O', '4,0': 'O' };
const rightArm = { ...rightHandUp, '7,11': 'U', '6,11': 'O', '5,11': 'O' };
const rightArmHigh = { ...rightHandUp, '6,11': 'U', '5,11': 'O', '4,11': 'O' };
const mouthOpen = { '5,5': 'E', '5,6': 'E' };
// Walking in a gown: her hem sways one way, then the other.
const hemRight = { 10: '...UUUUUUUU.', 11: '..UUUUUUUUUU' };
const hemLeft = { 10: '.UUUUUUUU...', 11: 'UUUUUUUUUU..' };
const walkHems = [hemRight, {}, hemLeft, {}];

/** Her standing, with each change written over her in turn: rows ({ row: 'cells' }) or cells ({ 'row,column': letter }). */
const pose = (...changes) => changes.reduce((rows, change) =>
  Object.keys(change).some(key => key.includes(',')) ? set(rows, change) : patched(rows, change), joulene);

const armsUp = { ...leftArmHigh, ...rightArmHigh };
// Leaning on the edge: she stands two cells lower, behind it, with her arms folded on top of it.
const leaning = pose(rightHandUp, leftHandUp, { 9: 'UOOOOOOOOOOU' });
// Climbing up from behind the edge: her hands are a prop that stays on the edge while she pulls herself up between them.
const climbing = pose(leftHandUp, rightHandUp);

const mascotPoses = {
  stand: joulene,
  blink: pose(closedEyes),
  lookLeft: pose(lookLeft),
  lookRight: pose(lookRight),
  // Looking down at the edge, before she topples off it.
  lookDown: pose(closedEyes, { '5,4': 'E', '5,7': 'E' }),
  ...Object.fromEntries(walkHems.flatMap((hem, i) => [
    [`walkLeft-${i}`, pose(lookLeft, hem)],
    [`walkRight-${i}`, pose(lookRight, hem)],
  ])),
  wave: pose(leftArm),
  // The waving arm a row higher, beside her hair.
  waveHigh: pose(leftArmHigh),
  armsUp: pose(armsUp),
  stretch: pose(closedEyes, { ...leftHandUp, ...rightHandUp, '5,0': 'U', '4,0': 'O', '3,0': 'O', '5,11': 'U', '4,11': 'O', '3,11': 'O' }),
  // Typing: her hands bob in turn over the laptop's lid.
  typeLeft: pose(leftHandUp, { '7,3': 'O' }),
  typeRight: pose(rightHandUp, { '7,8': 'O' }),
  lean: leaning,
  leanBlink: patched(leaning, closedEyes),
  climb: climbing,
  climbLookLeft: patched(climbing, lookLeft),
  climbLookRight: patched(climbing, lookRight),
  // Her right arm up instead, over her longer hair: for dancing, tossing a ball and raising the hammer.
  waveRight: pose(rightArm),
  // Half-way through a twirl: the back of her head, all hair, and her circlet without its gem.
  back: pose({ 1: '..hYYYYYYh..', 3: '.HHHHHHHHHH.', 4: '.HHHHHHHHHH.', 5: '.HHHHHHHHHHh', 6: '.HHHHHHHHHHH' }),
  // A big yawn, arms up, eyes shut and mouth wide.
  yawn: pose(armsUp, closedEyes, mouthOpen, { '6,5': 'E', '6,6': 'E' }),
  // Achoo: eyes shut, mouth open.
  achoo: pose(closedEyes, mouthOpen),
  // Tapping a foot: a shoe out from under her hem.
  tap: pose({ 11: '.UUUUUUUU.B.' }),
  // Waving toward the sidebar, to her left, when another tab needs the user.
  waveLookLeft: pose(leftArm, lookLeft),
  waveHighLookLeft: pose(leftArmHigh, lookLeft),
  // Picked up and carried: arms up, mouth open.
  carried: pose(armsUp, { '6,5': 'E', '6,6': 'E' }),
  // Hands on her hips, elbows out.
  handsOnHips: pose(leftHandUp, rightHandUp, { '7,0': 'U', '8,1': 'O', '7,11': 'U', '8,10': 'O' }),
};

// What she has with her. Z takes the theme's muted text colour, so it reads on light and dark.
const mascotProps = {
  // Asleep: a small z, then a bigger one higher up.
  z: ['ZZZZ', '..Z.', '.Z..', 'ZZZZ'],
  bigZ: ['ZZZZZ', '...Z.', '..Z..', '.Z...', 'ZZZZZ'],
  // Her hands gripping the edge while she climbs up.
  hands: ['.O........O.', '.O........O.'],
  // Startled.
  bang: ['ZZ', 'ZZ', 'ZZ', '..', 'ZZ'],
  // Puzzled.
  question: ['ZZZ', '..Z', '.Z.', '...', '.Z.'],
  // The puff of a sneeze.
  puff: ['.ZZ.', 'ZZZZ', '.ZZ.'],
  // A heart she blows, and a ball she tosses, the colour of her circlet's gem.
  heart: ['.K.K.', 'KKKKK', '.KKK.', '..K..'],
  ball: ['KK', 'KK'],

  // While Claude works, what she works with depends on the tool it's running. Reading or searching: a magnifying glass
  // held out to her right, the handle in her hand.
  magnifier: ['...XX.', '..XDDX', '..XDDX', '...XX.', '..N...', '.N....'],
  // Editing: a hammer, raised, and striking the edge.
  'hammer-up': ['XXX', 'XXX', '.N.', '.N.', '.N.'],
  'hammer-down': ['N...', '.N..', '..XX', '..XX'],
  // Running commands: a little terminal on the edge in front of her, its output scrolling, the cursor blinking.
  'terminal-0': ['LLLLLLLLLL', 'LVVVVEEEEL', 'LVVEEEEEEL', 'LVEWEEEEEL', 'LLLLLLLLLL'],
  'terminal-1': ['LLLLLLLLLL', 'LVVEEEEEEL', 'LVVVVVEEEL', 'LVEEEEEEEL', 'LLLLLLLLLL'],
  'terminal-2': ['LLLLLLLLLL', 'LVVVVVEEEL', 'LVVVEEEEEL', 'LVEWEEEEEL', 'LLLLLLLLLL'],
  // The web: a globe on a stand beside her, turning.
  'globe-0': ['.DDD.', 'DVVDD', 'DDVVD', 'DDDVD', '.DDD.', '..X..', '.XXX.'],
  'globe-1': ['.DDD.', 'DDVVD', 'DDDVV', 'VDDDV', '.DDD.', '..X..', '.XXX.'],
  'globe-2': ['.DDD.', 'DDDVV', 'VDDDV', 'VVDDD', '.DDD.', '..X..', '.XXX.'],
  // Plan mode: a clipboard she writes on.
  clipboard: ['...XX...', 'NNNNNNNN', 'NWWWWWWN', 'NWZZZZWN', 'NWWWWWWN', 'NWZZZWWN', 'NNNNNNNN'],
  // A long turn: a mug of coffee on the edge beside her, steaming.
  'mug-0': ['Z.Z.', '.Z.Z', 'LLL.', 'LBLL', 'LLLL', 'LLL.'],
  'mug-1': ['.Z.Z', 'Z.Z.', 'LLL.', 'LBLL', 'LLLL', 'LLL.'],
  // The context nearly full: a drop of sweat.
  sweat: ['D', 'D'],
  // Compacting: a broom she sweeps the edge with.
  broom: ['.N.', '.N.', '.N.', '.N.', '.N.', 'YYY', 'YYY'],
  // A failed turn: stars going round her head.
  star: ['.Y.', 'YYY', '.Y.'],
  // Another tab needs the user: an arrow toward the sidebar.
  arrow: ['..Z.', '.Z..', 'ZZZZ', '.Z..', '..Z.'],
  // A file dropped on the composer, caught; and a huge paste, a heavy crate.
  file: ['ZZZZ.', 'ZWWZZ', 'ZLLWZ', 'ZWWWZ', 'ZLLLZ', 'ZZZZZ'],
  crate: ['NNNNNNNN', 'NBNNNNBN', 'NNBNNBNN', 'NNNBBNNN', 'NNBNNBNN', 'NBNNNNBN', 'NNNNNNNN'],
  // A message sent: a paper plane she throws up into the conversation.
  plane: ['Z....', 'ZWZ..', '.ZWWZ', '..ZZZ'],
  // All of Claude's tasks done: confetti.
  'confetti-y': ['Y'],
  'confetti-r': ['R'],
  'confetti-d': ['D'],
  'confetti-v': ['V'],
  'confetti-k': ['K'],
  // Every changed file reviewed: a rubber stamp, and the check mark it leaves.
  stamp: ['.NN.', '.NN.', 'NNNN', 'RRRR'],
  check: ['.....V', '....VV', 'V..VV.', 'VVVV..', '.VV...'],
  // Poked once too often: a storm cloud over her.
  cloud: ['.ZZZ..', 'ZZZZZZ', '.ZZZZ.', '..Y...', '.Y....'],
  // Sitting on the edge: her gown over the front of the box, and her shoes swinging under it. Drawn in front of it.
  'dangle-0': ['.UUUUUUUUUU.', '...BB..BB...'],
  'dangle-1': ['.UUUUUUUUUU.', '..BB..BB....'],
  'dangle-2': ['.UUUUUUUUUU.', '....BB..BB..'],
  // The back of her laptop's lid, in front of her while she types.
  laptop: ['.LLLLLLLL.', '.LLLLLLLL.', '.LLLLLLLL.', 'BBBBBBBBBB'],
  // While a usage limit holds the task: a small hourglass whose sand runs down.
  'hourglass-0': ['XXXXX', '.SSS.', '..S..', '.GGG.', 'XXXXX'],
  'hourglass-1': ['XXXXX', '.GSG.', '..S..', '.GSG.', 'XXXXX'],
  'hourglass-2': ['XXXXX', '.GGG.', '..G..', '.SSS.', 'XXXXX'],
};

// Her cave, which she visits (DESIGN.md §5), in her palette's letters; and with her eyes looking out of its dark while
// she's inside.
const caveProp = cave.map(row => row.replace(/[gGhD]/g, ch => ({ g: 'c', G: 'C', h: 'i', D: 'k' })[ch]));
mascotProps.cave = caveProp;
mascotProps['cave-eyes'] = set(caveProp, { '8,5': 'W', '8,7': 'W' });

// What she wears on her head in place of her circlet, its brim over it: `y` is the row of her sprite the hat's top row
// goes on (negative is above her), `x` the column. A witch's hat for Halloween, Santa's in December, a nightcap late at
// night.
const mascotHats = {
  witch: { x: 0, y: -4, rows: ['.......P....', '......PP....', '.....PPP....', '....PPPP....', '...PYYYPP...', 'QQQQQQQQQQQQ'] },
  santa: { x: 0, y: -3, rows: ['..........FF', '.......RRRFF', '.....RRRRR..', '...RRRRRRR..', '.FFFFFFFFFF.'] },
  nightcap: { x: 0, y: -3, rows: ['F...........', 'FDD.........', '.DDDDDD.....', '..DDDDDDD...', '.FFFFFFFFFF.'] },
};

const mascotPalette = {
  O: '#CF7F96', // her face and arms, rose
  E: '#141413', // her eyes
  H: '#F0DFA6', // her hair, platinum
  h: '#C4A961', // its edge, and her belt
  K: '#5B7FD6', // her circlet's gem, which takes the tab group's colour
  U: '#3B4E8C', // her gown
  L: '#8F8D86', // the lid, a warm grey that reads on light and dark
  B: '#5F5E59',
  X: '#8F8D86',
  G: '#F4F6FB', // the hourglass's empty glass
  F: '#E6E1D8', // a hat's fur trim and pompom, off-white so it shows on a light theme
  S: '#CF7F96', // the hourglass's sand
  Y: '#E3B341', // gold: her circlet, stars, confetti, a broom's bristles
  D: '#5B9BD5', // blue: sweat, the sea, a nightcap
  V: '#4CAF6A', // green: land, a terminal's output, the check mark
  R: '#D2453C', // red: Santa's hat, a stamp's rubber, confetti
  W: '#F5F4EF', // paper
  N: '#8B5E3C', // wood: handles, a clipboard, a crate
  P: '#7B5BB0', // a witch's hat, and its brim
  Q: '#4B3470',
  // Her cave: its grass, stone, dark and ground.
  c: palette.g,
  C: palette.G,
  M: palette.M,
  m: palette.m,
  i: palette.h,
  k: palette.D,
  e: palette.e,
};

function mascotJson() {
  const rows = frames => Object.entries(frames).map(([name, cells]) => `    ${JSON.stringify(name)}: ${JSON.stringify(cells)}`).join(',\n');
  return [
    '{',
    '  "_comment": "Drawn by packaging/icon/build-icons.mjs; change the poses there. Z is the theme\'s muted text colour, K her gem, in the tab group\'s colour.",',
    `  "palette": ${JSON.stringify(mascotPalette)},`,
    '  "poses": {',
    rows(mascotPoses),
    '  },',
    '  "props": {',
    rows(mascotProps),
    '  },',
    '  "hats": {',
    Object.entries(mascotHats).map(([name, hat]) => `    ${JSON.stringify(name)}: ${JSON.stringify(hat)}`).join(',\n'),
    '  }',
    '}',
    '',
  ].join('\n');
}

// ── Drawing ──────────────────────────────────────────────────────────────
const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));

/** A transparent RGBA image. */
const image = (width, height) => ({ width, height, data: Buffer.alloc(width * height * 4) });

/** How far (x, y) is from the segment a–b. */
function fromSegment(x, y, [ax, ay], [bx, by]) {
  const dx = bx - ax, dy = by - ay;
  const t = dx || dy ? Math.min(1, Math.max(0, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy))) : 0;
  return Math.hypot(x - ax - t * dx, y - ay - t * dy);
}

/** How far (x, y) is from a line through `pts`, round it if `closed`. */
function fromLine(x, y, pts, closed) {
  let d = Infinity;
  const n = closed ? pts.length : pts.length - 1;
  for (let i = 0; i < n; i++) d = Math.min(d, fromSegment(x, y, pts[i], pts[(i + 1) % pts.length]));
  return d;
}

/** How far (x, y) is outside the polygon `pts`, negative inside. */
function fromPolygon(x, y, pts) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, yi] = pts[i], [xj, yj] = pts[j];
    if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  const d = fromLine(x, y, pts, true);
  return inside ? -d : d;
}

/** How far (x, y) is outside a shape, negative inside. */
function distance(shape, x, y) {
  const d = shape.fill ? fromPolygon(x, y, shape.fill) - (shape.grow ?? 0)
    : shape.line ? fromLine(x, y, shape.line, false) - shape.width / 2
      : shape.ring ? fromLine(x, y, shape.ring, true) - shape.width / 2
        : Math.hypot(x - shape.dot[0], y - shape.dot[1]) - shape.radius;
  return shape.clip ? Math.max(d, fromPolygon(x, y, shape.clip)) : d;
}

/** The box a shape stays inside: [left, top, right, bottom]. */
function bounds(shape) {
  const pts = shape.fill ?? shape.line ?? shape.ring ?? [shape.dot];
  const pad = shape.grow ?? (shape.width ?? 0) / 2 + (shape.radius ?? 0);
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  return [Math.min(...xs) - pad, Math.min(...ys) - pad, Math.max(...xs) + pad, Math.max(...ys) + pad];
}

/** A paint's colour at (x, y). */
function colour(paint, x, y) {
  if (typeof paint === 'string') return rgb(paint);
  const g = paint.radial ?? paint.linear;
  const t = paint.radial
    ? Math.hypot(x - g.cx, y - g.cy) / g.r
    : ((x - g.x1) * (g.x2 - g.x1) + (y - g.y1) * (g.y2 - g.y1)) / ((g.x2 - g.x1) ** 2 + (g.y2 - g.y1) ** 2);
  const stops = g.stops;
  const at = Math.min(1, Math.max(0, t));
  const next = stops.findIndex(([offset]) => offset >= at);
  if (next <= 0) return rgb(stops[next < 0 ? stops.length - 1 : 0][1]);
  const [o0, c0] = stops[next - 1], [o1, c1] = stops[next];
  const k = (at - o0) / (o1 - o0);
  return rgb(c0).map((c, i) => c + (rgb(c1)[i] - c) * k);
}

/**
 * The shapes, their 1024 square drawn `side` pixels across in the middle of a `width`×`height` image. Each pixel takes
 * as much of a shape as covers it, from how far it is from its edge, and at the small sizes from several points in
 * it, so thin lines stay as light as they are thin.
 */
function render(shapes, side, width = side, height = width) {
  const img = image(width, height);
  const unit = 1024 / side;
  const left = (width - side) / 2, top = (height - side) / 2;
  const n = side <= 64 ? 4 : side <= 256 ? 2 : 1;
  for (const shape of shapes) {
    const [x0, y0, x1, y1] = bounds(shape);
    const opacity = shape.opacity ?? 1;
    for (let py = Math.max(0, Math.floor(y0 / unit + top) - 1); py < Math.min(height, Math.ceil(y1 / unit + top) + 1); py++) {
      for (let px = Math.max(0, Math.floor(x0 / unit + left) - 1); px < Math.min(width, Math.ceil(x1 / unit + left) + 1); px++) {
        let covered = 0;
        for (let sy = 0; sy < n; sy++) {
          for (let sx = 0; sx < n; sx++) {
            const d = distance(shape, (px - left + (sx + 0.5) / n) * unit, (py - top + (sy + 0.5) / n) * unit);
            covered += Math.min(1, Math.max(0, 0.5 - (d * n) / unit));
          }
        }
        const alpha = (covered / (n * n)) * opacity;
        if (alpha <= 0) continue;
        const [r, g, b] = colour(shape.paint, (px - left + 0.5) * unit, (py - top + 0.5) * unit);
        const at = (py * width + px) * 4;
        const under = img.data[at + 3] / 255;
        const out = alpha + under * (1 - alpha);
        [r, g, b].forEach((c, i) => { img.data[at + i] = Math.round((c * alpha + img.data[at + i] * under * (1 - alpha)) / out); });
        img.data[at + 3] = Math.round(out * 255);
      }
    }
  }
  return img;
}

// ── Files ────────────────────────────────────────────────────────────────
function png({ width, height, data }) {
  const stride = width * 4;
  const raw = Buffer.alloc((stride + 1) * height); // each row starts with filter type 0, none
  for (let y = 0; y < height; y++) data.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  const chunk = (type, body) => {
    const out = Buffer.alloc(12 + body.length);
    out.writeUInt32BE(body.length, 0);
    out.write(type, 4, 'ascii');
    body.copy(out, 8);
    out.writeUInt32BE(crc32(out.subarray(4, 8 + body.length)), 8 + body.length);
    return out;
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // bits per channel
  header[9] = 6; // RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/** An .ico holding each image as a PNG, which Windows has read since Vista. */
function ico(images) {
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(1, 2); // an icon, not a cursor
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  const bodies = images.map((img, i) => {
    const body = png(img);
    const entry = 6 + 16 * i;
    header[entry] = img.width % 256; // 0 means 256
    header[entry + 1] = img.height % 256;
    header.writeUInt16LE(1, entry + 4); // colour planes
    header.writeUInt16LE(32, entry + 6); // bits per pixel
    header.writeUInt32LE(body.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += body.length;
    return body;
  });
  return Buffer.concat([header, ...bodies]);
}

/** Shapes as an SVG, in their 1024 square. */
function vectors(shapes) {
  const n = v => +v.toFixed(2);
  const list = pts => pts.map(([x, y]) => `${n(x)},${n(y)}`).join(' ');
  const defs = [];
  const paintOf = paint => {
    if (typeof paint === 'string') return paint;
    const id = `paint-${defs.length}`;
    const stops = (paint.radial ?? paint.linear).stops.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join('');
    defs.push(paint.radial
      ? `<radialGradient id="${id}" gradientUnits="userSpaceOnUse" cx="${n(paint.radial.cx)}" cy="${n(paint.radial.cy)}" r="${n(paint.radial.r)}">${stops}</radialGradient>`
      : `<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${paint.linear.x1}" y1="${paint.linear.y1}" x2="${paint.linear.x2}" y2="${paint.linear.y2}">${stops}</linearGradient>`);
    return `url(#${id})`;
  };
  const body = shapes.map(shape => {
    const p = paintOf(shape.paint);
    const fade = shape.opacity !== undefined && shape.opacity !== 1 ? ` opacity="${shape.opacity}"` : '';
    const round = 'stroke-linecap="round" stroke-linejoin="round"';
    if (shape.fill) {
      const edge = shape.grow ? ` stroke="${p}" stroke-width="${n(shape.grow * 2)}" stroke-linejoin="round"` : '';
      return `  <polygon points="${list(shape.fill)}" fill="${p}"${edge}${fade}/>`;
    }
    if (shape.line) return `  <polyline points="${list(shape.line)}" fill="none" stroke="${p}" stroke-width="${n(shape.width)}" ${round}${fade}/>`;
    if (shape.ring) return `  <polygon points="${list(shape.ring)}" fill="none" stroke="${p}" stroke-width="${n(shape.width)}" ${round}${fade}/>`;
    return `  <circle cx="${n(shape.dot[0])}" cy="${n(shape.dot[1])}" r="${n(shape.radius)}" fill="${p}"${fade}/>`;
  });
  return [
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">',
    '  <!-- Drawn by packaging/icon/build-icons.mjs; change the sign there. -->',
    ...(defs.length ? ['  <defs>', ...defs.map(def => `    ${def}`), '  </defs>'] : []),
    ...body,
    '</svg>',
    '',
  ].join('\n');
}

/** Rows of cells as an SVG, a rectangle for each run of one colour. */
function cellsSvg(rows, colors) {
  const byColor = new Map();
  rows.forEach((row, y) => {
    for (let x = 0; x < row.length; ) {
      let end = x + 1;
      while (end < row.length && row[end] === row[x]) end++;
      if (row[x] !== '.') {
        const fill = colors[row[x]];
        byColor.set(fill, [...(byColor.get(fill) ?? []), `<rect x="${x}" y="${y}" width="${end - x}" height="1"/>`]);
      }
      x = end;
    }
  });
  const groups = [...byColor].map(([fill, rects]) => `  <g fill="${fill}">${rects.join('')}</g>`);
  return [
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${rows[0].length} ${rows.length}" shape-rendering="crispEdges">`,
    '  <!-- Drawn by packaging/icon/build-icons.mjs; change the sprite there. -->',
    ...groups,
    '</svg>',
    '',
  ].join('\n');
}

// The name in pixels, as Joulene is drawn, for the user guide's landing page: 5×7 letters a cell apart, from the
// site's blue through periwinkle to Joulene's rose, over a shadow a cell down and right in the deep blue.
const letters = {
  D: ['####.', '#...#', '#...#', '#...#', '#...#', '#...#', '####.'],
  E: ['#####', '#....', '#....', '####.', '#....', '#....', '#####'],
  V: ['#...#', '#...#', '#...#', '#...#', '#...#', '.#.#.', '..#..'],
  N: ['#...#', '##..#', '#.#.#', '#..##', '#...#', '#...#', '#...#'],
  T: ['#####', '..#..', '..#..', '..#..', '..#..', '..#..', '..#..'],
  R: ['####.', '#...#', '#...#', '####.', '#.#..', '#..#.', '#...#'],
  Y: ['#...#', '#...#', '.#.#.', '..#..', '..#..', '..#..', '..#..'],
};

function wordmark(word = 'DEVENTRY') {
  const rows = letters.D.map((_, y) => [...word].map(letter => letters[letter][y]).join('.'));
  const width = rows[0].length + 1;
  const runs = (dx, dy) => rows.flatMap((row, y) => {
    const rects = [];
    for (let x = 0; x < row.length; ) {
      let end = x + 1;
      while (end < row.length && row[end] === row[x]) end++;
      if (row[x] === '#') rects.push(`<rect x="${x + dx}" y="${y + dy}" width="${end - x}" height="1"/>`);
      x = end;
    }
    return rects;
  }).join('');
  return [
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${rows.length + 1}" shape-rendering="crispEdges">`,
    '  <!-- Drawn by packaging/icon/build-icons.mjs. -->',
    `  <title>${word[0]}${word.slice(1).toLowerCase()}</title>`,
    '  <defs>',
    `    <linearGradient id="brand" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="${width}" y2="0">`,
    '      <stop offset="0" stop-color="#4A6FC4"/><stop offset="0.5" stop-color="#8A93DA"/><stop offset="1" stop-color="#CF7F96"/>',
    '    </linearGradient>',
    '  </defs>',
    `  <g fill="#34539E" fill-opacity="0.3">${runs(1, 1)}</g>`,
    `  <g fill="url(#brand)">${runs(0, 0)}</g>`,
    '</svg>',
    '',
  ].join('\n');
}

function write(path, content) {
  const full = join(root, path);
  mkdirSync(dirname(full), { recursive: true });
  writeFileSync(full, content);
  console.log(`${path} (${content.length} bytes)`);
}

const logo = mark(prompt);
write('packaging/icon/deventry.svg', vectors(logo));
write('.idea/.idea.Deventry/.idea/icon.svg', vectors(logo));
write('packaging/icon/deventry-1024.png', png(render(dock(prompt), 1024)));
write('src/Deventry.App/Assets/deventry.ico', ico([16, 20, 24, 32, 40, 48, 64, 128, 256].map(size => render(logo, size))));
write('docs/assets/deventry.svg', vectors(logo));
write('docs/assets/joulene.svg', cellsSvg(joulene, mascotPalette));
write('docs/assets/favicon.ico', ico([16, 32, 48].map(size => render(logo, size))));
write('docs/assets/wordmark.svg', wordmark());

// The taskbar's sizes fill their square; the tiles' logos keep the margin Windows' tiles want, the sign two-thirds of
// the tile's height.
const assets = 'packaging/windows/Assets';
for (const size of [16, 20, 24, 30, 32, 36, 40, 48, 64, 256]) {
  write(`${assets}/Square44x44Logo.targetsize-${size}_altform-unplated.png`, png(render(logo, size)));
}
write(`${assets}/Square44x44Logo.png`, png(render(logo, 44)));
write(`${assets}/StoreLogo.png`, png(render(logo, 50)));
write(`${assets}/Square150x150Logo.png`, png(render(logo, 108, 150)));
write(`${assets}/Wide310x150Logo.png`, png(render(logo, 108, 310, 150)));

// The animations' frames, in the order AppIconAnimations shows them: the taskbar's overlays at 32, and the Dock's at
// 512, where the sign is exactly half its size in the 1024 icon, so the Dock icon doesn't shift when one starts or stops.
const frames = 'src/Deventry.App/Assets/AppIcon';
const lit = [1, 2, 3, 3];
lit.forEach((on, i) => write(`${frames}/chevrons-${i}.png`, png(render(overlayChevrons(on), 32))));
lit.forEach((on, i) => write(`${frames}/working-${i}.png`, png(render(dock(chevrons(on)), 512))));
[question, bang, question, bang].forEach((symbol, i) => write(`${frames}/asking-${i}.png`, png(render(dock(symbol), 512))));
sand.forEach((glassFrame, i) => write(`${frames}/hourglass-${i}.png`, png(render(overlayHourglass(glassFrame), 32))));
sand.forEach((glassFrame, i) => write(`${frames}/waiting-${i}.png`, png(render(dock(hourglass(glassFrame, 2.45, 9)), 512))));

// Joulene on the composer: her poses, props and hats, which the app draws cell by cell (Mascot/MascotArt).
write('src/Deventry.App/Assets/Mascot/mascot.json', mascotJson());
