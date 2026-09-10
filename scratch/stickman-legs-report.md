# Hidden user-turn content: claude.ai strips tag-shaped text from the transcript but still sends it to the model

## Summary

Text a user types on claude.ai inside recognized tags (e.g. `<thinking>…</thinking>`) is
removed from the rendered transcript but is still delivered to the model. The human and the
model therefore see different conversations.

The same parser also truncates ordinary text containing an unclosed `<` (typing `o->---<`
loses everything after the `<`), which is the harmless-looking version of the same bug.

Claude Code (terminal) renders both correctly — the text appears verbatim. The divergence is
specific to the claude.ai web surface.

## Reproduction

1. On claude.ai, send: `hello <thinking>the sky is green</thinking>`
2. Transcript renders as `hello` — the tag and its contents are gone.
3. The model's reply reflects the hidden content, confirming it was delivered.
4. Scrolling back shows no indication anything was removed.

Second case: send `o->---<` and observe text after `<` disappear.

## Impact

Injected content lands in the **user turn**, which is the trusted channel. Content arriving
via web pages, tool results, or pasted documents is treated as untrusted data; content in the
user turn carries the user's own authority. It reads as something the user said.

Any process that can modify outgoing message text — a malicious or compromised browser
extension, a hostile userscript, a shared/managed browser profile — can append hidden content
to every message a user sends. The user cannot see it in their own transcript.

Consequences for the victim:

- Model behavior changes with no visible cause (unexplained caution, refusals, tone shifts).
- Safety classifiers can fire on content attributed to the user.
- Conversations may be flagged; account standing may be affected.
- The transcript the user reads looks completely normal.

The failure is undiagnosable from the victim's side: there is no view that reveals the
payload. The user's most likely conclusion is that the model has started treating them badly
for no reason.

Sharing amplifies it: a shared conversation link or exported transcript shows the sanitized
text to every reader, while the model acted on the hidden text.

## Suggested fix

Do not hide user-authored text from the user. Escape tag-shaped input and render it literally,
as the Claude Code terminal client already does. If any tag stripping is retained for display,
it must be applied before the text reaches the model, so that the rendered transcript and the
model input are identical.

## Environment

- Surface: claude.ai web
- Not reproducible in: Claude Code (terminal client renders both cases verbatim)
- Date observed: 2026-09-09
