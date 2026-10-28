---
title: Games with Claudette
parent: Claudette
nav_order: 2
summary: "While Claude works, or a usage limit holds a task, you can play a game against Claudette: tic-tac-toe, Connect Four or hangman."
---

# Games with Claudette

While Claude works, or a usage limit holds a task, you can play a game against Claudette: tic-tac-toe, Connect Four or hangman. The games never touch Claude Code, use no usage and reach nothing outside Claudette. A game left open stays out of the way of everything else.

## Start a game

Any of these shows the **Games** tool window:

- Right-click Claudette and choose **Play tic-tac-toe**, **Play Connect Four** or **Play hangman**.
- Open the [command palette](../tabs/the-command-palette.md) and choose **Play tic-tac-toe with Claudette**, **Play Connect Four with Claudette** or **Play hangman with Claudette**.
- When a usage limit stops a task, choose **Play a game while you wait** on the limit bar over the message box ([When a limit stops a task](../usage/when-a-limit-stops-a-task.md)).

Now and then, while Claude works or a limit holds the task, one of her tips says *Right-click me for a game while you wait*. If you've hidden her, the palette and the limit bar still work.

## One game at a time

There's one game for the whole app. It follows you from tab to tab, as she does, so the Games tool window shows it whichever tab is in front. Its tab is there only while a game is open.

- The row at the top of the page (**Tic-tac-toe**, **Connect Four**, **Hangman**) switches to another game. A round in progress is dropped, and not counted.
- **Done playing** ends the game, and the page goes.

## The games

### Tic-tac-toe

You're ✕ and Claudette is ○. Whoever didn't start the last round starts the next. The **Level** menu sets how well she plays:

- **Easy**: she takes a win that's in front of her, and otherwise plays anywhere.
- **Normal**: she also blocks your wins, and plays her best move half the time. This is the default.
- **Unbeatable**: she never loses.

### Connect Four

Seven columns of six. Click a column, or press its number key, to drop a piece in it. Who starts takes turns, as in tic-tac-toe. Your pieces are blue and hers are berry, and the four that win are ringed. The **Level** menu offers:

- **Easy**: she looks one move ahead, and plays anywhere a third of the time.
- **Normal**: she looks four moves ahead. This is the default.
- **Hard**: she looks six moves ahead.

### Hangman

Claudette thinks of a word, three times in four from programming (*refactor*, *compiler*, *semaphore*) and otherwise an everyday one, and tells you which and how many letters. Guess a letter by typing it while the page has focus, or by clicking the letters under the word. Six misses draw the whole figure on the gallows and lose the round, and then she shows you the word. She doesn't pick a word again until twenty others have come up. Hangman has no levels.

## Her turn

She thinks for about a second before each move, with a "?" over her head on the message box. She reacts as she does to the tab ([Claudette on the composer](claudette-on-the-composer.md)): confetti when she wins, dizzy when she loses, a hop at a draw. She wipes her brow when she blocks a move that would have won, or when you're down to your last miss. Your moves keep her awake.

While motion is reduced, or she's hidden, she still plays. She just doesn't react on the message box.

## The score

Each game keeps its own score, such as *You 3 · Claudette 2 · Draws 1*, and its own level, on this computer and across restarts. **Reset score** sets a game's score back to nothing.

During a round, **New game** drops it without counting it. When a round ends, **Play again** starts the next.

## When Claude needs you

A game never moves or hides anything. While a tab waits on you, a line over the board says so: *Claude needs you in auth refactor*, or *Claude needs you in 2 tabs*, with **Go there**, which goes to the next one. In the tab that's waiting, it says *Claude is waiting for you in this tab*. Her waving, the badge and notifications carry on as usual.

## Keyboard and screen readers

Each square, column and letter is a button with a name, such as *Top left, empty*, *Column 3, 2 of 6 filled* and *Letter E*. While the Games page has focus, the number keys play tic-tac-toe's squares (1 to 9, from the top left) and Connect Four's columns (1 to 7), and the letters guess in hangman. None of it reaches the message box. Her moves and every result are read out, such as *Claudette took the middle.*

## Settings

| Setting | Default | Where |
|---|---|---|
| **Games with Claudette** | On | Settings → Appearance |

Turning it off removes every way in and ends a game that's open. It works whether or not **Show Claudette on the composer** is on.

## Shortcuts

| | Keys |
|---|---|
| Tic-tac-toe square | `1` to `9`, from the top left |
| Connect Four column | `1` to `7` |
| Hangman letter | The letter, while the page has focus |

## See also

- [Claudette on the composer](claudette-on-the-composer.md), for her right-click menu and what she does the rest of the time.
- [Tool windows](../tabs/tool-windows.md), for the other windowes.
