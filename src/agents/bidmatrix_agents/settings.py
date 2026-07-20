from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class WorkerSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="BIDMATRIX_WORKER_",
        extra="ignore",
    )

    temporal_address: str = Field(default="localhost:7233", alias="TEMPORAL_ADDRESS")
    temporal_namespace: str = Field(default="default", alias="TEMPORAL_NAMESPACE")
    temporal_task_queue: str = Field(default="bidmatrix-agents", alias="TEMPORAL_TASK_QUEUE")
    health_host: str = "0.0.0.0"  # noqa: S104 - required inside the container network
    health_port: int = 8081
