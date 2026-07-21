from typing import Literal, Self

from pydantic import Field, SecretStr, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class WorkerSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="BIDMATRIX_WORKER_",
        extra="ignore",
    )

    temporal_address: str = Field(default="localhost:7233", alias="TEMPORAL_ADDRESS")
    temporal_namespace: str = Field(default="default", alias="TEMPORAL_NAMESPACE")
    temporal_task_queue: str = Field(default="bidmatrix-agents", alias="TEMPORAL_TASK_QUEUE")
    internal_base_url: str = Field(
        default="http://localhost:8080",
        alias="BIDMATRIX_INTERNAL_BASE_URL",
    )
    internal_service_token: SecretStr = Field(
        default=SecretStr(""),
        alias="INTERNAL_SERVICE_TOKEN",
    )
    event_poll_interval_seconds: float = Field(default=1.0, ge=0.1, le=60.0)
    agent_mode: Literal["deterministic", "live"] = Field(
        default="deterministic",
        alias="BIDMATRIX_AGENT_MODE",
    )
    openai_api_key: SecretStr = Field(default=SecretStr(""), alias="OPENAI_API_KEY")
    openai_model_executive: str = Field(default="", alias="OPENAI_MODEL_EXECUTIVE")
    openai_model_support: str = Field(default="", alias="OPENAI_MODEL_SUPPORT")
    openai_model_product: str = Field(default="", alias="OPENAI_MODEL_PRODUCT")
    openai_model_engineering: str = Field(default="", alias="OPENAI_MODEL_ENGINEERING")
    agent_max_turns: int = Field(default=6, ge=1, le=20)
    agent_timeout_seconds: float = Field(default=60.0, ge=5.0, le=600.0)
    health_host: str = "0.0.0.0"  # noqa: S104 - required inside the container network
    health_port: int = 8081

    @model_validator(mode="after")
    def validate_internal_service_token(self) -> Self:
        if not self.internal_service_token.get_secret_value():
            raise ValueError("INTERNAL_SERVICE_TOKEN is required")
        if self.agent_mode == "live":
            if not self.openai_api_key.get_secret_value():
                raise ValueError("OPENAI_API_KEY is required when BIDMATRIX_AGENT_MODE=live")
            if not all(
                (
                    self.openai_model_executive,
                    self.openai_model_support,
                    self.openai_model_product,
                    self.openai_model_engineering,
                )
            ):
                raise ValueError("All four OPENAI_MODEL_* values are required in live mode")
        return self

    def model_for_agent(self, agent_key: str) -> str:
        if self.agent_mode == "deterministic":
            return f"{agent_key}-deterministic-f0"
        models = {
            "executive": self.openai_model_executive,
            "support": self.openai_model_support,
            "product-analyst": self.openai_model_product,
            "engineering": self.openai_model_engineering,
        }
        try:
            return models[agent_key]
        except KeyError as error:
            raise ValueError(f"Unknown agent key {agent_key}") from error
