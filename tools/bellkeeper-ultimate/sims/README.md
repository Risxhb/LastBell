# Phase simulations (proposal pages, not the game)

Browser pages that show perfect bots doing one Ultimate v2 phase, for Chzy18 to judge before anything is built in Unity.

P2 "Broken Hour" (relay plates, Gear Train chains, Inverted Bell):

```bash
node p2solve.js p2plan.json         # solve a perfect route beat by beat (prints "fails 0")
node build-p2.js p2-example.html    # page = p2-template.html + the plan
node test-p2.js p2-example.html     # headless: perfect must be clean; each mistake button must wipe
```

Open the page with `#t=17.6` to jump to a moment, and `#m=pass|half|chain` to start with a mistake.

P3 "The Open Hour" (bullet hell: Mainspring, Rewound Shot, Echo of You, Bell Curtains, plates), one self-contained page:

```bash
node test-p3.js p3-example.html 10   # perfect over 10 start jitters (9/10 clean at commit), then each mistake must cost the run
```

The bots search 3-leg routes (0.2 s legs, best 10 kept per leg) against predicted bullets, echoes and curtains.
Open with `#t=22.4still` to freeze at a moment, `#m=rewind|still|gap|late` to start with a mistake.
