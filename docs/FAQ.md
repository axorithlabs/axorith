﻿# Frequently Asked Questions (FAQ)

### General

**Q: What is Axorith?**

A: Axorith is a Productivity OS that helps you automate your focus rituals. You can create "Session Presets" that, with one click, launch your apps, block distracting sites, start your music, and set up your entire digital environment for a specific task (like coding, writing, or gaming).

**Q: Is Axorith free?**

A: Yes, the core desktop application is and will always be free for personal, non-commercial use. We plan to offer optional paid services like cloud synchronization in the future to fund the project's development. See our [Monetization Philosophy](monetization.md) for more details.

**Q: What platforms does Axorith support?**

A: Axorith is built to be cross-platform and runs natively on Windows, macOS, and Linux.

### Technical

**Q: Why is the project split into a Client and a background Service?**

A: This client-server architecture ensures maximum stability. The background service manages your active session. If the user interface (Client) crashes for any reason, your session continues running without interruption. You can simply restart the Client and it will reconnect to the service.

**Q: How does the Site Blocker module work? Does it require admin rights?**

A: The Site Blocker communicates with a lightweight browser extension via Native Messaging. This is a secure, standard way for desktop applications to interact with browsers. It does **not** require administrator rights.

**Q: Is it safe to use modules that require authentication (like Spotify)?**

A: Yes. We are implementing a secure Authentication Provider system. You will manage your accounts (like Spotify) in a central, secure location within Axorith. Modules will request permission to use these accounts, similar to how mobile apps ask for permissions. Your sensitive tokens are stored securely on your machine and are never exposed directly to the modules.

### Community

**Q: How can I contribute?**

A: We welcome all contributions! Please read our [Contributing Guidelines](../CONTRIBUTING.md) to get started. You can report bugs, suggest features, improve documentation, or write your own modules.

**Q: Where can I find the latest development progress?**

A: Our [public YouTrack board](https://axorithlabs.youtrack.cloud/agiles/192-1/current) shows our live task list, bug reports, and current development status.

**Q: Where can I join the community?**

A: Join our [Discord Server](https://discord.gg/bEmxUzj6ta) to chat with the developers and other users!.