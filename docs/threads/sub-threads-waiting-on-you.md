---
title: Sub-threads waiting on you
parent: Threads
nav_order: 4
summary: "A sub-thread is a whole tab, so it stops for your permission, questions and plans like any other."
---

# Sub-threads waiting on you

A sub-thread is a whole tab, so it stops for your permission, questions and plans like any other. Claudette never answers those for you, and the thread's Claude never hears of them. They'd be easy to miss while you're watching the thread, so the thread shows them in a strip over its composer.

## The strip

For each sub-thread waiting on you, the strip has a line with the sub-thread's name and what it asks, as its notification says it: *Art page: Allow this command? npm test*. Beside it:

- **Allow** and **Deny**, for a permission prompt. They answer the sub-thread's own prompt, through its own session, and cost no tokens.
- **Go to tab**, for every prompt. It selects the sub-thread, where the card is. A question or a plan to approve has no **Allow** or **Deny** in the strip, since they need more than a yes or no, so answer them in the sub-thread's tab ([Questions](../permissions/questions.md), [Approving a plan](../permissions/approving-a-plan.md)).

The strip also holds the thread's own waiting messages: the *Send this to …?* questions from [Sending work and getting reports](sending-work-and-getting-reports.md).

The strip is empty and hidden when nothing waits.

## Elsewhere

Each sub-thread's row in the [sidebar](../tabs/the-sidebar.md) says *Needs your input* while it waits, and you get a [notification](../away/notifications.md) for it as for any tab. `Ctrl/Cmd+J` goes to the next tab waiting for you.

A card in the sub-thread's tab works as always: [Permission cards](../permissions/permission-cards.md) covers **Always allow…** and the rest, which the strip doesn't offer.

## See also

- [Making a thread](making-a-thread.md): **Go to thread**, to get back from a sub-thread.
