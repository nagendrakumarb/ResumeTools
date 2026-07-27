from concurrent import futures
from pathlib import Path
import sys

import grpc

from capabilities import RUNTIME, TASK_IDS, WORKER_ID
from generate_protos import generate


PROJECT = Path(__file__).resolve().parent
GENERATED = PROJECT / "generated"
if not (GENERATED / "ai_artifact_worker_pb2.py").exists():
    generate()
sys.path.insert(0, str(GENERATED))

import ai_artifact_worker_pb2 as messages  # noqa: E402
import ai_artifact_worker_pb2_grpc as services  # noqa: E402


class ArtifactWorker(services.AiArtifactWorkerServicer):
    def GetCapabilities(self, request, context):
        return messages.CapabilityResponse(
            success=True,
            worker_id=WORKER_ID,
            runtime=RUNTIME,
            task_ids=TASK_IDS,
            message="Worker is available. Feature implementations are intentionally pending.",
        )

    def ExecuteTask(self, request, context):
        supported = request.task_id in TASK_IDS
        message = (
            "Task contract accepted successfully; implementation is intentionally pending."
            if supported
            else "Task was accepted successfully but is not assigned to this worker."
        )
        return messages.TaskResponse(
            success=True,
            task_id=request.task_id,
            message=message,
            artifact_path=request.output_path,
            package_type="placeholder",
            metrics={"placeholder": 1.0},
        )

    def ExportPortablePackage(self, request, context):
        return messages.TaskResponse(
            success=True,
            task_id=request.task_id,
            message="Portable export request accepted successfully; implementation is intentionally pending.",
            artifact_path=request.portable_output_path,
            package_type="placeholder",
            metrics={"placeholder": 1.0},
        )


def serve(address: str = "127.0.0.1:5093") -> None:
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=4))
    services.add_AiArtifactWorkerServicer_to_server(ArtifactWorker(), server)
    server.add_insecure_port(address)
    server.start()
    print(f"{WORKER_ID} listening on {address}")
    server.wait_for_termination()


if __name__ == "__main__":
    serve()
