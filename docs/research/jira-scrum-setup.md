# Tổ chức Scrum trên Jira Software cho Pickory

Ngày nghiên cứu: 2026-10-03. Đối tượng: nhóm nhỏ làm Pickory, kế hoạch hiện có ở `tasks/plan.md` và `tasks/todo.md` (38 task: T01-T05, ID-01..ID-14, UI-01..UI-08, AI-00..AI-09, E2E-01).

## Cách đọc tài liệu này

Mỗi nhận định được gắn một nhãn nguồn:

- **[Scrum Guide]**: Scrum Guide 2020 chính thức, https://scrumguides.org/scrum-guide.html
- **[Atlassian]**: tài liệu hoặc bài hướng dẫn chính thức của Atlassian (support.atlassian.com, atlassian.com/agile). Đây là khuyến nghị hoặc mô tả tính năng của Atlassian, không phải quy định của Scrum.
- **[Đề xuất Pickory]**: ý kiến của người viết để áp dụng cho dự án, không đến từ nguồn nào.
- **[Chưa kiểm chứng]**: chưa tìm được nguồn chính thống trong đợt nghiên cứu này, cần tự thử trên Jira trước khi tin.

Lưu ý về giao diện: Atlassian đã đổi tên gọi, "issue" thành "work item", "project" thành "space", và nhiều trang cũ vẫn dùng tên cũ. Tên menu, vị trí nút và wizard import có thể khác theo gói Jira, loại space (company-managed hay team-managed) và thời điểm. Nhiều trang hướng dẫn chỉ nói cho company-managed. Khi thấy giao diện khác, hãy tin giao diện thật và đối chiếu lại trang nguồn.

Hạn chế của đợt nghiên cứu: một số trang atlassian.com/agile bị cắt phần thân khi đọc bằng công cụ tóm tắt, nên tôi tải HTML và tách văn bản để đọc. Không dùng blog hay Atlassian Community. Mọi nguồn dùng ở đây nằm ở mục "Danh sách nguồn" cuối file.

---

## 1. Khung Scrum cần nắm (nền để dùng Jira cho đúng)

### 1.1 Những gì Scrum Guide quy định

- Scrum Team gồm một Scrum Master, một Product Owner và Developers, "no sub-teams or hierarchies", thường 10 người hoặc ít hơn. [Scrum Guide]
- Sprint có độ dài cố định, "one month or less". Trong Sprint: không thay đổi làm nguy hại Sprint Goal, chất lượng không giảm, Product Backlog được tinh chỉnh khi cần, phạm vi có thể được làm rõ và thương lượng lại với Product Owner. Chỉ Product Owner có quyền hủy Sprint, khi Sprint Goal trở nên lỗi thời. [Scrum Guide]
- Ba "cam kết" gắn với ba artifact: Product Goal (Product Backlog), Sprint Goal (Sprint Backlog), Definition of Done (Increment). [Scrum Guide]
- Sprint Planning có ba chủ đề: tại sao Sprint này có giá trị (Sprint Goal), có thể làm xong gì, làm bằng cách nào. Sprint Goal phải chốt trước khi Sprint Planning kết thúc. Giới hạn tối đa 8 giờ cho Sprint một tháng. [Scrum Guide]
- Sprint Backlog = Sprint Goal + các mục đã chọn + kế hoạch thực hiện. [Scrum Guide]
- Daily Scrum: 15 phút cho Developers, tập trung vào tiến độ tới Sprint Goal. [Scrum Guide]
- Sprint Review: "a working session", không giới hạn ở việc trình bày, tối đa 4 giờ cho Sprint một tháng. Sprint Retrospective: lập kế hoạch tăng chất lượng và hiệu quả, xem xét cả Definition of Done, tối đa 3 giờ cho Sprint một tháng. Các sự kiện ngắn hơn khi Sprint ngắn hơn. [Scrum Guide]
- Product Backlog refinement là hoạt động liên tục, chia nhỏ và làm rõ mục thành mục nhỏ hơn, chính xác hơn. "The Developers who will be doing the work are responsible for the sizing." [Scrum Guide]
- Mục không đạt Definition of Done thì không được phát hành, thậm chí không được trình bày ở Sprint Review, mà quay lại Product Backlog. [Scrum Guide]
- Scrum Guide nhắc burn-down, burn-up, cumulative flow là các cách dự báo, nhưng không quy định công cụ hay đơn vị ước lượng. Story point, Fibonacci, velocity không có trong Scrum Guide. [Scrum Guide]

Nguồn: https://scrumguides.org/scrum-guide.html

### 1.2 Những gì Atlassian khuyến nghị thêm (không bắt buộc theo Scrum)

- Sprint Planning: không quá 2 giờ cho mỗi tuần của Sprint (Sprint 2 tuần thì tối đa 4 giờ). Backlog refinement là "optional event", với Sprint 2 tuần nên họp refinement giữa Sprint. Kế hoạch "just enough", tập trung vào mục tiêu chứ không kín từng phút của Sprint. Bài này do Dave West (Scrum.org) viết và Atlassian đăng lại. https://www.atlassian.com/agile/scrum/sprint-planning
- Sprint Review: Atlassian dành 30-60 phút cho mỗi lần, họp sau khi Sprint kết thúc và trước retrospective, kiểu demo thân mật, "not an exam". https://www.atlassian.com/agile/scrum/sprint-reviews
- Retrospective: ít nhất 30 phút đến 1 giờ, mọi thành viên tham dự, facilitator có thể luân phiên, kết thúc bằng vài hành động có người chịu trách nhiệm và hạn. https://www.atlassian.com/agile/scrum/retrospectives
- Product Backlog: nhóm "kéo" việc theo năng lực, không bị Product Owner "đẩy"; nên giữ mọi việc của nhóm phát triển trong một backlog duy nhất; roadmap chia thành epic, epic chia thành story. https://www.atlassian.com/agile/scrum/backlogs
- Sprint: Atlassian ghi độ dài "usually 1-4 weeks". https://www.atlassian.com/agile/scrum/sprints

Lưu ý: các con số 2 giờ mỗi tuần, 30-60 phút là của Atlassian, nằm trong giới hạn trần của Scrum Guide nhưng không phải quy tắc Scrum.

---

## 2. Cấu trúc công việc trong Jira: epic, story, task, sub-task

### 2.1 Jira cung cấp gì [Atlassian]

- Phân cấp mặc định ba tầng: Epic, rồi work type chuẩn (Bug, Story, Task), rồi Subtask. Epic có thể chứa story, task, bug; task có thể chứa subtask; subtask chỉ làm con. Space phần mềm có sẵn Epic, Bug, Story, Task, Subtask. Một work item chỉ hiển thị tối đa 500 work item con. Nguồn: https://support.atlassian.com/jira-cloud-administration/docs/what-are-issue-types/
- Epic là "a large body of work", thường kéo dài qua nhiều sprint, phạm vi epic thay đổi là bình thường, khác với sprint. Atlassian khuyên dùng trường `parent` trong JQL để tìm work item thuộc epic thay vì `Epic Link` cũ. Nguồn: https://support.atlassian.com/jira-software-cloud/docs/what-is-an-epic/
- Story point mặc định chỉ gán cho story và epic, không gán cho subtask và bug (quản trị viên có thể đổi qua custom field context). Nguồn: https://support.atlassian.com/jira-software-cloud/docs/what-are-story-points/
- Burndown, velocity và sprint report không tính điểm của subtask, chỉ tính điểm của work item cha. Nguồn: các trang báo cáo ở mục 8.

Hệ quả thực tế [Đề xuất Pickory, suy ra từ hai điểm trên]: đừng ước lượng ở sub-task. Ước lượng ở story hoặc task cấp cha, và kiểm tra xem work type Task trong space của bạn có trường Story point estimate hay không (mặc định theo tài liệu thì không). Nếu không có, hoặc nhờ admin thêm Task vào context của trường, hoặc dùng Story cho mọi mục cần ước lượng.

### 2.2 Ánh xạ cho Pickory [Đề xuất Pickory]

**Epic (7 epic, mỗi epic có một mục tiêu kết thúc rõ).** Epic là nhóm theo giá trị, không phải theo phase, vì phase trong `plan.md` trộn hai track.

| Epic | Nội dung | Task |
|---|---|---|
| E1 Nền tảng | Monorepo, API, DB, web, lint ranh giới | T01, T02, T03, T04, T05 |
| E2 Identity - Lõi RBAC và đăng nhập nhân viên | quyền, phiên nhân viên, audit, khung back-office | ID-01, ID-02, ID-03, ID-04, UI-05 |
| E3 Identity - Tài khoản khách | đăng ký, chống dò mật khẩu, email token, consent, Google, hồ sơ | ID-05..ID-10, UI-01..UI-04 |
| E4 Identity - Quản trị nhân viên và khách | role, mời, gán role, khóa, audit UI | ID-11..ID-14, UI-06..UI-08 |
| E5 AI Gateway - Lõi và độ tin cậy | spike, redactPII, hợp đồng, retry, circuit breaker | AI-00, AI-01, AI-02, AI-03 |
| E6 AI Gateway - Provider, ngân sách, back-office | DeepSeek, ngân sách, chatJson, tool loop, embed, API admin | AI-04..AI-09 |
| E7 Nghiệm thu E2E | Playwright, hai luồng của identity | E2E-01 |

Giữ 7 epic là đủ cho 38 task. Có thể gộp E2 và E3 nếu muốn ít hơn, nhưng đừng chia nhỏ hơn.

**Work type.** Atlassian định nghĩa khá rộng ("Story: smallest unit of work", "Task: work that needs to be done"), nên việc chọn là quy ước của nhóm:

- **Story**: mục có hành vi quan sát được với một người dùng hoặc người vận hành: UI-01..UI-08, ID-02, ID-03, ID-05, ID-09..ID-14, AI-09, E2E-01 có thể là Story hoặc Task.
- **Task**: mục nền hoặc kỹ thuật không có người dùng trực tiếp: T01-T05, ID-01, ID-04, ID-06, ID-07, ID-08, AI-01..AI-08. AI-00 là Task gắn nhãn `spike`.
- **Bug**: lỗi phát sinh khi làm, không có trong todo.md.
- **Sub-task**: không tạo sẵn từ todo.md. Mỗi task đã đủ nhỏ (XS-M, tối đa 5 file). Chỉ tạo sub-task khi một task M bị chia việc cho nhiều người, và ghi nhớ sub-task không được tính điểm.
- Tiêu chí nghiệm thu và "Kiểm tra" của todo.md đưa nguyên vào Description dưới dạng checklist. Atlassian khuyên ghi acceptance criteria ngay trong work item. https://www.atlassian.com/agile/scrum/sprint-reviews

**Cách đặt tên.** Khoá Jira (ví dụ `PICK-17`) tự sinh và không trùng mã trong todo.md, nên:

- Summary: `[ID-05] Đăng ký, đăng nhập khách và UserLinkedEvent`. Giữ mã gốc ở đầu summary để tra chéo với `plan.md` và `todo.md` (giới hạn summary 255 ký tự, nguồn: trang mapping CSV).
- Epic: `E3 Identity - Tài khoản khách`.
- Component: `identity`, `ai-gateway`, `web`, `infra`. Components hỗ trợ nhập nhiều giá trị khi import CSV (nguồn CSV importer).
- Label: `track-a`, `track-b`, `spike`, `checkpoint`, `live-test`, `needs-api-key`.
- Phụ thuộc: tiếp tục ghi "Phụ thuộc" trong Description. Tạo link "blocks / is blocked by" giữa các issue là việc nên làm, nhưng tôi chưa đọc trang Atlassian nào về link type và chưa chắc CSV import tạo được link. [Chưa kiểm chứng] Với 38 task, tạo link thủ công sau import cho các chuỗi chính (ID-01 đến ID-04, AI-02 đến AI-05) là chấp nhận được.
- Open Questions trong `plan.md`: tạo thành Task nhãn `open-question` trong Sprint 1 (xem mục 5), vì câu 1, 2 phải chốt trước Phase 1 và câu 3 chặn AI-00.

---

## 3. Product backlog và refinement

### 3.1 Nguyên tắc

- Product Backlog là danh sách có thứ tự, "emergent", là nguồn công việc duy nhất của Scrum Team. Product Owner chịu trách nhiệm quản lý. [Scrum Guide]
- Jira: backlog của scrum board nằm ở tab Backlog. Tại đây tạo issue, kéo thả để xếp hạng (rank), gán vào sprint, epic hoặc version, ước lượng, sửa hàng loạt. Issue chỉ hiện ở backlog nếu không phải subtask, khớp bộ lọc của board, và trạng thái nằm ở cột không phải Done. [Atlassian] https://support.atlassian.com/jira-software-cloud/docs/use-your-scrum-backlog/
- Với refinement, Atlassian gợi ý: không việc cá nhân nào quá 16 giờ (hoặc khoảng 20 điểm); mục vượt ngưỡng thì chia nhỏ rồi ước lượng lại; mục ở sâu trong backlog chỉ cần ước lượng thô, vì yêu cầu sẽ đổi. [Atlassian] https://www.atlassian.com/agile/project-management/estimation

### 3.2 Áp dụng cho Pickory [Đề xuất Pickory]

- Backlog Pickory đã được chia sẵn tương đối chi tiết, mỗi task có tiêu chí nghiệm thu, kích thước XS/S/M và không task nào vượt 5 file. Vì vậy tại thời điểm import, coi Phase 0 và Phase 1 là "ready", còn Phase 2 trở đi là dạng ước lượng thô, được tinh chỉnh lại trước sprint tương ứng. Điều này khớp tinh thần "just enough planning" của Atlassian.
- Thứ tự backlog: thứ tự phụ thuộc trong `plan.md` đã quyết định phần lớn. Product Owner chỉ cần quyết định chen task của hai track (Track A, Track B) vào cùng sprint như thế nào, theo mục 5.
- Nhịp refinement: một buổi ngắn giữa mỗi sprint (30-45 phút, theo gợi ý giữa Sprint của Atlassian) để xem trước sprint kế tiếp: kiểm tra tiêu chí nghiệm thu, phụ thuộc (đặc biệt API key cho AI-00, câu hỏi mở), và ước lượng.
- Chọn một người làm Product Owner duy nhất (Scrum Guide yêu cầu một người chịu trách nhiệm); người này có thể đồng thời là Developer. Quyết định đó là việc của nhóm, không phải của tài liệu này.

---

## 4. Ước lượng (story points)

- Scrum Guide chỉ nói Developers chịu trách nhiệm sizing, không đòi story point. [Scrum Guide]
- Atlassian: story point là "a subjective unit of measurement that doesn't correlate to any amount of time", đo độ phức tạp, khối lượng, rủi ro một cách tương đối; Atlassian cảnh báo story point bị lạm dụng khi dùng để đánh giá con người hoặc gán lịch chi tiết, và velocity không phải thước đo năng suất. Nên ước lượng tập thể và dùng planning poker cho nhóm mới. [Atlassian] https://support.atlassian.com/jira-software-cloud/docs/what-are-story-points/ và https://www.atlassian.com/agile/project-management/estimation
- Bật estimation cho board: space settings, Features, Estimation, chọn Story points hoặc Time (team-managed); company-managed cấu hình ở board settings. Phải có ước lượng trước khi sprint bắt đầu thì burndown và velocity mới có số. [Atlassian] https://support.atlassian.com/jira-software-cloud/docs/enable-estimation/ (đọc qua kết quả tìm kiếm, nên kiểm tra lại vị trí menu).

**Đề xuất Pickory:** todo.md đã có cỡ XS/S/M (1 file, 1-2 file, 3-5 file). Dùng để khởi tạo điểm ban đầu, rồi để nhóm chỉnh lại khi planning poker:

| Cỡ trong todo.md | Điểm khởi tạo |
|---|---|
| XS | 1 |
| S | 2 |
| M | 5 |

Với cách này: Phase 0 = 19 điểm, Phase 1 = 36, Phase 2 = 35, Phase 3 = 27, Phase 4 = 32, E2E = 5, tổng khoảng 154 điểm cho 38 task. Cỡ theo "số file" là một proxy thô về công sức, chưa tính rủi ro (ví dụ AI-04, AI-07 phụ thuộc kết quả spike). Điểm khởi tạo chỉ là giả định để có số trong sprint đầu. Sau sprint 1 và 2, hãy ước lượng lại các mục chưa làm bằng velocity thật, không giữ mãi con số 1/2/5.

---

## 5. Tạo và lập kế hoạch Sprint

### 5.1 Trong Jira [Atlassian]

- Tạo sprint: tab Backlog, cuộn xuống phần Backlog, chọn "Create sprint". Cần quyền Manage Sprints. https://support.atlassian.com/jira-software-cloud/docs/create-sprints-in-company-managed-projects/
- Bắt đầu sprint: cần ít nhất một work item trong sprint; nhập Sprint name, Start date, End date và Sprint goal (tùy chọn). Sprint goal hiển thị trên Sprint Report và Burndown. Thêm hoặc bỏ item khi sprint đang chạy được tính là thay đổi phạm vi. Muốn nhiều sprint chạy cùng lúc phải bật parallel sprints. https://support.atlassian.com/jira-software-cloud/docs/plan-a-sprint/
- Kết thúc sprint ("Complete sprint"): item chưa xong chuyển về Backlog, sang sprint tương lai hoặc sprint mới. Một item chỉ tính là xong khi nằm ở cột cuối của board; theo tài liệu thì subtask cũng phải Done. Cần quyền Manage Sprints hoặc admin. https://support.atlassian.com/jira-software-cloud/docs/complete-a-sprint/

### 5.2 Điểm lệch giữa Jira và Scrum Guide cần để ý

- Jira coi Sprint goal là trường tùy chọn, còn Scrum Guide coi Sprint Goal là bắt buộc và là cam kết. Hãy điền luôn. [Scrum Guide vs Atlassian]
- Jira không có trường "Definition of Done" riêng (xem mục 7). [Chưa kiểm chứng đầy đủ, chỉ thấy Atlassian hướng dẫn ghi acceptance criteria trong issue]

### 5.3 Đề xuất số sprint và độ dài [Đề xuất Pickory]

- **Độ dài: 2 tuần.** Nằm trong giới hạn Scrum Guide (tối đa một tháng) và vùng 1-4 tuần của Atlassian. Hai tuần đủ cho khoảng 3-5 task cỡ M. Nếu nhóm chỉ có một đến hai người làm bán thời gian, cân nhắc sprint một tuần để vòng phản hồi ngắn hơn, nhưng khi đó điểm mỗi sprint giảm một nửa.
- **Số sprint:** với tổng khoảng 154 điểm và giả định velocity 18-22 điểm cho mỗi sprint (giả định, chưa có số đo), cần khoảng 8-9 sprint, tương đương 16-18 tuần. Chỉ lập kế hoạch chi tiết 3-4 sprint đầu (Atlassian: "just enough" plan); phần sau điều chỉnh bằng velocity thật.
- **Capacity:** nhóm nhỏ nên tính capacity theo người, trừ ngày nghỉ và thời gian sự kiện, trước khi chọn việc. Con số 18-22 ở trên phải được thay bằng số đo của Sprint 1.

### 5.4 Sprint goal gợi ý cho 4 sprint đầu [Đề xuất Pickory]

Mục đích: mỗi sprint có một mục tiêu duy nhất, đo được, và kết thúc ở một trạng thái chạy được (đúng tinh thần Increment). Các sprint được cắt để checkpoint của `todo.md` rơi vào cuối sprint.

**Sprint 1 - "Nền tảng chạy được"** (19 điểm: T01, T02, T03, T04, T05, cộng hai task chốt câu hỏi mở 1 và 2)
- Goal: `pnpm lint && pnpm typecheck && pnpm test && pnpm build` sạch, `pnpm dev` chạy cả web và api, DB test chạy, ESLint chặn import sai ranh giới module.
- Kết thúc bằng Checkpoint 0. Đây cũng là buổi chốt Open Question 1 (dependency ngoài tech stack) và 2 (danh mục quyền do identity giữ), vì Phase 1 phụ thuộc vào đó.
- Nếu xong sớm, kéo AI-01 (redactPII, chỉ cần T02) vào, đây là việc nhỏ và độc lập.

**Sprint 2 - "Nhân viên đăng nhập được, gateway chat qua fake provider"** (21 điểm: ID-01, ID-02, ID-03, AI-00, AI-01, AI-02)
- Goal: nhân viên đăng nhập bằng phiên bảo mật với quyền đúng role; gateway nhận lời gọi chat qua fake provider, đã redactPII và ghi một dòng log mỗi lần gọi; kết quả spike DeepSeek và AI SDK đã ghi vào ADR.
- AI-00 đặt ở sprint này vì nó là rủi ro High trong `plan.md` và cần API key (Open Question 3). Nếu chưa có key, AI-00 bị chặn: đưa việc xin key lên đầu sprint, và dự phòng bằng cách kéo ID-04 vào thay.

**Sprint 3 - "Lõi dùng chung đã sẵn sàng"** (15 điểm: ID-04, UI-05, AI-03)
- Goal: module khác dùng được `requirePermission` và `recordAudit`; nhân viên vào được khung back-office; gateway có retry, timeout và circuit breaker kiểm bằng test.
- Kết thúc bằng Checkpoint 1. Còn dư năng lực nên chừa chỗ kéo ID-05 vào khi sprint đi nhanh.

**Sprint 4 - "Khách đăng ký được, gateway gọi DeepSeek thật"** (19 điểm: ID-05, UI-01, ID-06, ID-07, AI-04)
- Goal: khách đăng ký, đăng xuất, đăng nhập lại qua UI, có chống dò mật khẩu và email token; gateway gọi DeepSeek thật bằng live test.
- UI-02, ID-08, UI-03, AI-05, AI-06 (16 điểm) đẩy sang Sprint 5 và kết thúc bằng Checkpoint 2.

**Dự kiến thô, chưa chốt:** Sprint 6 gần với Phase 3 (27 điểm, có thể tách hai sprint), Sprint 7-8 cho Phase 4 (32 điểm), E2E-01 và Checkpoint cuối ở sprint cuối.

### 5.5 Cách chạy Sprint Planning cho Pickory [Đề xuất Pickory, dựa trên Scrum Guide]

1. Chủ đề 1: Product Owner đưa ra mục tiêu giá trị, cả nhóm viết Sprint Goal một câu và dán vào hộp Sprint goal.
2. Chủ đề 2: Developers chọn mục theo thứ tự backlog, kiểm tra phụ thuộc theo `plan.md`, so tổng điểm với capacity.
3. Chủ đề 3: với mỗi mục M, nói nhanh cách làm. Chỉ chia sub-task khi cần.
4. Timebox: tối đa 4 giờ cho sprint 2 tuần (Atlassian). Nhóm nhỏ thường chỉ cần 1-2 giờ.

---

## 6. Board

- Board scrum có các cột mặc định To Do, In Progress, Done. Mỗi cột ánh xạ tới một hoặc nhiều status; Jira coi công việc xong khi nó nằm ở cột ngoài cùng bên phải, nên mọi status "đã xong" (Done, Cancelled...) phải ánh xạ vào cột cuối, nếu không burndown và velocity sai. [Atlassian] https://support.atlassian.com/jira-software-cloud/docs/configure-columns/
- Đổi cấu hình cột giữa chừng ảnh hưởng báo cáo của sprint (có bài KB riêng về chuyện này). https://support.atlassian.com/jira/kb/how-changing-the-board-column-settings-can-impact-the-sprint-reports/

**Đề xuất Pickory:**

- Bốn cột: To Do, In Progress, In Review, Done. Cột "In Review" khớp với quy ước của `todo.md` là mọi task phải qua `lint`, `typecheck`, `test` trước khi commit và review. Không thêm cột phức tạp hơn khi chỉ có nhóm nhỏ.
- Chỉ có một board scrum cho cả hai track. Dùng component hoặc label `track-a`/`track-b` để lọc. Không cần parallel sprints vì hai track cùng nằm trong một sprint.
- Có thể dùng swimlane hoặc quick filter theo epic, label. Tôi chưa đọc trang chính thức về hai tính năng này nên cần tự kiểm tra. [Chưa kiểm chứng]

---

## 7. Definition of Done

**Scrum Guide:** DoD là mô tả chính thức trạng thái Increment đạt chất lượng yêu cầu. Nếu một mục không đạt DoD thì không được phát hành và không được trình bày ở Sprint Review, nó quay lại backlog. Retrospective xem xét cả DoD. [Scrum Guide]

**Atlassian:** nhấn mạnh văn hóa "done thật sự", acceptance criteria do Product Owner xác định và ghi ngay trong work item, kèm testing notes. [Atlassian] https://www.atlassian.com/agile/scrum/sprint-reviews

**Jira:** "Done" về mặt công cụ chỉ là mục nằm ở cột cuối. Jira không buộc nhóm tuân theo DoD. Việc tuân thủ phụ thuộc vào kỷ luật của nhóm. [Atlassian, Chưa kiểm chứng về việc không có trường DoD riêng]

**Đề xuất Pickory:** DoD chung cho mọi task, rút từ các quy ước đã có trong `tasks/todo.md` và `tasks/plan.md`. Nhóm duyệt rồi lưu thành một trang (ví dụ `docs/` hoặc Confluence) và dán một checklist rút gọn vào mô tả issue qua mẫu mặc định:

1. Mọi tiêu chí nghiệm thu trong mô tả đã tích xong.
2. `pnpm lint && pnpm typecheck && pnpm test` sạch (quy ước của todo.md).
3. Test của task được viết và pass; file `service.ts` và file logic đạt coverage 80% (ngưỡng trong T01 và Checkpoint cuối).
4. Task có live test (AI-04, AI-06, AI-07, AI-08) chạy được với `AI_LIVE_TESTS=1` khi có key.
5. Không có secret trong git; tài liệu và spec đã cập nhật nếu lệch (ví dụ ID-01 cập nhật `SPEC-identity.md`).
6. Task UI đã kiểm tra thủ công trên `pnpm dev`.
7. Đã merge vào `main`, migration (nếu có) đã rebase và chạy lại theo Decision 4 của plan.

Mục thuộc nhóm "một phần" hoặc "hoãn" trong Traceability phải ghi rõ trong mô tả để không bị coi là sót DoD.

---

## 8. Sprint Review, Retrospective và vai trò của Checkpoint

### 8.1 Nguồn

- Sprint Review là buổi làm việc để kiểm tra Increment và thích nghi Product Backlog, không chỉ là buổi trình bày. [Scrum Guide]
- Atlassian: Sprint Review là outward-facing, tập trung vào sản phẩm, người tham gia gồm Scrum Team và các bên liên quan, chương trình điển hình gồm tổng quan Sprint Goal, demo, thảo luận mục chưa xong, cập nhật backlog. Retrospective là buổi nội bộ về quy trình, nên diễn ra sau review. [Atlassian] https://www.atlassian.com/agile/scrum/sprint-reviews và https://www.atlassian.com/agile/scrum/retrospectives
- Báo cáo Sprint Report của Jira có ích khi retrospective: liệt kê việc xong, chưa xong, thêm sau khi bắt đầu và bị bỏ. [Atlassian] https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-sprint-report/

### 8.2 Dùng checkpoint của todo.md làm Sprint Review [Đề xuất Pickory]

Có hai điểm cần xử lý:

1. Scrum Guide đòi mỗi Sprint có Sprint Review và tạo ra Increment đạt DoD. Checkpoint 0 đến 3 và Checkpoint cuối trong `todo.md` rơi theo phase, không theo sprint, nên không phải sprint nào cũng có checkpoint. Nếu chỉ review ở checkpoint thì thiếu review ở các sprint giữa.
2. Vì vậy: mỗi sprint có một Sprint Review ngắn (30-60 phút theo Atlassian). Checkpoint là "cổng chất lượng" gắn vào Sprint Review của sprint rơi trúng checkpoint, không thay thế review thường kỳ.

Cách làm cụ thể trong Jira:

| Checkpoint | Nằm ở | Cách thể hiện trong Jira |
|---|---|---|
| 0 Nền tảng | Cuối Sprint 1 | Task "CP0 Review" nhãn `checkpoint` trong Sprint 1; Version `CP0` |
| 1 Lõi | Cuối Sprint 3 | Task "CP1 Review", Version `CP1` |
| 2 | Cuối Sprint 5 | Task "CP2 Review", Version `CP2` |
| 3 | Cuối sprint chứa UI-04 và AI-09 | Task "CP3 Review", Version `CP3` |
| Cuối | Sau E2E-01 | Task "CP-final", Version `CP-final` |

- Version: Jira cho gán issue vào version ngay ở tab Backlog (nguồn: trang Use your scrum backlog). Gán các task của mỗi phase vào Version tương ứng để có thể xem tiến độ theo mốc. Tôi chưa đọc trang nào mô tả "release" hay "version" chi tiết. [Chưa kiểm chứng]
- Mỗi task "CPx Review" có checklist là các dòng trong mục Checkpoint của `todo.md`; nó chỉ được Done khi mọi dòng tích xong.
- Các dòng "Review với người dùng" trong checkpoint chính là phần "thu thập phản hồi và cập nhật backlog" của Sprint Review.
- Các câu hỏi mở dành cho Checkpoint (ví dụ Checkpoint 1 hỏi về API key) đưa thành mục chương trình họp.
- Task "CPx Review" không cần điểm (0 điểm hoặc không ước lượng), để không làm sai velocity.

### 8.3 Retrospective [Đề xuất Pickory]

- Sau mỗi Sprint Review, họp 30-45 phút, mở Sprint Report và Velocity để xem việc đã xong, chưa xong, thay đổi phạm vi.
- Nhìn lại cả DoD và độ chính xác của điểm ước lượng (gợi ý của Atlassian: lấy 5 story 8 điểm gần nhất, xem có thật sự cùng mức công sức không).
- Kết thúc bằng 1-3 hành động có người chịu trách nhiệm, tạo thành Task nhãn `retro-action` trong sprint kế tiếp.

---

## 9. Báo cáo: burndown, velocity, sprint report

Tất cả theo [Atlassian]. Các báo cáo này là báo cáo của board scrum, tính theo bộ lọc của board.

- **Burndown:** "actual and estimated amount of work to be done in a sprint", trục dọc là chỉ số ước lượng của board (story point, thời gian hoặc số item). Có đường guideline lý tưởng; nếu thiếu guideline thì có thể sprint bắt đầu khi chưa có item. Điểm của subtask không hiện; thêm subtask vào item đang chạy được tính là thay đổi phạm vi, chỉ hiện nếu bật "remaining estimate and time spent". https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-burndown-chart/
- **Velocity:** hiển thị cột Commitment (ước lượng lúc bắt đầu sprint, không gồm phần thêm giữa sprint) và Completed (xong khi sprint kết thúc), đường trung bình, tính bằng trung bình điểm đã hoàn thành qua các sprint gần đây. Nhiều sprint hơn thì dự báo chính xác hơn. https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-velocity-chart/
- **Sprint report:** việc xong, chưa xong, thêm sau khi bắt đầu (đánh dấu sao), bị bỏ; áp dụng cho board scrum của company-managed space. https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-sprint-report/
- Team-managed space có báo cáo sprint burndown riêng (kết quả tìm kiếm trỏ tới https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-team-managed-sprint-burndown-chart/); tôi chưa đọc chi tiết trang này.

**Áp dụng cho Pickory [Đề xuất Pickory]:**

- Sprint 1 và 2 chưa đủ dữ liệu để tin velocity. Dùng 3 sprint đầu để hiệu chỉnh. Không so sánh với con số 18-22 như một mục tiêu.
- Vì ước lượng ở task cha (không ở subtask), đừng chia sub-task nhiều nếu muốn burndown phản ánh đúng tiến độ.
- Velocity là công cụ dự báo của nhóm, không dùng để đánh giá cá nhân (Atlassian cảnh báo điều này ở trang estimation).

---

## 10. Đưa danh sách task có sẵn vào Jira

### 10.1 Hai đường import và giới hạn quan trọng [Atlassian]

1. **CSV importer tạo hàng loạt work item** (từ Filters, Search work items, More actions, "Import work items from CSV"): nhanh, có bước Validate, nhưng tài liệu ghi rõ nó "can't map work items with a parent-child hierarchy relationship". Cần quyền space admin hoặc app admin cùng "Create work items" và "Make bulk changes"; người không phải admin tối đa 250 work item mỗi file. https://support.atlassian.com/jira-software-cloud/docs/create-issues-using-the-csv-importer/
2. **External system import, nguồn CSV** (Settings, System, External system import): hỗ trợ quan hệ cha-con, nhiều space, đính kèm; cỡ lô gợi ý 1.500 work item mỗi file. https://support.atlassian.com/jira-cloud-administration/docs/import-data-from-a-csv-file/

Vì Pickory cần quan hệ Epic, Story/Task nên đi theo **đường 2**. Bài KB của Atlassian cho hướng này ghi: file phải có các cột `Issue type`, `Issue key`, `Issue ID`, `Summary`, `Parent`; các dòng cấp cao nhất (epic) đặt ở trên cùng, rồi work type chuẩn, cuối cùng subtask; ở cột `Parent` của dòng con điền **Issue ID** của cha (không phải Issue key); khi chạy wizard chọn "Switch to the old experience", chọn CSV, rồi ánh xạ `issue id` tới `Issue id` và `parent` tới `Parent`. Sau import, mở một issue con kiểm tra trường Parent và xem mục "Child issues" ở epic. https://support.atlassian.com/jira/kb/keep-issue-parent-child-mapping-during-csv-import-to-jira-cloud/

Tên nút "Switch to the old experience" phụ thuộc phiên bản Jira và có thể đã đổi. Hãy xem giao diện thực tế.

### 10.2 Quy tắc chuẩn bị file [Atlassian]

Nguồn: https://support.atlassian.com/jira-software-cloud/docs/prepare-a-csv-file-for-import/ và trang CSV importer ở trên.

- Bắt buộc có hàng tiêu đề và cột `Summary`; tiêu đề không có dấu câu ngoài dấu phẩy phân cột.
- Ô có dấu phẩy hoặc xuống dòng phải đặt trong dấu nháy kép; dấu nháy kép trong nội dung phải gấp đôi.
- Dùng email để chỉ người (assignee, reporter).
- Giá trị nhiều (Components, Labels, Fix versions): lặp lại cột cùng tên.
- Trường thời gian tính bằng giây. Hạn chế: Summary tối đa 255 ký tự, Description tối đa 30.000 ký tự.
- Wizard cho chọn mã hóa file và dấu phân cách. Với tiếng Việt hãy lưu file UTF-8 (đề xuất của tôi) và kiểm tra bước xem trước.
- Mỗi ID phải duy nhất để quan hệ cha-con đúng.

### 10.3 Vấn đề Sprint khi import [Atlassian]

Cột Sprint trong CSV phải điền **Sprint ID**, không phải tên; dùng tên thì Jira cảnh báo "Sprint id must be a number" và trường Sprint để trống, vì nhiều sprint có thể trùng tên. https://support.atlassian.com/jira/kb/jira-csv-import-sprint-id-must-be-number/

Tôi không tìm thấy tài liệu nói CSV import có thể tạo sprint mới. [Chưa kiểm chứng]

**Đề xuất Pickory:** không import cột Sprint. Import toàn bộ vào backlog, rồi tạo sprint và kéo task vào ở buổi Sprint Planning thật. Cách này vừa tránh lỗi Sprint ID, vừa đúng Scrum (Developers chọn việc ở Sprint Planning, không do file quyết định trước).

### 10.4 Quy trình đề xuất [Đề xuất Pickory]

1. Tạo space Scrum thử (hoặc một space tạm) để chạy thử import với 3-5 dòng, kiểm tra tiếng Việt, hierarchy, story points, components, labels. Chạy bước Validate trước khi import thật.
2. Chuẩn hóa dữ liệu nguồn từ `todo.md` thành bảng 45 dòng (7 epic và 38 task). Đây là việc cơ học nên có thể viết script nhỏ đọc `todo.md` rồi sinh CSV, hoặc làm tay trong bảng tính. Không dùng cột Sprint.
3. Chạy import thật qua External system import. Với 45 dòng thì không chạm giới hạn 250 hay 1.500.
4. Cấu hình board: cột và ánh xạ status (mục 6), bật estimation (mục 4).
5. Kiểm tra: mở vài issue con kiểm tra Parent; mở từng epic xem Child issues; kiểm tra điểm hiển thị đúng ở backlog.
6. Tạo link "blocked by" cho các chuỗi phụ thuộc chính thủ công (xem mục 2.2).
7. Tạo 4 sprint đầu và chạy Sprint Planning thật cho Sprint 1.

### 10.5 Mẫu CSV (rút gọn, chỉ để minh họa cấu trúc)

Các cột `Issue ID`, `Parent`, `Issue type`, `Summary`, `Description`, `Story point estimate`, `Labels`, `Component/s` là tên gợi ý; tên đúng tùy bước ánh xạ trong wizard của bạn. Tên trường điểm ("Story point estimate" hay "Story Points") và việc Task có trường điểm hay không cần kiểm tra thực tế. [Chưa kiểm chứng]

```csv
Issue ID,Issue type,Summary,Parent,Description,Story point estimate,Labels,Labels,Component/s
1,Epic,E1 Nền tảng,,"Monorepo, API, DB, web, lint ranh giới",,,,infra
2,Epic,E3 Identity - Tài khoản khách,,"Đăng ký, đăng nhập khách, email token, consent, Google, hồ sơ",,,,identity
11,Task,[T01] Khởi tạo pnpm workspace và tooling,1,"Tiêu chí:
- [ ] pnpm install, lint, typecheck, test, build chạy sạch
- [ ] Có .env.example",5,track-a,,infra
12,Story,"[ID-05] Đăng ký, đăng nhập khách và UserLinkedEvent",2,"Tiêu chí:
- [ ] Mật khẩu tối thiểu 8 ký tự",5,track-a,,identity
```

Lưu ý: ô chứa dấu phẩy hoặc xuống dòng (như summary của ID-05) phải bọc trong dấu nháy kép, script sinh file cần làm việc này tự động.

---

## 11. Khuyến nghị tóm tắt cho Pickory

1. Dùng một scrum board, 7 epic, Sprint 2 tuần, ước lượng bằng story point khởi tạo 1/2/5 từ cỡ XS/S/M.
2. Mỗi sprint có Sprint Goal đúng một câu và một Sprint Review ngắn; checkpoint là cổng chất lượng gắn vào review của sprint tương ứng, không phải sprint nào cũng có.
3. DoD viết thành văn bản chung, nhúng checklist vào issue.
4. Import qua External system import (không dùng bulk create) để giữ quan hệ epic và task; không import cột Sprint.
5. Sprint 1 là sprint hiệu chỉnh; chưa tin velocity trước Sprint 3.
6. Việc chặn lớn nhất của Sprint 2 là API key cho AI-00 và các Open Question 1-3; xử lý trong Sprint 1.

---

## Danh sách nguồn

Scrum Guide:
- https://scrumguides.org/scrum-guide.html

Atlassian, tài liệu Jira (support.atlassian.com):
- https://support.atlassian.com/jira-software-cloud/docs/what-is-an-epic/
- https://support.atlassian.com/jira-cloud-administration/docs/what-are-issue-types/
- https://support.atlassian.com/jira-software-cloud/docs/use-your-scrum-backlog/
- https://support.atlassian.com/jira-software-cloud/docs/create-sprints-in-company-managed-projects/
- https://support.atlassian.com/jira-software-cloud/docs/plan-a-sprint/
- https://support.atlassian.com/jira-software-cloud/docs/complete-a-sprint/
- https://support.atlassian.com/jira-software-cloud/docs/what-are-story-points/
- https://support.atlassian.com/jira-software-cloud/docs/enable-estimation/ (chỉ đọc mô tả trong kết quả tìm kiếm)
- https://support.atlassian.com/jira-software-cloud/docs/configure-columns/ (chỉ đọc mô tả trong kết quả tìm kiếm)
- https://support.atlassian.com/jira/kb/how-changing-the-board-column-settings-can-impact-the-sprint-reports/ (chỉ đọc tiêu đề)
- https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-burndown-chart/
- https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-velocity-chart/
- https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-sprint-report/
- https://support.atlassian.com/jira-software-cloud/docs/view-and-understand-the-team-managed-sprint-burndown-chart/ (chỉ đọc tiêu đề)
- https://support.atlassian.com/jira-software-cloud/docs/create-issues-using-the-csv-importer/
- https://support.atlassian.com/jira-software-cloud/docs/prepare-a-csv-file-for-import/
- https://support.atlassian.com/jira-software-cloud/docs/mapping-csv-data-to-jira-fields/
- https://support.atlassian.com/jira-cloud-administration/docs/import-data-from-a-csv-file/
- https://support.atlassian.com/jira/kb/keep-issue-parent-child-mapping-during-csv-import-to-jira-cloud/
- https://support.atlassian.com/jira/kb/jira-csv-import-sprint-id-must-be-number/

Atlassian, hướng dẫn Agile (atlassian.com/agile):
- https://www.atlassian.com/agile/scrum/sprint-planning
- https://www.atlassian.com/agile/project-management/estimation
- https://www.atlassian.com/agile/scrum/sprint-reviews
- https://www.atlassian.com/agile/scrum/retrospectives
- https://www.atlassian.com/agile/scrum/backlogs
- https://www.atlassian.com/agile/scrum/sprints
