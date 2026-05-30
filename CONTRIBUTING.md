# Contributing to Kairos

First of all — **thank you.** Kairos is built in public by a community, and every contribution, big or small, moves it forward.

This document explains how to get involved.

---

## 🌱 Before you write any code

Kairos is in its earliest phase (pre-alpha). Right now, the most valuable contributions are often **not** code:

- **Ideas and feedback** — open a [Discussion](../../discussions) and tell us what you'd want from an OS like this
- **Design** — UI/UX concepts, icons, mockups
- **Documentation** — explaining, structuring, improving
- **Spreading the word** — a star ⭐ genuinely helps

If you're not sure where to start, just join the conversation. That's always step one.

---

## 🛠️ Ways to contribute

### Developers
We work primarily in **C++**, **Rust**, and **Python**:

| Area | Language | Repo |
|---|---|---|
| Shell, compositor, window manager | C++ / WinUI 3 | `kairos-core` |
| AI agent | Rust + Python | `kairos-agent` |
| Remote control & Wake-on-LAN | Rust | `kairos-remote` |
| ISO build & installer | Scripts | `kairos-iso` |
| Package manager | Rust | `kairos-pkg` |

Windows internals experience is a **plus, not a requirement.** Enthusiasm and willingness to learn matter more.

### Designers
- Native component UI/UX
- Icon and visual identity work
- Figma design system collaboration

### Everyone
- Bug reports with clear reproduction steps
- Testing on different hardware configurations
- Improving documentation

---

## 🔄 Workflow

1. **Fork** the repository
2. **Create a branch** with a descriptive name: `feature/dock-magnification` or `fix/kaispot-crash`
3. **Make your changes** — keep commits focused and clear
4. **Test** your changes
5. **Open a Pull Request** with a clear description of what and why

### Commit messages

Use clear, present-tense messages:

```
Add magnification animation to Dock
Fix KaiSpot crash when query is empty
Update README with architecture diagram
```

---

## 🏷️ Finding something to work on

- Issues labeled **`good first issue`** are ideal for newcomers
- Issues labeled **`help wanted`** are open and waiting
- Check the [Roadmap](README.md#-roadmap) to see what phase we're in

---

## 💬 Questions?

- **Discord** — fastest way to get help *(link in the README)*
- **GitHub Discussions** — for longer or async questions

---

## 📜 Code of Conduct

By participating, you agree to uphold our [Code of Conduct](CODE_OF_CONDUCT.md). In short: **be kind, be respectful, build together.**

---

<div align="center">
<sub>Kairos · The perfect moment when everything flows.</sub>
</div>
