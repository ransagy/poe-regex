# Contributing

Thanks for helping make this project more accurate for the PoE community.

Contributions come in three flavors — all welcome via pull requests:

1. **Add test cases** — new query→expected-string cases that exercise grammar
   behavior we don't cover yet (see `tests/PoeRegex.Core.Tests/CompilerTests.cs`
   for the pattern).
2. **Fix cases** — if a case's expected output is wrong or the engine produces
   an incorrect query, fix the test and/or the engine and add a regression case.
3. **Report wrong cases** — open an issue describing the wrong output and the
   correct behavior you observed in-game.

## Adding a test case

Test cases are self-authored examples modeled on the functional behavior of the
trade sites — not copied from wikis, forums, subreddits, or guide sites (their
content is not licensed for reproduction; see `docs/DESIGN.md` §1). Because
trade-search query strings are functional optima, a correct case will match what
community tooling produces anyway.

1. Add your input (`QuerySpecification`) and the exact expected `Query` string
   to the relevant test class.
2. Run `dotnet test PoeRegex.slnx` and make sure the suite passes.

## Licensing of contributions

This project is MIT-licensed. By submitting a pull request, you agree that your
contribution is licensed in-bound under the same MIT license, so the project can
distribute it freely. No separate contributor-license agreement is required.

## Getting started

```sh
dotnet build PoeRegex.slnx
dotnet test PoeRegex.slnx
```
