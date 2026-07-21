from dataclasses import dataclass


@dataclass(frozen=True)
class ScheduleDefinition:
    schedule_id: str
    workflow_name: str
    cron: str
    enabled_by_default: bool


DAILY_EXECUTIVE_BRIEF = ScheduleDefinition(
    schedule_id="daily-executive-brief",
    workflow_name="DailyExecutiveBriefWorkflow",
    cron="0 7 * * 1-5",
    enabled_by_default=False,
)
