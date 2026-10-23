# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub: on the repository's **Security** tab, choose **Report a vulnerability**. Don't open a public issue.

Include the steps to reproduce and the version or commit you tested. You should get a reply within a week.

## Supported versions

FolderChat is pre-1.0. Security fixes go into the latest release only.

## Security model

FolderChat runs on your own computer and stores API keys for paid services, so it is built to be reachable only by you:

- **Network:** by default it listens on `127.0.0.1` only.
- **DNS rebinding:** requests whose `Host` header isn't `localhost`, `127.0.0.1` or `[::1]` are refused, so a website you visit can't reach the app by pointing its own domain at your machine.
- **Cross-site requests:** every change request must carry an anti-forgery token, which other websites can't read.
- **API keys:** keys are encrypted with ASP.NET Core Data Protection. On Windows, the key ring is also protected with DPAPI for your user account. The API never returns a key.
- **Model output:** replies are rendered as Markdown and sanitized with DOMPurify before they reach the page.
- **Attached files:** files are read on your computer. FolderChat keeps only their text, in the local database, and sends it to the model you picked. It reads at most 20 MB per file and caps how far a Word file can expand. Files that are attached but never sent are deleted after a day.

What it does **not** protect against:

- Other programs running as your user on the same computer.
- Anything you send to a cloud provider. That provider receives your messages, and the text of any files attached to them, under its own terms.
- Files built to attack the PDF or Word readers. They run inside FolderChat, so a malicious file could slow it down or stop it.
- Exposing the app to a network with `--urls http://0.0.0.0:...`. FolderChat has no sign-in yet.
