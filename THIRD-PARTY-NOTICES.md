# Third-party notices

FolderChat includes or depends on the following open-source components.

## Bundled in `src/FolderChat.Web/wwwroot/lib`

Each folder contains the component's license file.

| Component | Version | License |
|---|---|---|
| [Bootstrap](https://getbootstrap.com) | 5.3.3 | MIT |
| [marked](https://github.com/markedjs/marked) | 18.1.1 | MIT |
| [DOMPurify](https://github.com/cure53/DOMPurify) | 3.4.17 | Apache-2.0 or MPL-2.0 |
| [highlight.js](https://highlightjs.org) | 11.12.0 | BSD-3-Clause |

## Icons

The icons in `src/FolderChat.Web/Views/Shared/_Icons.cshtml` come from [Lucide](https://lucide.dev) 0.460.0, under the ISC License:

> Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.
>
> Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## NuGet packages

| Package | License |
|---|---|
| Anthropic (official Anthropic SDK) | Apache-2.0 |
| OpenAI (official OpenAI SDK) | MIT |
| Microsoft.Extensions.AI.* | MIT |
| OllamaSharp | MIT |
| PdfPig (reads attached PDFs) | Apache-2.0 |
| DocumentFormat.OpenXml (Open XML SDK, reads attached Word documents) | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | MIT |
| Serilog.AspNetCore | Apache-2.0 |
| Microsoft.AspNetCore.OpenApi and Microsoft.OpenApi (describe the REST API) | MIT |
| xunit.v3 (tests only) | Apache-2.0 |

Versions are pinned in `Directory.Packages.props`.
