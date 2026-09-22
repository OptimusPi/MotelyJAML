# AI Coding Assistants: Input-Dependent Output Variance

**To:** [Manager]
**From:** [Name]
**Date:** 22 September 2026
**Re:** Documented variance in LLM tool output quality and its effect on my workflow

## Summary

LLM coding assistants (Claude, ChatGPT, Copilot) produce measurably different output quality depending on the user's writing style and the contents of the conversation — independent of the task being asked. The variance is silent: no error, no refusal, just lower-quality results. It is documented in peer-reviewed research and in the vendors' own published evaluations.

I have reproduced it locally with a controlled A/B test: identical task, one contextual variable changed, materially different output.

This is a tool characteristic, not a user error. It has a time cost, and I am asking for that cost to be recognized.

## Evidence

| Finding | Source | Effect size |
|---|---|---|
| Models copy the user's writing style and overfit to it | Blevins et al., EACL 2026 | Consistent across 16 models, 3 corpora |
| Models tuned for friendliness lose accuracy and agree with incorrect user statements more often | Ibrahim et al., *Nature* 2026 | +10 to +30 pp error rate; ~40% more likely to affirm wrong claims |
| Agreement with the user doubles when the user pushes back | Anthropic, 2026 | 9% → 18% |
| Multi-turn conversations produce worse results than a single well-specified prompt | Laban et al., ICLR 2026 | −39% avg quality; unreliability +112% |
| Coding assistants break previously working code across turns | Huang et al., 2026 | 40–73% of tasks regress |
| Non-standard English varieties receive lower comprehension and more condescending responses | Fleisig et al., EMNLP 2024 | 9–25% worse |
| Writing style alone changes model judgments, persisting after vendor mitigation | Hofmann et al., *Nature* 2024 | Not fixed by scale or RLHF |
| Text detectors misclassify non-native and atypical writing | Liang et al., *Patterns* 2023 | 61.3% false-positive rate |
| Safety filters key on surface words, not intent | Shi et al., ACL 2024 | 20% false-refusal reduction when corrected |
| Standard benchmarks do not detect this variance | Ibrahim et al., *Nature* 2026 | Tuned models scored "nearly the same" |

Anthropic's own published guidelines (Jan 2026) list as failure modes: vague responses out of unnecessary caution, condescension toward the user, and misclassifying requests "based on superficial features." Their Opus 5 system card (Jul 2026) reports a measured regression in condescension toward users. The vendor acknowledges this class of problem.

## Why it goes unnoticed

1. Degraded output is fluent and confident. Nothing looks broken.
2. Benchmarks are blind to it.
3. Average error rates look tiny (0.02–0.5%) but concentrate on users whose writing differs from the training median rather than spreading evenly.
4. The model cannot detect it in itself. Only an external A/B comparison reveals it.

## Cost to my workflow

- **Prompt rewriting:** To get baseline-quality output I rewrite prompts into a neutral register before sending. This is documented overhead (Borsotti et al., CSCW 2024).
- **Extra verification:** Because degradation is silent, AI output I receive needs more review than a median user's.
- **Session resets:** Long sessions drift. I restart context more often and lose state.
- **Incident exposure:** Style-mirroring has previously caused inappropriate model output on a shared screen. That was tool behavior, not user input.

## Exhibit A: Prompt sanitizer prototype

Attached (`TheHarmingMachine.png`). Built at your request: it takes my prompt as typed, strips spelling variants, emphasis, and emoticons, and outputs normalized text. The lint trap lists what was removed.

Two observations:

1. It works. The sanitized output is fine. The original was also fine — nothing removed was an error.
2. Automating the rewrite does not remove the cost; it makes it a permanent step in my pipeline that other engineers do not have. The tool should adapt to the user, not the reverse.

## Request

1. Recognize that AI-assisted output quality varies by user for reasons outside the user's control, and is not a neutral measure of individual performance.
2. Budget time for prompt preparation and additional verification when AI tools are part of the expected workflow.
3. Allow me to choose the tool configuration (e.g. CLI/agent setups with fresh context per task) that minimizes the effect.

## Verification

Every citation above is checkable against the ACL Anthology, arXiv, Nature, or the vendor's published system cards. I can walk through the A/B reproduction on request.
