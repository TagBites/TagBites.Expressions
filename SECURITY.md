# Security Policy

## Supported versions

Security fixes are provided for the latest released version.

| Version | Supported |
|---------|-----------|
| 1.1.x   | ✅        |
| < 1.1   | ❌        |

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Report vulnerabilities privately through GitHub: **Security → [Report a vulnerability](https://github.com/TagBites/TagBites.Expressions/security/advisories/new)**.

Include a description, affected version, and a minimal expression / configuration that reproduces the issue. We aim to acknowledge reports within a few business days and to release a fix or mitigation as soon as a valid issue is confirmed.

## Security model

This library parses a C# expression and compiles it to a delegate that runs as ordinary .NET code with the privileges of the host process. There is no runtime sandbox — safety comes from **restricting what an expression is allowed to reference**, not from isolating what it does once compiled.

By default the surface is deliberately narrow:

- **Reflection is disabled** (`AllowReflection = false`). Members of `Type` / `MemberInfo` are not callable, apart from a small read-only allowlist.
- **Types are opt-in.** An expression can only reference a type by name if it is a parameter type, one of a few common built-ins, or explicitly added to `ExpressionParserOptions.IncludedTypes`.
- **Expressions only.** No statements, loops, assignments or type definitions are parsed, so there is no place for arbitrary code blocks.

### What you are responsible for when evaluating untrusted input

The defaults reduce the attack surface but do not make evaluation of untrusted expressions unconditionally safe. When the expression text comes from an untrusted source, keep in mind:

- **Anything you expose can be called.** Whatever you put in `IncludedTypes`, `GlobalMembers` or return from a `CustomPropertyResolver` becomes reachable. Do not expose types or members with dangerous side effects (file system, process, network, environment, etc.).
- **Setting `AllowReflection = true` removes a key protection** — only do so for trusted input.
- **Resource exhaustion (denial of service) is still possible.** An expression can allocate large arrays or run expensive LINQ over large sequences. If the input is untrusted, run evaluation with appropriate limits (memory/CPU limits, or a separate process), and validate or constrain the input.

### Recommendation

- Trusted expressions (from your own code or configuration): default options are fine.
- Untrusted expressions (from end users, external systems): keep `AllowReflection = false`, expose only the minimal set of safe types and members you actually need, and enforce runtime limits.
