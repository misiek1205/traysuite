# Project language

Use English for project work across the repository, including READMEs, documentation, code comments, commit messages, pull requests, and issues.

Keep project artifacts in English even when a request is written in another language. Add or update translations only when explicitly requested. Preserve existing application localization when editing unrelated code.

# Pull requests and CI

Make changes on a feature branch and open a pull request targeting `main`. Merge through GitHub after the required Actions check passes against the current base and review conversations are resolved. Keep direct pushes, force pushes, and bypasses disabled on `main`. Merge coordinated public dependencies before the Pro pull request.

# Windows testing

Agents may use the project's dedicated Windows 11 x64 test VM on the Proxmox server for interactive validation when authorized access is available. See the private Pro repository instructions for the VM and console details. Use a clean test profile for Hub installation, activation, and uninstall tests, and coordinate disruptive actions on the shared VM. Report Windows ARM64 runtime coverage separately from x64 runtime tests and cross-builds.
