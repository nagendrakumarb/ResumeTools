from pathlib import Path
from grpc_tools import protoc


def generate() -> None:
    project = Path(__file__).resolve().parent
    proto = project.parent.parent / "src" / "ProfessionalHub.AI.Contracts" / "Protos" / "ai_artifact_worker.proto"
    generated = project / "generated"
    generated.mkdir(exist_ok=True)
    (generated / "__init__.py").touch()

    exit_code = protoc.main(
        [
            "grpc_tools.protoc",
            f"-I{proto.parent}",
            f"--python_out={generated}",
            f"--grpc_python_out={generated}",
            str(proto),
        ]
    )
    if exit_code != 0:
        raise SystemExit(exit_code)


if __name__ == "__main__":
    generate()
