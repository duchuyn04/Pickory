# Pickory Agent Rules

These rules apply to all work in this repository, including code, tests, comments, documentation, commit messages, UI copy, and responses to the user.

`MUST` and `MUST NOT` are mandatory requirements. `SHOULD` identifies a preference, not permission to bypass a mandatory requirement. A task is not complete while an applicable requirement remains unmet.

## 1. Punctuation

- MUST use the ordinary ASCII hyphen (`-`, U+002D) when joining phrases.
- MUST use commas, colons, parentheses, or separate sentences when a pause is needed.
- MUST NOT write U+2014 (em dash) or U+2013 (en dash) in any newly authored or modified content, including chat responses and examples.
- MUST check changed content for both prohibited characters before reporting completion.
- MUST NOT copy prohibited punctuation from source material into new content.

## 2. Playwright Manual Test Cases

Apply this section whenever the user requests manual test cases for a feature or module.

### Scope and format

- MUST design at most 100 cases per request. If coverage would exceed this limit, select the most important flows and explicitly list the omitted scope.
- MUST write executable Playwright scenarios, with concrete setup, locators, actions, and assertions. A description of a human clicking through the UI is not sufficient.
- MUST give every case these fields: ID, Group, Steps, Expected Result, Priority, Status, and Notes.
- MUST use a unique, stable ID for every case.
- MUST describe an observable expected result. Statements such as "works correctly" are not acceptance criteria.
- MUST sort cases by priority: P0, P1, P2, then P3.
  - P0: security, data integrity, and flows that must not fail.
  - P1: main business flows.
  - P2: input validation and error handling.
  - P3: usability, accessibility, and visual presentation.

### Status and evidence

Status MUST be exactly one of `Chưa chạy`, `PASS`, `FAILED`, or `UNKNOWN`. Keep these values unchanged even in an English document.

- `Chưa chạy`: the scenario has not been executed. This is the initial status.
- `PASS`: Playwright executed the scenario and the observed result matched every expected result.
- `FAILED`: an observed result differed from the expected result. Notes MUST identify the actual result and reference a screenshot or log as evidence.
- `UNKNOWN`: execution or verification was not possible in the current environment. Notes MUST identify the specific blocker and any checks that could not be performed.

- MUST NOT mark a case `PASS` based on code inspection, assumptions, a previous run, or a different scenario.
- MUST NOT treat a partial run as proof that the entire case passed.
- MUST NOT change Steps, Expected Result, or Priority just to turn a failing case into a passing case.
- If a case appears incorrect, MUST report the discrepancy to the user and preserve the original acceptance criteria until the user authorizes a correction.

## 3. Explicit, Readable Code

- MUST choose names that describe the responsibility of each variable, function, type, and file.
- MUST write control flow that a reader can follow without reconstructing hidden assumptions.
- MUST prefer clear statements over compressed expressions, clever shortcuts, or unexplained abbreviations.
- SHOULD use early returns and small, purpose-specific functions instead of deeply nested conditions or callbacks.
- MUST follow existing repository conventions unless the task explicitly requires changing them.

## 4. Vietnamese, Step-Specific Comments

- MUST write newly authored code comments and documentation comments in Vietnamese. English rule text in this file does not change the comment language.
- MUST explain the reason for each meaningful processing step in new or modified logic: the invariant, business constraint, security requirement, boundary condition, or failure behavior it protects.
- MUST place each explanation immediately before the relevant line or step. Use an inline comment only when it remains clearly tied to that line.
- A step may contain several tightly related statements serving one invariant. Independent decisions or side effects MUST have separate explanations.
- MUST comment security checks, transformations, external calls, retry decisions, state transitions, and non-obvious error handling individually.
- MUST NOT replace step-specific explanations with a single broad comment above a function or block.
- MUST NOT write comments that merely repeat the code. State why the step is needed, not just what it does.
- Imports, braces, straightforward declarations, and obvious assignments do not require artificial comments.
- MUST update or remove a comment when changing the behavior it explains. An inaccurate comment is a rule violation.

Example of the required comment style:

```ts
function login(email, password) {
  const user = findUser(email);
  // Báo cùng một lỗi cho tài khoản không tồn tại và mật khẩu sai để tránh lộ email đã đăng ký.
  if (!user) throw new Error('Email hoặc mật khẩu không đúng.');
  // So sánh với hash đã lưu thay vì lưu hoặc đối chiếu mật khẩu dạng rõ.
  const passwordMatches = verify(user.hash, password);
  if (!passwordMatches) throw new Error('Email hoặc mật khẩu không đúng.');
  // Tạo phiên mới sau xác thực để tránh tái sử dụng phiên do kẻ khác cố định trước đó.
  return createSession(user);
}
```

## 5. Mandatory Compliance Check

Before reporting completion, MUST check every changed file and the final response against all applicable sections above.

- Verify punctuation, descriptive names, and Vietnamese explanations for each meaningful processing step.
- For requested manual tests, verify the case limit, required fields, priority order, status values, and execution evidence.
- Fix violations in your own changes before handing off work. Do not label noncompliant work complete.
- If a requirement cannot be met, report the exact unmet requirement and blocker. Do not silently weaken the rule or fabricate evidence.
- When delegating repository work, MUST include these requirements in the handoff and check the returned changes against them.
