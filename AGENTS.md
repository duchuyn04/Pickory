# Pickory Agent Rules

These rules apply to all work in this repository, including code, tests, comments, documentation, commit messages, UI copy, and responses to the user.

`MUST` and `MUST NOT` are mandatory requirements. `SHOULD` identifies a preference, not permission to bypass a mandatory requirement. A task is not complete while an applicable requirement remains unmet.

## 1. Punctuation

- MUST use the ordinary ASCII hyphen (`-`, U+002D) when joining phrases.
- MUST use commas, colons, parentheses, or separate sentences when a pause is needed.
- MUST NOT write U+2014 (em dash) or U+2013 (en dash) in any newly authored or modified content, including chat responses and examples.
- MUST check changed content for both prohibited characters before reporting completion.
- MUST NOT copy prohibited punctuation from source material into new content.
- MUST NOT separate the final `i` after an accented vowel in a Vietnamese word.
- MUST run `node scripts/check-agent-text.mjs <file...>` on every changed text file before reporting completion.
- MUST check chat responses with the same rule as `scripts/check-agent-text.mjs` before sending them.

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
  // Trả cùng một lỗi khi không có tài khoản. Kẻ tấn công không biết được email nào đã đăng ký.
  if (!user) throw new Error('Email hoặc mật khẩu không đúng.');
  // So sánh với hash đã lưu. Hệ thống không lưu mật khẩu dạng rõ.
  const passwordMatches = verify(user.hash, password);
  if (!passwordMatches) throw new Error('Email hoặc mật khẩu không đúng.');
  // Tạo phiên mới sau khi xác thực. Phiên cũ có thể do kẻ tấn công tạo trước (session fixation).
  return createSession(user);
}
```

## 5. ASD-STE100 Writing Rules for Comments and Documentation

ASD-STE100 (Simplified Technical English) is an English controlled language. This repository keeps Vietnamese for comments and documentation, so it applies the STE writing rules to Vietnamese text. There is no Vietnamese STE dictionary. MUST NOT claim full ASD-STE100 compliance.

### Scope

- Applies to newly authored or modified code comments, documentation comments, and Markdown documentation (`spec/`, `docs/`, `tasks/`, README files).
- Existing text that a task does not touch MAY stay as it is. When a task modifies a sentence, that sentence MUST follow this section.
- Section 4 decides what a comment explains (the reason for a step). This section decides how the sentence is written.

### Words

- MUST use one term for one concept, and keep that term in the whole file. Example: after choosing `phiên`, do not alternate with `session` or `phiên đăng nhập` for the same concept.
- MUST write code identifiers, routes, environment variables, and file names exactly as in the code, inside backticks.
- MUST NOT use slang, idioms, or vague words such as `xử lý`, `làm gì đó`, `cái này`, or `v.v.` when a specific word exists.
- SHOULD use a common Vietnamese word instead of an English loanword when the Vietnamese word is clear. Keep the English term when it is the established technical name, for example `token`, `cache`, or `retry`.
- MUST define an abbreviation at its first use in a document, unless it is a code identifier.
- MAY use these common technical abbreviations without a definition: API, CORS, DB, E2E, HTTP, HTTPS, JSON, SQL, UI, URL, PII, RBAC. In a code comment, MUST NOT spell out an abbreviation that this list covers.

### Sentences

- Count words as space-separated tokens. A code identifier in backticks counts as one word.
- MUST keep each procedural sentence (an instruction or a step) at 20 words or fewer.
- MUST keep each descriptive sentence (an explanation, a reason, a fact) at 25 words or fewer.
- MUST write one instruction per sentence. MUST NOT join two actions with `và` or `rồi` in one instruction.
- MUST write instructions in the imperative form: start with the verb, for example `Chạy`, `Kiểm tra`, `Xóa`.
- SHOULD use the active voice. Name the actor that does the action, for example `API trả lỗi 403`, not `lỗi 403 được trả về`.
- MUST give one topic per sentence and one topic per paragraph.
- MUST keep a paragraph at 6 sentences or fewer.
- MUST NOT stack more than 3 nouns or modifiers in one noun phrase. Split a long phrase into a short phrase and a separate sentence.

### Procedures, warnings, and lists

- MUST write a procedure as a numbered list, one step per item, in the order of execution.
- MUST write a warning or caution before the step it applies to. Start with the instruction, then give the risk in a separate sentence.
- MUST use a list when a sentence would contain more than 3 parallel items.

### Code comments

- MUST keep each comment at one or two sentences. Each sentence follows the length limits above.
- MUST state the reason (rule, risk, or boundary) directly. MUST NOT start a comment with filler such as `Lưu ý là` or `Ở đây chúng ta`.
- MUST use the same term for a concept in comments and in the code identifier that holds it.

Example. Not acceptable (too long, two topics, vague word):

```ts
// Ở đây xử lý việc kiểm tra xem user có tồn tại không và nếu không có thì vẫn trả lỗi giống như sai mật khẩu để hacker không đoán được email nào đã đăng ký trong hệ thống.
if (!user) throw new Error('Email hoặc mật khẩu không đúng.');
```

Acceptable:

```ts
// Trả cùng một lỗi khi không có tài khoản. Kẻ tấn công không biết được email nào đã đăng ký.
if (!user) throw new Error('Email hoặc mật khẩu không đúng.');
```

## 6. Mandatory Compliance Check

Before reporting completion, MUST check every changed file and the final response against all applicable sections above.

- Verify punctuation, descriptive names, and Vietnamese explanations for each meaningful processing step.
- Run `node scripts/check-agent-text.mjs <file...>` on changed text files. Scan the final response for a separated `i` after an accented vowel.
- Verify that new or modified comments and documentation follow section 5: sentence length, one instruction per sentence, consistent terms, and imperative procedures.
- For requested manual tests, verify the case limit, required fields, priority order, status values, and execution evidence.
- Fix violations in your own changes before handing off work. Do not label noncompliant work complete.
- If a requirement cannot be met, report the exact unmet requirement and blocker. Do not silently weaken the rule or fabricate evidence.
- When delegating repository work, MUST include these requirements in the handoff and check the returned changes against them.

## 7. Subagent Model and Effort

- MUST run every spawned subagent on the main session's model and effort. Set `model` and `effort` to those values explicitly, because an agent definition can pin different values.
- MUST use a different model or effort only when the user names that value in the prompt.
- A fork always runs the main session's model and effort. MUST NOT use a fork when the user asks for a different model or effort.
- The Herdr builder runs the model and effort that the user has set up. MUST NOT change them unless the user names a value in the prompt.
