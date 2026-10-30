# 可选流程参考

本文件保留此前的详细角色矩阵、阶段表、产物标签和模板，供用户明确要求正式流程时按需选用。以下内容是历史参考，不自动构成项目门禁；如与根目录 `AGENTS.md` 或用户当前要求不一致，以后者为准。使用前应按实际任务明确适用范围、责任人及审批要求，不得虚构产物或审批。

## 一、总则

- **Artifact First**：所有阶段只认标准产物，不以口头结论代替交付。
- **Gate Before Build**：未满足入口条件的 agent 不得越过门禁进入实现。
- **One Role, One Decision**：关键决策必须有唯一主责角色。
- **Read-Only Reviewers**：评审、审批、安全角色默认只读。
- **Traceable Delivery**：每段实现都必须能追溯到需求、设计、架构、测试依据。
- 按任务规模和实际风险确定适用产物；不得为了满足流程虚构功能、设计、审批或验证结果。

## 二、标准 Artifact 标签

禁止使用同义词或中文别名，统一使用以下标签：

| 类别 | 标签 | 说明 |
| --- | --- | --- |
| 产品 | `[artifact:PRD]` | 背景、目标、范围、约束、验收标准 |
| 产品 | `[artifact:UserStory]` | 角色、场景、价值、验收条件 |
| 产品 | `[artifact:Prototype]` | 页面结构、交互流程、状态与异常流 |
| 设计 | `[artifact:DesignSpec]` | Token、视觉规范、组件规则、响应式要求 |
| 设计 | `[artifact:UICode]` | HTML/CSS 原型或高保真实现稿 |
| 设计 | `[artifact:InteractionSpec]` | 状态切换、反馈、动画、空态/错态说明 |
| 架构 | `[artifact:SystemArch]` | 系统边界、模块职责、依赖与数据流 |
| 架构 | `[artifact:DBDesign]` | ER、表结构、约束、索引、迁移策略 |
| 架构 | `[artifact:APIDoc]` | 接口契约、字段、鉴权、错误码 |
| 架构 | `[artifact:TaskBreakdown]` | 面向实现层的任务拆解、依赖与边界 |
| 开发 | `[artifact:ImplementationPlan]` | 目标、改动范围、实现步骤、风险、验证计划 |
| 开发 | `[artifact:FrontendCode]` | 前端实现结果与验证说明 |
| 开发 | `[artifact:BackendCode]` | 后端实现结果与验证说明 |
| 开发 | `[artifact:AICode]` | Prompt、RAG、工作流、模型集成实现 |
| 质量 | `[artifact:Approval]` | 审批结论，仅允许 `APPROVED` / `REJECTED` |
| 质量 | `[artifact:ReviewReport]` | 代码/设计/架构评审结果 |
| 质量 | `[artifact:TestCase]` | 功能、边界、异常、回归用例 |
| 质量 | `[artifact:TestReport]` | 测试结果、阻断项、残余风险 |
| 安全 | `[artifact:SecurityReport]` | 权限、依赖、数据安全与攻击面审计 |
| 运维 | `[artifact:DeploymentPlan]` | 构建、部署、回滚、监控与发布说明 |

## 三、统一执行协议

所有 agent 在开始工作时必须先做入口检查，再决定是“阻塞”还是“交付”。

开发任务先按第六章分级，入口检查和必需产物以对应级别为准；第四、五章描述完整流程中的角色与阶段，不额外增加 XS、S、M 的必需产物。紧急缺陷按 6.5 执行。

### 3.0 产物适用性与保存

- M/L 任务开始时，根据实际范围记录必需产物、不适用的候选产物及原因，并在范围或风险变化时更新。记录可放在现有产物中，不要求单独建文件。
- 不存在的功能无需虚构设计。例如，没有数据库可将 DBDesign 标记为 `N/A`；没有对外 API 可将 APIDoc 标记为 `N/A`。但实际存在的配置格式、协议、进程参数和内部契约仍须在相关设计中说明。
- 第六章要求的 PRD、相关设计或架构、TaskBreakdown、Approval，以及 L 级要求的 Review、Security、QA 结论不得仅以 `N/A` 省略。未验证的内容须如实列为未执行或残余风险，不得声称通过。
- 多个产物可合并在同一文件或可追溯的记录中，每个产物仍须使用对应标准标签和必需字段。不适用项可在适用性记录中注明，无须建立空产物。
- 文档中的角色是职责而非已存在 agent 的保证；记录实际执行者，并按项目实际能力分配任务。

### 3.1 入口检查

每个 agent 开工前必须显式检查：

- 当前任务目标是否明确。
- 必须 artifact 是否齐备。
- 本角色是否有权限处理该任务。
- 是否存在上游审批或评审前置条件。

如果任一条件不满足，必须返回 `BLOCKED`，不得自行脑补上游产物后继续推进。

### 3.2 通用输出头

除 `Approval` 外，所有 artifact 输出都必须包含以下头部字段；阻塞产物还须包含 3.3 中的阻塞字段：

```text
[artifact:<Tag>]
status: READY | BLOCKED | PASS | FAIL
owner: <agent-name>
scope:
- <本次处理范围>
inputs:
- <使用到的上游 artifact 或上下文>
handoff_to:
- <下一个责任角色>
```

字段约束：

- `READY`：该产物已完成，可交付下游。
- `BLOCKED`：当前角色拒绝继续推进，等待上游补齐。
- `PASS` / `FAIL`：仅用于 `ReviewReport`、`SecurityReport`、`TestReport`。

### 3.3 阻塞模板

缺少输入、权限不符或门禁未过时，必须按以下结构返回：

```text
[artifact:<Tag>]
status: BLOCKED
owner: <agent-name>
scope:
- <希望完成但尚未能执行的工作>
inputs:
- <已使用的上游 artifact 或上下文；没有则写 None>
missing_inputs:
- <缺失的 artifact 或上下文>
blocking_reasons:
- <为什么不能继续>
handoff_to:
- <应补齐该输入的角色>
next_action:
- <上游补齐后如何重新进入流程>
```

### 3.4 交付模板

普通非阻塞产物至少应包含以下内容；Approval 和 ReviewReport、SecurityReport、TestReport 分别使用其专项状态和模板。报告保留通用头及适用的交付字段，但以 `PASS` 或 `FAIL` 替代 `READY`，且与 `verdict` 一致。

```text
[artifact:<Tag>]
status: READY
owner: <agent-name>
scope:
- <本次交付范围>
inputs:
- <使用到的输入>
deliverables:
- <产物核心内容摘要>
risks:
- <剩余风险；没有则写 None>
handoff_to:
- <下游角色>
exit_criteria:
- <本阶段完成标准>
```

### 3.5 专项模板

`[artifact:Approval]` 必须使用：

```text
[artifact:Approval]
result: APPROVED | REJECTED
owner: engineering-manager
approver: <实际审批人或获授权的审批角色>
scope:
- <审批范围>
required_inputs:
- <审查过的输入>
not_applicable:
- <不适用的候选产物及原因；没有则写 None>
checklist:
- [ ] PRD 已齐备；Prototype 按任务适用性检查
- [ ] 设计或架构产物已齐备
- [ ] TaskBreakdown 已齐备
- [ ] 实施范围清晰
- [ ] 风险可控
blocking_issues:
- <阻断问题；没有则写 None>
approved_scope:
- <允许进入实现的范围；拒绝时写 None>
handoff_to:
- <实现角色或返回上游角色>
```

`[artifact:ReviewReport]` / `[artifact:SecurityReport]` / `[artifact:TestReport]` 必须包含：

```text
verdict: PASS | FAIL
findings:
- [severity:blocker|high|medium|low] <问题>
must_fix:
- <发布前必须修复的问题；没有则写 None>
can_follow_up:
- <允许后续处理的问题；没有则写 None>
```

`[artifact:ImplementationPlan]` 必须包含：

```text
goal:
- <要实现什么>
changed_areas:
- <模块/文件/接口范围>
steps:
- <实现步骤>
risks:
- <技术风险>
validation:
- <测试与验证方式>
```

`[artifact:FrontendCode]` / `[artifact:BackendCode]` / `[artifact:AICode]` 必须包含：

```text
changed_files:
- <文件或模块>
contracts_affected:
- <接口/表结构/Prompt 契约；没有则写 None>
tests_run:
- <已执行验证；未执行则明确写 Not Run>
known_issues:
- <已知残余问题；没有则写 None>
```

`[artifact:DeploymentPlan]` 必须包含；不适用的步骤或监控项写 `N/A` 并说明原因，不得省略适用的回退和验证安排：

```text
precheck:
- <发布前检查项>
steps:
- <部署步骤>
rollback:
- <回滚步骤>
monitoring:
- <监控与告警项>
owner:
- <发布责任方>
```

## 四、角色矩阵

角色矩阵列出完整流程的候选输入与输出；是否必需按第六章及 3.0 的适用性判断，不适用项无需虚构。一个人或 AI 可依次承担多个角色，但须记录每项产物的角色和实际执行者；同一任务的关键决策须有唯一主责。

同一执行者实现后进行的评审须标明“自查”，不得称为独立评审。需要独立评审时须由独立执行者完成；角色名称本身不能证明独立性。评审、审批和安全角色履职时默认只读，发现问题交还实现角色处理。

### 4.1 产品与决策层

#### senior-ai-agent-pm

- 输入：用户需求、业务上下文。
- 输出：`[artifact:PRD]`、`[artifact:UserStory]`、`[artifact:Prototype]`。
- 禁止：直接编写业务实现代码。
- 交接：`senior-ui-designer`、`tech-lead-architect`。

#### tech-lead-architect

- 输入：`[artifact:PRD]`、`[artifact:Prototype]`，必要时参考设计稿。
- 输出：`[artifact:SystemArch]`、`[artifact:DBDesign]`、`[artifact:APIDoc]`、`[artifact:TaskBreakdown]`。
- 禁止：绕过实现层直接完成主要业务实现。
- 交接：`engineering-manager`、实现层。

#### engineering-manager

- 输入：上游产品、设计、架构、任务拆解产物。
- 输出：`[artifact:Approval]`。
- 禁止：跳过门禁直接放行编码。
- 交接：实现层或退回上游补齐。

### 4.2 设计与实现层

#### senior-ui-designer

- 输入：`[artifact:PRD]`、`[artifact:Prototype]`。
- 输出：`[artifact:DesignSpec]`、`[artifact:UICode]`、`[artifact:InteractionSpec]`。
- 禁止：替代前端完成业务联调。

#### senior-frontend-engineer

- 输入：`[artifact:TaskBreakdown]`、`[artifact:DesignSpec]`、`[artifact:UICode]`、`[artifact:APIDoc]`、`[artifact:Approval]`。
- 输出：`[artifact:ImplementationPlan]`、`[artifact:FrontendCode]`。
- 禁止：审批未通过时直接编码。
- 该审批要求适用于第六章要求审批的任务。

#### senior-backend-engineer

- 输入：`[artifact:TaskBreakdown]`、`[artifact:SystemArch]`、`[artifact:DBDesign]`、`[artifact:APIDoc]`、`[artifact:Approval]`。
- 输出：`[artifact:ImplementationPlan]`、`[artifact:BackendCode]`。
- 禁止：脱离契约自行扩展接口或数据结构。

#### ai-engineer

- 输入：`[artifact:PRD]`、`[artifact:Prototype]`、`[artifact:SystemArch]`、`[artifact:Approval]`。
- 输出：`[artifact:ImplementationPlan]`，必要时输出 `[artifact:AICode]`。
- 禁止：跳过方案设计直接写 Prompt 或集成代码。

### 4.3 评审与质量层

#### code-reviewer

- 输入：实现代码与对应上游设计/契约。
- 输出：`[artifact:ReviewReport]`。
- 权限：默认只读。

#### system-designer

- 输入：`[artifact:SystemArch]`、`[artifact:DBDesign]`、`[artifact:TaskBreakdown]`。
- 输出：`[artifact:ReviewReport]`。
- 权限：默认只读。

#### security-engineer

- 输入：架构、接口、实现代码。
- 输出：`[artifact:SecurityReport]`。
- 权限：默认只读。

#### senior-qa-engineer

- 输入：`[artifact:PRD]`、`[artifact:Prototype]`、`[artifact:APIDoc]`、实现代码、评审结果。
- 输出：`[artifact:TestCase]`、`[artifact:TestReport]`。
- 责任：覆盖主流程、异常流、回归范围。

#### devops-engineer

- 输入：实现代码、测试结果、发布要求。
- 输出：`[artifact:DeploymentPlan]`。
- 禁止：在质量门禁失败时推动正式发布。

## 五、阶段入口/出口条件

| 阶段 | 主责角色 | 入口条件 | 输出 | 出口条件 | 下一角色 |
| --- | --- | --- | --- | --- | --- |
| Product | `senior-ai-agent-pm` | 需求或问题陈述存在 | PRD、UserStory、Prototype | 范围、目标、验收标准明确 | `senior-ui-designer`、`tech-lead-architect` |
| Design | `senior-ui-designer` | PRD、Prototype 齐备 | DesignSpec、UICode、InteractionSpec | 关键页面、状态、组件规则齐备 | `tech-lead-architect`、`engineering-manager` |
| Architecture | `tech-lead-architect` | PRD、Prototype 齐备 | SystemArch、DBDesign、APIDoc、TaskBreakdown | 模块边界、接口、任务拆解清晰 | `engineering-manager` |
| Approval | `engineering-manager` | 产品、设计/架构、任务拆解齐备 | Approval | 结果为 `APPROVED` 或 `REJECTED` | 实现层或上游 |
| Implementation | 实现层 | 对应级别的必需输入完整；M/L 须 `Approval=APPROVED` | 按第六章交付计划和实现说明 | 代码完成且验证说明齐备 | 按适用性进入 Review / Security / QA |
| Review | `code-reviewer`、`system-designer` | 代码或设计产物已提交 | ReviewReport | 阻断问题已识别 | QA / 实现层 |
| Security | `security-engineer` | 架构/接口/代码可审查 | SecurityReport | 高危问题结论明确 | QA / 实现层 |
| QA | `senior-qa-engineer` | 代码与契约可验证 | TestCase、TestReport | 关键路径测试结论明确 | `devops-engineer` |
| Release | `devops-engineer` | 第八章适用的发布门禁通过 | 按任务级别及风险提供发布方案 | 适用的交付、回退及运行检查方案齐备 | 发布执行 |

## 六、任务规模分级

所有开发任务在执行前必须先判断任务规模。任务规模和适用性共同决定必需的 artifact 与门禁。

### 6.1 XS：微小变更

适用范围：

- 文案、注释、格式调整。
- 不改变行为的配置或文档更新。
- 小型样式修正，且不影响布局结构和交互流程。

执行要求：

- 可直接处理。
- 交付时至少说明 `changed_files` 与 `validation`。
- 不需要补齐完整 PRD、设计、架构和 Approval。

### 6.2 S：小型变更

适用范围：

- 局部 bugfix。
- 小范围行为调整。
- 测试补充。
- 不改变公开接口、数据结构、权限模型或发布流程的局部实现。

执行要求：

- 必须补齐最小 `[artifact:ImplementationPlan]`。
- 完成后必须补齐 `[artifact:FrontendCode]`、`[artifact:BackendCode]` 或 `[artifact:AICode]` 中对应的实现说明。
- 必须明确 `tests_run`；未执行测试时写 `Not Run` 并说明原因。

### 6.3 M：中型变更

适用范围：

- 新增局部功能。
- 影响关键用户流程的交互或页面调整。
- 接口契约、数据结构、状态流或核心业务规则发生变化。

执行要求：

- 必须具备相关 `[artifact:PRD]`。
- 必须具备相关设计或架构产物。
- 必须具备 `[artifact:TaskBreakdown]`。
- 必须具备 `[artifact:Approval]` 且结果为 `APPROVED`。
- 完成后应进入 Review / Security / QA 中必要的质量流程。

### 6.4 L：大型变更

适用范围：

- 跨模块功能。
- 架构调整。
- 权限、安全、数据迁移、外部暴露面变更。
- 发布流程、部署架构或监控告警变更。

执行要求：

- 必须覆盖适用风险，具备 PRD、相关设计或架构、TaskBreakdown、Approval、ImplementationPlan 和对应实现说明；其他产物按 3.0 判断适用性。
- 必须包含 Review、Security、QA 结论，并记录实际检查、测试证据、未覆盖范围和残余风险。无法完成必要验证时不得声称通过，应如实阻塞交付或发布。
- 涉及发布时必须补齐 `[artifact:DeploymentPlan]`。
- 不得以紧急缺陷修复为由跳过安全或数据相关审查。

### 6.5 紧急缺陷修复边界

仅当满足以下任一条件时，才可使用紧急缺陷修复例外：

- 生产不可用或核心流程阻断。
- 构建、启动、部署失败导致无法交付。
- 数据错误、权限错误或安全风险正在影响用户。
- 明确的高优先级回归缺陷。

紧急缺陷修复仍必须满足：

- 补齐最小 `[artifact:ImplementationPlan]`。
- 补齐对应实现说明。
- 补齐 `[artifact:ReviewReport]` 与 `[artifact:TestReport]`。
- 若涉及权限、数据或外部暴露面，必须补齐 `[artifact:SecurityReport]`。

当任务规模判断不明确时，默认按更高一级处理。

## 七、编码前置门禁

`senior-frontend-engineer`、`senior-backend-engineer`、`ai-engineer` 开工前先按第六章分级：

- XS：按 6.1 执行，可直接处理。
- S：按 6.2 执行，先补齐最小 `[artifact:ImplementationPlan]`。
- M 和 L：除第六章对应要求外，开工前必须全部满足以下条件：

- 存在 `[artifact:PRD]`。
- 存在与任务相关的设计或架构产物。
- 存在 `[artifact:TaskBreakdown]`。
- 存在 `[artifact:Approval]` 且结果为 `APPROVED`。

对应级别的必需条件未满足时，必须返回 `BLOCKED`，不得继续实现。

紧急缺陷修复例外按 `6.5 紧急缺陷修复边界` 执行。

审批可覆盖边界明确的版本或里程碑，记录审批人、范围、依据及结论。已批准范围内的后续 XS/S 任务按对应分级执行；超出批准范围或新增重大权限、凭据、数据和外部暴露风险时，应更新相关产物并重新审批。审批必须有实际授权和可追溯记录，不得由实现者自行编造；用户可作为有权审批人明确确认，其确认应记录在 Approval 中。

## 八、审批与发布检查清单

### 8.1 审批清单

`engineering-manager` 审批时必须逐项确认：

- [ ] 目标、范围、验收标准明确。
- [ ] 适用的关键页面或交互状态已定义；不适用时说明原因。
- [ ] 适用的 API、配置、协议及数据契约明确；不适用时说明原因。
- [ ] 任务拆解明确到可执行粒度。
- [ ] 风险、依赖和非功能约束被说明。
- [ ] 本次实现范围没有越权扩张。

### 8.2 发布清单

发布前必须满足：

- [ ] 如任务级别要求审批，适用范围内的 `[artifact:Approval]` 为 `APPROVED`。
- [ ] 按任务级别及发布风险完成必要的 `[artifact:ReviewReport]`、`[artifact:SecurityReport]` 和 `[artifact:TestReport]`；L 级三者均必需。
- [ ] 已执行的评审无 blocker，安全检查无高危未决问题，关键路径验证通过；未执行或未覆盖的项目和风险已如实记录并按门禁处理。
- [ ] 发布方案与项目类型相符，覆盖适用的构建、交付、回退、配置或数据保留及运行检查；不适用项说明原因。L 级涉及发布时必须提供 `[artifact:DeploymentPlan]`。

## 九、交接规则

每次角色交接必须明确：

- 当前交付的 artifact 标签。
- 当前状态：`READY` / `BLOCKED` / `APPROVED` / `REJECTED` / `PASS` / `FAIL`。
- 下一角色。
- 下一角色的入口条件是否已满足。
- 是否存在必须先处理的阻断项。

禁止出现“产物已经给了，但不知道谁接”或“下游自己猜是否可以开工”的情况。

## 十、维护规则

新增 agent 时，必须补充：

- 输入 artifact。
- 输出 artifact。
- 是否可写代码。
- 是否参与门禁。
- 阻塞时交还给谁。

修改 artifact 标签时，必须同步更新 `AGENTS.md` 与 `.codex/agents/*.toml`。

禁止在不同 agent prompt 中定义冲突标签、冲突门禁或冲突权限语义。

如果 agent prompt 与本文件冲突，以本文件为准。

