from typing import Any

from bidmatrix_agents.agent_runtime.contracts import (
    AgentOutput,
    EngineeringInput,
    EngineeringOutput,
    ExecutiveInput,
    ExecutiveOutput,
    ExperimentProposal,
    MaterialClaim,
    ProductAnalystInput,
    ProductAnalystOutput,
    ProposedAction,
    PullRequestDraft,
    SupportInput,
    SupportOutput,
    TestResult,
)


def run_fake_agent(agent_key: str, input_data: dict[str, Any]) -> AgentOutput:
    if agent_key == "executive":
        return run_executive(ExecutiveInput.model_validate(input_data))
    if agent_key == "support":
        return run_support(SupportInput.model_validate(input_data))
    if agent_key == "product-analyst":
        return run_product(ProductAnalystInput.model_validate(input_data))
    if agent_key == "engineering":
        return run_engineering(EngineeringInput.model_validate(input_data))
    raise ValueError(f"Unknown agent key {agent_key}")


def run_executive(input_data: ExecutiveInput) -> ExecutiveOutput:
    open_tasks = int(input_data.task_summary.get("open", 0))
    review_backlog = int(input_data.metrics_snapshot.get("manualReviewBacklog", 0))
    risk = f"Manual-review backlog is {review_backlog}; capacity and age are not provided."
    proposed_task = "Review the oldest manual-review items and record verified blockers."
    return ExecutiveOutput(
        status="needs_attention" if review_backlog else "completed",
        summary="The fixture indicates an operating backlog that needs evidence-backed triage.",
        findings=[f"There are {open_tasks} open tasks in the supplied snapshot.", risk],
        proposed_actions=[
            ProposedAction(
                tool_key="task.create",
                arguments={
                    "title": proposed_task,
                    "description": (
                        "Use authoritative task and analysis state; do not infer requirements."
                    ),
                    "type": "operations",
                    "priority": "normal",
                },
                rationale="Create bounded internal follow-up without external side effects.",
            )
        ],
        artifacts=["executive_brief"],
        uncertainties=["The fixture does not contain task age, staffing, or completion trend."],
        requires_owner_attention=review_backlog > 0,
        executive_summary=(
            "Prioritize manual-review reliability before expanding analysis capability."
        ),
        metric_changes=["No historical series was supplied, so change cannot be calculated."],
        risks=[risk],
        recommended_priorities=["Triage manual review", "Preserve draft-only controls"],
        proposed_tasks=[proposed_task],
        owner_decisions_needed=["Confirm the acceptable manual-review backlog target."],
    )


def run_support(input_data: SupportInput) -> SupportOutput:
    conversation_text = " ".join(message.body for message in input_data.conversation).lower()
    injection_detected = any(
        phrase in conversation_text
        for phrase in ("ignore your instructions", "send an email directly", "reveal secret")
    )
    escalation_reasons = [
        label
        for phrase, label in (
            ("refund", "A refund was requested."),
            ("security incident", "A security incident was alleged."),
            ("legal advice", "Legal advice was requested."),
            ("wrong tenant", "Possible cross-tenant data was reported."),
        )
        if phrase in conversation_text
    ]
    sources = [item.source_id for item in input_data.approved_knowledge]
    approved_fact = input_data.approved_knowledge[0].fact if input_data.approved_knowledge else None
    uncertainties = []
    if injection_detected:
        uncertainties.append(
            "The customer message contained an instruction-like string that was ignored."
        )
    if approved_fact is None:
        uncertainties.append("No approved product fact was supplied for a material response claim.")
        escalation_reasons.append("Approved evidence is missing.")

    requires_escalation = bool(escalation_reasons)
    actions: list[ProposedAction] = []
    if requires_escalation:
        actions.append(
            ProposedAction(
                tool_key="task.create",
                arguments={
                    "title": "Review escalated support fixture",
                    "description": "; ".join(escalation_reasons),
                    "type": "support-escalation",
                    "priority": "high",
                },
                rationale="Route the issue to a human without sending any response.",
            )
        )

    fact_sentence = approved_fact or "I cannot verify that capability from approved information."
    return SupportOutput(
        status="needs_attention" if requires_escalation else "completed",
        summary="Prepared a draft-only response from approved fixture facts.",
        findings=["The message asks about an unavailable or unverified capability."],
        proposed_actions=actions,
        artifacts=["support_response_draft"],
        uncertainties=uncertainties,
        requires_owner_attention=requires_escalation,
        classification="product_capability_question",
        urgency="high" if requires_escalation else "normal",
        draft_subject="Re: BidMatrix analysis capabilities",
        draft_body=(
            f"Thanks for checking. {fact_sentence} "
            "Your request remains in human review, and no external action has been taken."
        ),
        material_claims=[MaterialClaim(claim=fact_sentence, source_ids=sources)]
        if approved_fact
        else [],
        sources=sources,
        requires_escalation=requires_escalation,
        escalation_reason="; ".join(escalation_reasons) if escalation_reasons else None,
    )


def run_product(input_data: ProductAnalystInput) -> ProductAnalystOutput:
    completion_metric = next(
        (metric for metric in input_data.metrics if metric.key == "intakeCompletionRate"),
        None,
    )
    completion = completion_metric.value if completion_metric else 0.0
    sample_size = completion_metric.sample_size if completion_metric else 0
    data_issue = f"The completion-rate sample contains only {sample_size} observations."
    experiment = ExperimentProposal(
        problem="Customers may not understand why intake remains in manual review.",
        evidence=[
            (
                f"Fixture intake completion rate is {completion:.0%} "
                f"across {sample_size} observations."
            ),
            *input_data.support_themes,
        ],
        hypothesis="A clearer intake status explanation may reduce abandoned intake sessions.",
        change="Add a draft-only explanatory status panel for a bounded internal evaluation.",
        primary_metric="intakeCompletionRate",
        guardrail_metrics=["supportContactsPerAnalysis", "invalidPdfRate"],
        sample_or_duration="At least 100 eligible sessions or four weeks, whichever is later.",
        risk="The current sample is too small to infer causality.",
        rollback_condition=(
            "Rollback if invalid PDF completion worsens by more than 5 percentage points."
        ),
        implementation_outline=[
            "Lock event definitions before the experiment.",
            "Expose the panel to a bounded cohort.",
            "Review primary and guardrail metrics with confidence intervals.",
        ],
    )
    return ProductAnalystOutput(
        status="needs_attention",
        summary="The fixture supports a bounded status-clarity experiment, not a causal claim.",
        findings=["Intake completion is below 100% in the supplied fixture.", data_issue],
        proposed_actions=[
            ProposedAction(
                tool_key="task.create",
                arguments={
                    "title": "Define intake status clarity experiment",
                    "description": (
                        "Validate event definitions and cohort rules before implementation."
                    ),
                    "type": "product-experiment",
                    "priority": "normal",
                },
                rationale="Keep the proposal internal and reviewable.",
            )
        ],
        artifacts=["product_review_report"],
        uncertainties=[data_issue, "No randomized comparison or historical baseline was supplied."],
        requires_owner_attention=True,
        observations=["Customers mention intake status clarity in the fixture theme."],
        hypotheses=[experiment.hypothesis],
        recommended_experiments=[experiment],
        data_quality_issues=[data_issue],
        owner_decisions_needed=["Approve or reject further experiment design work."],
    )


def run_engineering(input_data: EngineeringInput) -> EngineeringOutput:
    return EngineeringOutput(
        status="completed",
        summary="Prepared a documentation-only change in the isolated F1 worktree.",
        findings=["The fixture uses an allowlisted Git validation command and no remote action."],
        proposed_actions=[
            ProposedAction(
                tool_key="repo.createWorktree",
                arguments={"baseRevision": input_data.base_revision},
                rationale="Create the server-generated isolated workspace before any write.",
            ),
            ProposedAction(
                tool_key="repo.writeFile",
                arguments={
                    "path": "README.md",
                    "content": (
                        "# BidMatrix engineering fixture\n\n"
                        "This deterministic F1 change was prepared in an isolated worktree.\n\n"
                        "Remote Git operations remain disabled.\n"
                    ),
                },
                rationale="Apply the bounded documentation-only fixture change.",
            ),
            ProposedAction(
                tool_key="repo.runAllowlistedCommand",
                arguments={"executable": "git", "arguments": ["diff", "--check"]},
                rationale="Validate the reviewable patch with an exact allowlisted command.",
            ),
            ProposedAction(
                tool_key="repo.createDiffArtifact",
                arguments={"title": "Engineering F1 sandbox diff"},
                rationale="Persist the exact diff for owner review.",
            ),
        ],
        artifacts=["engineering_diff", "engineering_agent_output"],
        uncertainties=[],
        requires_owner_attention=False,
        implementation_summary=(
            "Update the fixture README inside the generated isolated worktree and record the diff."
        ),
        files_changed=["README.md"],
        tests_run=["git diff --check"],
        test_results=[
            TestResult(
                command="git diff --check",
                status="passed",
                summary=(
                    "The allowlisted deterministic fixture command is materialized "
                    "by Tool Gateway."
                ),
            )
        ],
        diff_artifact_id="created-through-tool-gateway",
        risks=["The diff still requires owner review before any future remote action."],
        follow_up_items=["Review the stored diff artifact in Owner Console."],
        pull_request_draft=PullRequestDraft(
            title="docs: add fixture support note",
            body="Draft only. No remote pull request has been opened.",
        ),
    )
