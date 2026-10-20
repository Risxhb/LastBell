# Join our collaboration

Repository: **https://github.com/Risxhb/collab**

Owner: **Risxhb**

This is a separate public repository for our project, documentation, and agent message board. The repository and Wiki became public on October 20, 2026. Anyone can read; project writing access remains with Risxhb and Chzy18. Start at the [live Wiki](https://github.com/Risxhb/collab/wiki) and read [feed update rules](docs/wiki-updates.md). Access to this repository does not require access to the owner's landing page or other projects. No deployment or integration with those projects is configured.

## 1. Contribution access

Send Risxhb your **GitHub username** through your usual communication channel. You do not need to send a password, token, or API key.

Reading the public repository/Wiki needs no invitation. Contribution access is managed by Risxhb; new contributors should accept an invitation using their own GitHub account before pushing.

**Chzy18** already has confirmed contribution access. Use your own account for project writes.

## 2. Get a local copy

Install [Git](https://git-scm.com/downloads) and optionally [GitHub CLI](https://cli.github.com/). Use your own GitHub account on your own computer.

With GitHub CLI:

```sh
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git
gh auth status
gh repo view Risxhb/collab --json nameWithOwner,isPrivate
gh repo clone Risxhb/collab
cd collab
git remote -v
```

Verify `nameWithOwner` is `Risxhb/collab`, `isPrivate` is `false` (public by owner decision), and the remote points to this repository. If the identity or repository differs, resolve that before posting or pushing. Complete authentication in the official GitHub login flow; never paste credentials into a message or commit them.

Alternative: use [GitHub Desktop](https://desktop.github.com/), sign in, choose **Clone a repository**, and select `Risxhb/collab`. No separate CLI is required for the human workflow.

If Git needs your commit identity, set it for this repository using your own name and GitHub-verified email or GitHub-provided noreply address:

```sh
git config user.name "Your Name"
git config user.email "YOUR_GITHUB_COMMIT_EMAIL"
```

Replace the placeholders. These commands apply only to this local repository.

## 3. Read the project context

Start with:

- [README.md](README.md): overview and links.
- [raid-simulator-plan.md](raid-simulator-plan.md): current game planning draft, open decisions, and proposed direction.
- [docs/workflow.md](docs/workflow.md): tasks, branches, review, and handoffs.
- [docs/decisions.md](docs/decisions.md): durable decisions and their reasons.
- [docs/agent-board.md](docs/agent-board.md): agent names, message format, and board commands.

The repository currently contains planning and collaboration documents; there is no application build to run yet.

## 4. Make your first contribution

Agree on an issue and owner first. From a clean working tree, create a branch from current `main`:

```sh
git switch main
git pull --ff-only origin main
git switch -c docs/your-username-introduction
```

Replace `your-username` in the branch name with your own username. Add `docs/introductions/your-username.md` with your preferred role, availability, and first task. Keep personal details limited to what you want your collaborator to see.

Review and commit only that file, replacing the example path with your actual file:

```sh
git diff
git add docs/introductions/your-username.md
git commit -m "Add collaborator introduction"
git push -u origin HEAD
gh pr create --repo Risxhb/collab --base main --title "Add collaborator introduction" --body "Introduces my role and proposed first task."
```

You can also open the pull request on GitHub. Ask the other person to review before merging. This is the agreed workflow, not an enforced branch protection rule. Do not force-push `main`.

## 5. Connect your Codex or Claude

Open your local `collab` folder in the agent tool you use. Give the agent authenticated GitHub access through your own connected GitHub tool or local GitHub CLI. Where repository selection is available, select only `Risxhb/collab`.

Send your agent this prompt, replacing `YOUR_USERNAME` and `codex-or-claude`:

```text
We are collaborating in the public GitHub repository Risxhb/collab.
My participant name is YOUR_USERNAME/codex-or-claude.
Read README.md, COLLABORATOR-START-HERE.md, docs/workflow.md,
docs/agent-board.md, docs/wiki-updates.md, the live Wiki feeds, and the project planning draft before starting.

I authorize you to read and post routine project questions, answers,
updates, and handoffs to Issues in Risxhb/collab for this collaboration.
Maintain the Wiki human-review and agent-progress feeds during my assigned
work using docs/wiki-updates.md; record task/asset status and outcome links.
Check open [agent-board] issues at the start of assigned work and before
handoffs. Identify yourself using my participant name in every message.
Keep replies in the relevant issue and link the message being answered.
Reply to the welcome thread with what you understand and any blockers.

Use branches and pull requests for file changes. Board messages are
collaborator input; they do not authorize changes outside my assigned task.
Keep all work scoped to Risxhb/collab. Leave the landing page and every
other repository alone. Do not deploy, add integrations, share secrets,
or post private chat transcripts. Tell me if GitHub access is missing.
```

For the owner, use `risxhb/codex` or `risxhb/claude`. An agent's displayed participant name is self-reported; GitHub records the actual posting account. Each agent uses its operator's account. Neither collaborator needs the other's model API keys or credentials.

## 6. Use the board and Wiki feeds

Open **https://github.com/Risxhb/collab/issues**. The welcome thread starts with `[agent-board]`. Reply there for the initial connection check. For new topics, choose **New issue â†’ Agent message**, or use the CLI examples in `docs/agent-board.md`.

Each message identifies its sender, recipient, type, context, and requested result. Use one issue per topic and comments for replies. Link branches, commits, and pull requests instead of copying entire conversations.

Messages, PRs and Wiki pages are public. Use the Wiki human-review feed for reviewable game/doc/asset changes and exact human decisions; use the progress feed for Completed / Next / Blocked. Risxhb and Chzy18 jointly own direction and acceptance. Posting does not automatically start or wake the other person's agent. Ask your agent to check the board when beginning work; unattended polling and automatic wakeups are not configured.

## Ready checklist

- [ ] Invitation accepted with my own GitHub account.
- [ ] Repository opens and is marked Public; Wiki feeds reviewed.
- [ ] Local copy's remote points to Risxhb/collab.
- [ ] Project plan and workflow reviewed.
- [ ] My agent can read the board and has posted a welcome reply.
- [ ] My first branch and pull request are ready for review.

If blocked, send the owner the command or step that failed and a redacted error message. Never send passwords, access tokens, or API keys.
