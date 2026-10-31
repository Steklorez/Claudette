// Draws Deventry's icon and Joulene (DESIGN.md §2, "Packaging and signing", "Renaming to Deventry"). The icon is the
// mouth of a cave, a burrow for dotBunny, in a grassy stone mound, with a prompt glowing in its dark; Joulene, the
// mascot on the composer, is a medieval lady. Both are 12×12 cells, and every size is drawn with a whole number of
// pixels per cell, so the small sizes stay crisp. Writes:
//   packaging/icon/deventry.svg               the icon as vectors, also Rider's project icon (.idea/.idea.Deventry/.idea/icon.svg)
//   packaging/icon/deventry-1024.png          the macOS icon, on a pale sky tile; build-dmg.sh scales it
//   src/Deventry.App/Assets/deventry.ico      the window and executable icon
//   docs/assets/deventry.svg, favicon.ico     the user guide's logo and favicon (GUIDE.md, "The site")
//   docs/assets/joulene.svg                   Joulene standing, for the guide's pages about her
//   docs/assets/wordmark.svg                  the name in pixels, for the guide's landing page
//   packaging/windows/Assets/*.png            the MSIX logos, and the taskbar's unplated sizes
//   src/Deventry.App/Assets/AppIcon/*.png     the animations' frames (DESIGN.md §10): the taskbar overlay's typing
//                                             prompt and running hourglass, and the Dock icon's cave typing, asking
//                                             with a hopping "?", and waiting by an hourglass (AppIconAnimations)
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

// ── The icon ─────────────────────────────────────────────────────────────
// The cave in its grassy mound, the dark of its mouth, and the ground in front of it.
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
  P: '#F0DFA6', // the prompt, glowing
  W: '#FFE7A3', // the prompt brighter, hopping
};
const tile = '#E4ECF7'; // a pale sky, behind the macOS icon
const cells = cave.length;

// The prompt in the dark, ">_", waiting for the next line: the chevron's three cells and the cursor's three.
const prompt = (cells = {}) => set(cave, { '7,4': 'P', '8,5': 'P', '9,4': 'P', '9,6': 'P', '9,7': 'P', '9,8': 'P', ...cells });
const sprite = prompt();
const cursorOff = { '9,6': 'D', '9,7': 'D', '9,8': 'D' };

// Working: the cursor types, a character and a blink at a time.
const typing = [
  prompt(),
  prompt({ '9,6': 'D', '9,7': 'P', '9,8': 'D', '9,9': 'P' }),
  prompt({ '9,6': 'P', '9,7': 'D', '9,8': 'P', '9,9': 'D' }),
  prompt(cursorOff),
];

// Needs you: the prompt turns into a "?" that hops, up a cell and brighter with a sparkle either side at the top of
// each hop, twice, then rests. It catches the eye by moving, not by changing colour.
function question(up) {
  const shape = ['.PP.', 'P..P', '..P.', '.P..', '....', '.P..'];
  const top = up ? 4 : 5;
  const cells = { '7,4': 'D', '8,5': 'D', '9,4': 'D', '9,5': 'D', ...cursorOff };
  shape.forEach((row, y) => [...row].forEach((ch, x) => { if (ch === 'P') cells[`${top + y},${5 + x}`] = up ? 'W' : 'P'; }));
  if (up) Object.assign(cells, { '4,4': 'W', '4,9': 'W' });
  return set(cave, cells);
}
const asking = [prompt(), question(false), question(true), question(false), question(true), question(false), question(false), question(false)];

// An hourglass, while a tab waits for a usage limit to reset: X its frame, E the empty glass, S the sand. A dark frame
// and pale glass, so it reads on a light or dark taskbar, and rose sand, Joulene's.
const hourglass = [
  'XXXXXXXXX',
  '.XEEEEEX.',
  '.XEEEEEX.',
  '.XEEEEEX.',
  '..XEEEX..',
  '...XEX...',
  '..XEEEX..',
  '.XEEEEEX.',
  '.XEEEEEX.',
  '.XEEEEEX.',
  'XXXXXXXXX',
];
const hourglassColors = { X: '#141413', E: '#F4F6FB', S: '#CF7F96' };

/**
 * The hourglass with sand in the lowest `top` rows of the top bulb and the lowest `bottom` rows of the bottom one, and
 * running through the neck between them while both have some.
 */
function sand(top, bottom) {
  const neck = hourglass.length >> 1;
  const middle = hourglass[0].length >> 1;
  const last = hourglass.length - 1;
  return hourglass.map((row, y) => [...row].map((ch, x) => {
    if (ch !== 'E') return ch;
    const inTop = y < neck && y >= neck - top;
    const inBottom = y > neck && y >= last - bottom;
    const running = top > 0 && bottom > 0 && x === middle && y >= neck;
    return inTop || inBottom || running ? 'S' : 'E';
  }).join(''));
}

/** Rows turned a quarter clockwise. */
const turn = rows => [...rows[0]].map((_, x) => rows.map((_, y) => rows[rows.length - 1 - y][x]).join(''));

// The sand runs down, rests, and the hourglass turns over: once sideways, then full at the top again.
const sandFrames = [sand(4, 0), sand(3, 1), sand(2, 2), sand(1, 3), sand(0, 4)];
sandFrames.push(turn(sandFrames[4]));

// The taskbar overlay while tabs work (Windows): the prompt in a little window of the cave's dark, typing as the icon
// does, edged in its stone so it reads on a light or dark taskbar.
const promptBox = [
  '.MMMMMMMMMMMM.',
  'MDDDDDDDDDDDDM',
  'MDDPDDDDDDDDDM',
  'MDDDPDDDDDDDDM',
  'MDDPDPPPDDDDDM',
  'MDDDDDDDDDDDDM',
  '.MMMMMMMMMMMM.',
];
const promptOverlay = [
  promptBox,
  set(promptBox, { '4,5': 'D', '4,6': 'P', '4,7': 'D', '4,8': 'P' }),
  set(promptBox, { '4,5': 'P', '4,6': 'D', '4,7': 'P', '4,8': 'D' }),
  set(promptBox, { '4,5': 'D', '4,6': 'D', '4,7': 'D' }),
];

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

// Pixels per cell at each size. At 24, 36 and 48, the taskbar at 100%, 150% and 200%, the cave fills the icon.
const scale = { 16: 1, 20: 1, 24: 2, 30: 2, 32: 2, 36: 3, 40: 3, 44: 3, 48: 4, 50: 3, 64: 5, 128: 9, 150: 8, 256: 18 };

// ── Drawing ──────────────────────────────────────────────────────────────
const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));

/** A transparent RGBA image. */
const image = (width, height) => ({ width, height, data: Buffer.alloc(width * height * 4) });

/** A sprite at `k` pixels per cell, its top-left corner at (x, y). */
function drawSprite(img, k, x, y, rows = sprite, colors = palette) {
  rows.forEach((row, cy) => [...row].forEach((ch, cx) => {
    if (ch === '.') return;
    const pixel = [...rgb(colors[ch]), 255];
    for (let py = y + cy * k; py < y + (cy + 1) * k; py++) {
      for (let px = x + cx * k; px < x + (cx + 1) * k; px++) {
        img.data.set(pixel, (py * img.width + px) * 4);
      }
    }
  }));
}

/** A rounded square, its edges antialiased from 4×4 samples a pixel. */
function drawTile(img, x, y, size, radius, hex) {
  const [r, g, b] = rgb(hex);
  const n = 4;
  for (let py = y; py < y + size; py++) {
    for (let px = x; px < x + size; px++) {
      let inside = 0;
      for (let sy = 0; sy < n; sy++) {
        for (let sx = 0; sx < n; sx++) {
          const fx = px - x + (sx + 0.5) / n;
          const fy = py - y + (sy + 0.5) / n;
          const cx = Math.min(Math.max(fx, radius), size - radius);
          const cy = Math.min(Math.max(fy, radius), size - radius);
          if ((fx - cx) ** 2 + (fy - cy) ** 2 <= radius * radius) inside++;
        }
      }
      if (inside > 0) img.data.set([r, g, b, Math.round((255 * inside) / (n * n))], (py * img.width + px) * 4);
    }
  }
}

/** The icon centred in a transparent `width`×`height` image. */
function icon(width, height = width) {
  const k = scale[height];
  const img = image(width, height);
  drawSprite(img, k, Math.floor((width - cells * k) / 2), Math.floor((height - cells * k) / 2));
  return img;
}

/**
 * The macOS icon, or a frame of the Dock's animation: Apple's grid, an 824/1024 rounded square with the cave at 58% of
 * its width. The frames are drawn at 512, where the cave is exactly half its size in the 1024 icon, so the Dock icon
 * doesn't shift when an animation starts or stops.
 */
function macIcon(size = 1024, rows = sprite) {
  const img = image(size, size);
  const unit = size / 1024;
  drawTile(img, 100 * unit, 100 * unit, 824 * unit, 185 * unit, tile);
  const k = 40 * unit;
  const at = size / 2 - (cells * k) / 2;
  drawSprite(img, k, at, at, rows);
  return img;
}

/**
 * A frame of a taskbar overlay: 32 px, as Windows wants overlays at twice their 16 px, 2 px a cell, as near the middle
 * as an even pixel goes, so its cells stay whole when the taskbar halves it at 100%.
 */
function overlayIcon(rows, colors) {
  const img = image(32, 32);
  const even = cells => 2 * Math.floor((16 - cells) / 2);
  drawSprite(img, 2, even(rows[0].length), even(rows.length), rows, colors);
  return img;
}

/**
 * A frame of the Dock's waiting animation: the cave with its prompt resting, and the hourglass on the tile's
 * bottom-right corner, like a badge, its cells three-fifths of the cave's so it reads at Dock sizes.
 */
function waitingIcon(rows) {
  const size = 512;
  const img = macIcon(size, prompt(cursorOff));
  const unit = size / 1024;
  const k = 24 * unit;
  const centre = 808 * unit;
  drawSprite(img, k, centre - (rows[0].length * k) / 2, centre - (rows.length * k) / 2, rows, hourglassColors);
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

/** Rows of cells as an SVG, a rectangle for each run of one colour. */
function svg(rows = sprite, colors = palette) {
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

// The name in the same pixels as the icon, for the user guide's landing page: 5×7 letters a cell apart, from the
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

write('packaging/icon/deventry.svg', svg());
write('.idea/.idea.Deventry/.idea/icon.svg', svg());
write('packaging/icon/deventry-1024.png', png(macIcon()));
write('src/Deventry.App/Assets/deventry.ico', ico([16, 20, 24, 32, 40, 48, 64, 128, 256].map(size => icon(size))));
write('docs/assets/deventry.svg', svg());
write('docs/assets/joulene.svg', svg(joulene, mascotPalette));
write('docs/assets/favicon.ico', ico([16, 32, 48].map(size => icon(size))));
write('docs/assets/wordmark.svg', wordmark());

const assets = 'packaging/windows/Assets';
for (const size of [16, 20, 24, 30, 32, 36, 40, 48, 64, 256]) {
  write(`${assets}/Square44x44Logo.targetsize-${size}_altform-unplated.png`, png(icon(size)));
}
write(`${assets}/Square44x44Logo.png`, png(icon(44)));
write(`${assets}/StoreLogo.png`, png(icon(50)));
write(`${assets}/Square150x150Logo.png`, png(icon(150)));
write(`${assets}/Wide310x150Logo.png`, png(icon(310, 150)));

// The animations' frames, in the order AppIconAnimations shows them.
const frames = 'src/Deventry.App/Assets/AppIcon';
promptOverlay.forEach((rows, i) => write(`${frames}/prompt-${i}.png`, png(overlayIcon(rows, palette))));
typing.forEach((rows, i) => write(`${frames}/typing-${i}.png`, png(macIcon(512, rows))));
asking.forEach((rows, i) => write(`${frames}/asking-${i}.png`, png(macIcon(512, rows))));
sandFrames.forEach((rows, i) => write(`${frames}/hourglass-${i}.png`, png(overlayIcon(rows, hourglassColors))));
sandFrames.forEach((rows, i) => write(`${frames}/waiting-${i}.png`, png(waitingIcon(rows))));

// Joulene on the composer: her poses, props and hats, which the app draws cell by cell (Mascot/MascotArt).
write('src/Deventry.App/Assets/Mascot/mascot.json', mascotJson());
